using System.Security.Cryptography;
using System.Text;

namespace Tarui.Cli;

/// <summary>
/// Result of assembling a macOS application bundle.
/// </summary>
internal sealed record MacOsBundleResult(string BundlePath, string TarGzPath, string Sha256);

/// <summary>
/// Assembles a macOS <c>.app</c> bundle directory from the dotnet publish output and emits the
/// <c>Info.plist</c> declared in <see cref="InfoPlistBuilder"/>. Notarization and stapling are
/// intentionally out of scope (Windows-first CLI; the operator notarizes before distribution).
/// </summary>
internal static class MacOsBundleBuilder
{
    /// <summary>Allowed RID values for the macOS bundle target.</summary>
    public static readonly IReadOnlySet<string> AllowedRids = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "osx-x64",
        "osx-arm64",
    };

    /// <summary>
    /// Validates the target RID and assembles the <c>&lt;name&gt;.app</c> directory under
    /// <paramref name="outDir"/>. Returns the bundle path plus the SHA-256 of the <c>tar.gz</c>
    /// archive the updater blueprint records.
    /// </summary>
    public static async Task<MacOsBundleResult> BuildAsync(
        AppManifest manifest,
        string binDir,
        string outDir,
        string rid)
    {
        if (!AllowedRids.Contains(rid))
        {
            throw new CliException(
                $"bundle.targets 'app-bundle' requires an osx-x64 or osx-arm64 RID, got '{rid}'.");
        }

        var bundleName = $"{SanitizeBundleName(manifest.Product.Name)}.app";
        var bundlePath = Path.Combine(outDir, bundleName);
        if (Directory.Exists(bundlePath))
        {
            Directory.Delete(bundlePath, recursive: true);
        }

        var contents = Path.Combine(bundlePath, "Contents");
        var macOsSubDir = Path.Combine(contents, "MacOS");
        var resourcesDir = Path.Combine(contents, "Resources");
        Directory.CreateDirectory(macOsSubDir);
        Directory.CreateDirectory(resourcesDir);

        // Copy the publish payload (executables + dlls + CEF runtime + web dist) into Contents/Resources
        // so the bundle keeps the same layout that Tarui.Hosting's WebView attacher expects at runtime.
        CopyDirectory(binDir, resourcesDir);

        var executableName = manifest.Bundle.MacOs?.ExecutableName ?? manifest.Product.Name;
        var onDisk = Directory.GetFiles(resourcesDir, executableName, SearchOption.TopDirectoryOnly)
            .FirstOrDefault();
        if (onDisk is null)
        {
            throw new CliException(
                $"bundle.macOS.executableName '{executableName}' was not found under {resourcesDir}. " +
                "Adjust bundle.macOS.executableName or set bundle.desktopProject to publish the matching assembly name.");
        }

        // Promote the executable into Contents/MacOS so the bundle is launchable via Finder.
        var targetExe = Path.Combine(macOsSubDir, executableName);
        File.Copy(onDisk, targetExe, overwrite: true);
        TrySetExecutableBit(targetExe);

        var infoPlist = InfoPlistBuilder.Build(manifest, rid);
        var infoPlistPath = Path.Combine(contents, "Info.plist");
        await File.WriteAllTextAsync(infoPlistPath, infoPlist).ConfigureAwait(false);

        // PkgInfo carries the legacy "APPL????" signature used by older macOS launchers; harmless when
        // CFBundlePackageType / CFBundleSignature already carry the canonical form.
        await File.WriteAllTextAsync(
            Path.Combine(contents, "PkgInfo"),
            "APPL????",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)).ConfigureAwait(false);

        var archiveName = $"{SanitizeBundleName(manifest.Product.Name)}-{manifest.Product.Version}-{rid}.app.tar.gz";
        var archivePath = Path.Combine(outDir, archiveName);
        if (File.Exists(archivePath))
        {
            File.Delete(archivePath);
        }

        // tar + gzip the bundle via System.Formats.Tar (net10 BCL). The resulting archive preserves
        // the bundle directory layout; macOS `tar -xzf` rehydrates it to <name>.app/Contents/...
        await Task.Run(() => TarArchive.WriteGzip(bundlePath, archivePath)).ConfigureAwait(false);

        var sha256 = await ComputeSha256Async(archivePath).ConfigureAwait(false);
        return new MacOsBundleResult(bundlePath, archivePath, sha256);
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, directory);
            Directory.CreateDirectory(Path.Combine(destination, relative));
        }

        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            File.Copy(file, Path.Combine(destination, relative), overwrite: true);
        }
    }

    private static void TrySetExecutableBit(string path)
    {
        // chmod is unavailable on Windows; cross-builds rely on the operator's macOS runner to
        // re-chmod during notarization. The bit is also preserved when the publish output is
        // already executable (native macOS publish).
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(path,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }
        }
        catch
        {
            // Best-effort: the file is still on disk and chmod can be reapplied later.
        }
    }

    private static string SanitizeBundleName(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (var character in name)
        {
            builder.Append(char.IsLetterOrDigit(character) || character is '.' or '_' or '-' ? character : '-');
        }

        return builder.ToString();
    }

    private static async Task<string> ComputeSha256Async(string filePath)
    {
        await using var stream = File.OpenRead(filePath);
        var hash = await SHA256.HashDataAsync(stream).ConfigureAwait(false);
        return Convert.ToHexStringLower(hash);
    }
}