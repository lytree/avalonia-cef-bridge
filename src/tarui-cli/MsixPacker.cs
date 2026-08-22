using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace Tarui.Cli;

/// <summary>Result of an MSIX packaging run.</summary>
internal sealed record MsixPackageResult(string Path, string Sha256, bool Signed);

/// <summary>
/// Managed MSIX packer (design §5.5 / W5). Uses no external tooling: an MSIX is a ZIP
/// (OPC) carrying four package-level parts — <c>[Content_Types].xml</c>,
/// <c>AppxManifest.xml</c>, <c>AppxBlockMap.xml</c> and the payload (the publish output).
/// All entries are stored uncompressed so the block-map hashes are exact. Windows SDK
/// (makeappx/signtool) is therefore optional: Authenticode signing runs only when a
/// certificate and signtool are configured (the store/distribution prerequisite).
/// </summary>
internal static class MsixPacker
{
    internal const int BlockSize = 65536; // bytes per block in the block map (MSIX default)

    private const string BlockMapNamespace = "http://schemas.microsoft.com/appx/2010/blockmap";

    /// <summary>Common OPC default content types; anything else falls back to octet-stream.</summary>
    private static readonly Dictionary<string, string> KnownDefaultTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["png"] = "image/png",
            ["jpg"] = "image/jpeg",
            ["jpeg"] = "image/jpeg",
            ["ico"] = "image/vnd.microsoft.icon",
            ["gif"] = "image/gif",
            ["svg"] = "image/svg+xml",
            ["json"] = "application/json",
            ["webmanifest"] = "application/manifest+json",
            ["html"] = "text/html",
            ["css"] = "text/css",
            ["js"] = "application/javascript",
            ["mjs"] = "application/javascript",
            ["map"] = "application/json",
            ["txt"] = "text/plain",
            ["md"] = "text/markdown",
            ["ttf"] = "font/ttf",
            ["otf"] = "font/otf",
            ["woff"] = "font/woff",
            ["woff2"] = "font/woff2",
            ["eot"] = "application/vnd.ms-fontobject",
            ["wasm"] = "application/wasm",
            ["pdb"] = "application/octet-stream",
        };

    /// <summary>
    /// Packs the publish output under <paramref name="binDir"/> into an MSIX written to
    /// <paramref name="outDir"/> named <c>&lt;name&gt;-&lt;version&gt;-&lt;rid&gt;.msix</c>.
    /// </summary>
    public static async Task<MsixPackageResult> PackAsync(
        AppManifest manifest,
        string appExe,
        string binDir,
        string outDir,
        string rid)
    {
        var fileName = $"{BundleName(manifest.Product.Name)}-{manifest.Product.Version}-{rid}.msix";
        var packagePath = Path.Combine(outDir, fileName);
        if (File.Exists(packagePath))
        {
            File.Delete(packagePath);
        }

        Directory.CreateDirectory(outDir);

        var payload = await Task.Run(() => EnumeratePayload(binDir)).ConfigureAwait(false);
        var appxManifest = BuildAppxManifest(manifest, appExe, rid);
        var contentTypes = BuildContentTypes(payload);

        // The block map covers every in-package file except the OPC root part
        // ([Content_Types].xml) and the block map itself.
        var blockMap = BuildBlockMap(payload, appxManifest);

        // [Content_Types].xml must be the first entry. Everything is stored uncompressed.
        using (var stream = File.Create(packagePath))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            WriteEntry(archive, "[Content_Types].xml", contentTypes);
            WriteEntry(archive, "AppxBlockMap.xml", blockMap);

            foreach (var file in payload)
            {
                WriteFileEntry(archive, file.RelativePath, file.FilePath);
            }

            WriteEntry(archive, "AppxManifest.xml", appxManifest);
        }

        var signed = await SignIfConfiguredAsync(manifest, packagePath).ConfigureAwait(false);
        var sha256 = await ComputeSha256Async(packagePath).ConfigureAwait(false);
        return new MsixPackageResult(packagePath, sha256, signed);
    }

    /// <summary>Re-validates that the on-disk package's block-map hashes match the stored files.</summary>
    public static bool VerifyBlockMap(string packagePath)
    {
        using var stream = File.OpenRead(packagePath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var blockMapEntry = archive.GetEntry("AppxBlockMap.xml");
        if (blockMapEntry is null)
        {
            return false;
        }

        string blockMap;
        using (var blockReader = new StreamReader(blockMapEntry.Open()))
        {
            blockMap = blockReader.ReadToEnd();
        }

        // Recompute the SHA-256 over the manifest and every payload file (they are stored
        // uncompressed, so hashes over the file bytes are exact), then require an exact
        // textual match — the simplest, dependency-free block-map validation.
        var entries = new List<string> { "AppxManifest.xml" };
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName;
            if (name is "[Content_Types].xml" or "AppxBlockMap.xml")
            {
                continue;
            }

            if (name == "AppxManifest.xml")
            {
                continue;
            }

            entries.Add(name);
        }

        var expected = BuildBlockMapFromFiles(
            archive,
            entries.ToArray());
        return blockMap.Replace("\r\n", "\n", StringComparison.Ordinal)
                       .Equals(expected.Replace("\r\n", "\n", StringComparison.Ordinal), StringComparison.Ordinal);
    }

    /// <summary>Generates the <c>AppxManifest.xml</c> for a full-trust (unpackaged-style) desktop app.</summary>
    internal static string BuildAppxManifest(AppManifest manifest, string appExe, string rid)
    {
        var product = manifest.Product;
        var description = string.IsNullOrWhiteSpace(manifest.Bundle.ShortDescription)
            ? product.Name
            : manifest.Bundle.ShortDescription;
        var publisher = manifest.Bundle.Msix?.Publisher ?? "CN=Tarui";
        var publisherDisplay = ParseInlineSubject(publisher) ?? product.Name;
        var version = ToFourPartVersion(product.Version);

        return
            $$"""
            <?xml version="1.0" encoding="utf-8"?>
            <Package
              xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
              xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
              xmlns:desktop="http://schemas.microsoft.com/appx/manifest/desktop/windows10"
              xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities"
              IgnorableNamespaces="uap desktop">
              <Identity Name="{{XmlEscape(product.Identifier)}}" Publisher="{{XmlEscape(publisher)}}"
                        Version="{{version}}" ProcessorArchitecture="{{ToArchitecture(rid)}}" />
              <Properties>
                <DisplayName>{{XmlEscape(product.Name)}}</DisplayName>
                <PublisherDisplayName>{{XmlEscape(publisherDisplay)}}</PublisherDisplayName>
                <Description>{{XmlEscape(description)}}</Description>
              </Properties>
              <Resources>
                <Resource Language="en-US" />
              </Resources>
              <Dependencies>
                <TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.17763.0" MaxVersionTested="10.0.22621.0" />
              </Dependencies>
              <Capabilities>
                <rescap:Capability Name="runFullTrust" />
              </Capabilities>
              <Applications>
                <Application Id="App" Executable="{{XmlEscape(appExe)}}" EntryPoint="Windows.FullTrustApplication">
                  <uap:VisualElements DisplayName="{{XmlEscape(product.Name)}}"
                                      Description="{{XmlEscape(description)}}"
                                      BackgroundColor="transparent" />
                </Application>
              </Applications>
              <Extensions>
                <desktop:Extension Category="windows.fullTrustProcess" Executable="{{XmlEscape(appExe)}}">
                  <desktop:FullTrustProcess />
                </desktop:Extension>
              </Extensions>
            </Package>
            """;
    }

    private static List<(string RelativePath, string FilePath, long Size)> EnumeratePayload(string binDir)
    {
        var files = new List<(string RelativePath, string FilePath, long Size)>();
        foreach (var file in Directory.GetFiles(binDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(binDir, file).Replace('\\', '/');
            files.Add((relative, file, new FileInfo(file).Length));
        }

        files.Sort(static (a, b) => string.CompareOrdinal(a.RelativePath, b.RelativePath));
        return files;
    }

    private static string BuildContentTypes(IReadOnlyList<(string RelativePath, string FilePath, long Size)> payload)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        builder.AppendLine("<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">");
        builder.AppendLine("  <Default Extension=\"xml\" ContentType=\"application/vnd.ms-appx.blockmap+xml\" />");

        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in payload)
        {
            var extension = Path.GetExtension(file.RelativePath);
            if (extension.Length > 1 && extensions.Add(extension[1..]))
            {
                var contentType = KnownDefaultTypes.TryGetValue(extension[1..], out var known)
                    ? known
                    : "application/octet-stream";
                builder.AppendLine(CultureInfo.InvariantCulture, $"  <Default Extension=\"{XmlEscape(extension[1..])}\" ContentType=\"{contentType}\" />");
            }
        }

        builder.AppendLine("  <Override PartName=\"AppxManifest.xml\" ContentType=\"application/vnd.ms-appx.manifest+xml\" />");
        builder.Append("</Types>");
        return builder.ToString();
    }

    private static string BuildBlockMap(
        IReadOnlyList<(string RelativePath, string FilePath, long Size)> payload,
        string appxManifest)
    {
        var manifestBytes = Encoding.UTF8.GetBytes(appxManifest);
        var bytesByPath = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["AppxManifest.xml"] = manifestBytes,
        };
        var order = new List<string> { "AppxManifest.xml" };
        foreach (var file in payload)
        {
            bytesByPath[file.RelativePath] = File.ReadAllBytes(file.FilePath);
            order.Add(file.RelativePath);
        }

        var builder = new StringBuilder();
        builder.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"no\"?>");
        builder.Append($"<BlockMap xmlns=\"{BlockMapNamespace}\" ");
        builder.AppendLine("HashMethod=\"http://www.w3.org/2001/04/xmlenc#sha256\">");

        foreach (var name in order)
        {
            AppendBlockMapFile(builder, name, bytesByPath[name].LongLength, bytesByPath[name]);
        }

        builder.Append("</BlockMap>");
        return builder.ToString();
    }

    private static void AppendBlockMapFile(StringBuilder builder, string name, long size, byte[] bytes)
    {
        builder.Append(CultureInfo.InvariantCulture, $"  <File Name=\"{name}\" Size=\"{size}\">");
        for (var offset = 0; offset < bytes.Length; offset += BlockSize)
        {
            var count = Math.Min(BlockSize, bytes.Length - offset);
            var hash = sha256Hex(bytes.AsSpan(offset, count));
            builder.Append(CultureInfo.InvariantCulture, $"<Block Hash=\"{hash}\" />");
        }

        builder.AppendLine("</File>");
    }

    private static string BuildBlockMapFromFiles(ZipArchive archive, IReadOnlyList<string> files)
    {
        var builder = new StringBuilder();
        builder.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"no\"?>");
        builder.Append($"<BlockMap xmlns=\"{BlockMapNamespace}\" ");
        builder.AppendLine("HashMethod=\"http://www.w3.org/2001/04/xmlenc#sha256\">");
        foreach (var name in files)
        {
            var entry = archive.GetEntry(name) ?? throw new CliException($"Missing package part: {name}");
            using var input = entry.Open();
            var bytes = new byte[entry.Length];
            input.ReadExactly(bytes);
            builder.Append(CultureInfo.InvariantCulture, $"  <File Name=\"{name}\" Size=\"{bytes.Length}\">");
            for (var offset = 0; offset < bytes.Length; offset += BlockSize)
            {
                var count = Math.Min(BlockSize, bytes.Length - offset);
                builder.Append(CultureInfo.InvariantCulture, $"<Block Hash=\"{sha256Hex(bytes.AsSpan(offset, count))}\" />");
            }

            builder.AppendLine("</File>");
        }

        builder.Append("</BlockMap>");
        return builder.ToString();
    }

    private static void WriteEntry(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.NoCompression);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private static void WriteFileEntry(ZipArchive archive, string entryName, string filePath)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.NoCompression);
        using (var target = entry.Open())
        using (var source = File.OpenRead(filePath))
        {
            source.CopyTo(target);
        }
    }

    private static async Task<bool> SignIfConfiguredAsync(AppManifest manifest, string packagePath)
    {
        var msix = manifest.Bundle.Msix;
        if (string.IsNullOrWhiteSpace(msix?.CertificatePath))
        {
            // No certificate configured; the package is emitted unsigned. Store / wide
            // distribution requires Authenticode signing (cert procurement + signtool).
            return false;
        }

        var signtool = WindowsSdkToolFinder.Find("signtool.exe");
        if (signtool is null)
        {
            throw new CliException(
                "bundle.msix.certificate.path is set, but signtool.exe was not found. " +
                "Install the Windows SDK and re-run, or remove the certificate to emit an unsigned package.");
        }

        var args = new List<string>
        {
            "sign",
            "/fd", "SHA256",
            "/f", msix.CertificatePath,
        };
        if (!string.IsNullOrWhiteSpace(msix.CertificatePassword))
        {
            args.Add("/p");
            args.Add(msix.CertificatePassword);
        }

        if (!string.IsNullOrWhiteSpace(msix.TimeStamperUrl))
        {
            args.Add("/tr");
            args.Add(msix.TimeStamperUrl);
            args.Add("/td");
            args.Add("SHA256");
        }

        args.Add(packagePath);
        var result = await ProcessRunner.RunAsync(signtool, args).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            throw new CliException($"Code signing failed (exit {result.ExitCode}): {result.StandardError.Trim()}");
        }

        return true;
    }

    private static string ToFourPartVersion(string version)
    {
        var parts = version.Split('.');
        return parts.Length switch
        {
            >= 4 => $"{parts[0]}.{parts[1]}.{parts[2]}.{parts[3]}",
            3 => $"{parts[0]}.{parts[1]}.{parts[2]}.0",
            2 => $"{parts[0]}.{parts[1]}.0.0",
            1 => $"{parts[0]}.0.0.0",
            _ => "0.0.0.0",
        };
    }

    private static string ToArchitecture(string rid)
    {
        return rid switch
        {
            { } r when r.Contains("arm64", StringComparison.OrdinalIgnoreCase) => "arm64",
            { } r when r.Contains("arm", StringComparison.OrdinalIgnoreCase) => "arm",
            { } r when r.Contains("x64", StringComparison.OrdinalIgnoreCase) => "x64",
            { } r when r.Contains("x86", StringComparison.OrdinalIgnoreCase) => "x86",
            _ => "neutral",
        };
    }

    private static string? ParseInlineSubject(string subject)
    {
        foreach (var segment in subject.Split(','))
        {
            var trimmed = segment.Trim().Trim('"');
            if (trimmed.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed[3..];
            }
        }

        return null;
    }

    private static string BundleName(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (var character in name)
        {
            builder.Append(char.IsLetterOrDigit(character) || character is '.' or '_' or '-' ? character : '-');
        }

        return builder.ToString();
    }

    private static string XmlEscape(string value)
        => value.Replace("&", "&amp;", StringComparison.Ordinal)
                .Replace("<", "&lt;", StringComparison.Ordinal)
                .Replace(">", "&gt;", StringComparison.Ordinal)
                .Replace("\"", "&quot;", StringComparison.Ordinal)
                .Replace("'", "&apos;", StringComparison.Ordinal);

    private static string sha256Hex(ReadOnlySpan<byte> bytes)
        => Convert.ToHexString(SHA256.HashData(bytes));

    private static async Task<string> ComputeSha256Async(string filePath)
    {
        await using var stream = File.OpenRead(filePath);
        var hash = await SHA256.HashDataAsync(stream).ConfigureAwait(false);
        return Convert.ToHexStringLower(hash);
    }
}