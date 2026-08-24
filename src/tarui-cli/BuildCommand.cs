using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Tarui.Contracts;

namespace Tarui.Cli;

/// <summary>
/// Orchestrates <c>tarui build</c>: runs the frontend build, publishes the desktop
/// app self-contained, bundles the configured targets (W2: portable zip) and emits
/// the updater blueprint manifest with checksums.
/// </summary>
internal sealed class BuildCommand
{
    private const string DefaultOutDirectory = "dist";

    private readonly CliConsole _console;

    public BuildCommand(CliConsole console) => _console = console;

    public async Task<int> RunAsync(CliOptions options)
    {
        var paths = CliPaths.Resolve(options.ManifestPath);
        var manifest = ManifestLoader.LoadValidated(paths);
        var rid = options.Rid ?? RuntimeIdentifier.ForCurrentPlatform();
        var outDir = paths.ResolveRelative(options.OutDir ?? DefaultOutDirectory);
        var desktopProject = ManifestLoader.ResolveDesktopProject(manifest, options.Project, paths);
        var frontendDist = paths.ResolveRelative(manifest.Build.FrontendDist);
        var bundleTargets = options.Bundles is { Count: > 0 } ? options.Bundles : manifest.Bundle.Targets;
        var privateKey = LoadPrivateKeyOrNull(options.SignKey);

        await RunBeforeBuildAsync(manifest, paths).ConfigureAwait(false);
        ValidateFrontendDist(frontendDist);
        var binDir = await PublishAsync(desktopProject, rid, outDir, paths).ConfigureAwait(false);
        ValidateCefRuntime(rid, paths);
        SynthesizePermissions(binDir);

        var artifacts = new List<BundleArtifact>();
        foreach (var target in bundleTargets)
        {
            artifacts.AddRange(await BundleAsync(target, manifest, binDir, outDir, rid).ConfigureAwait(false));
        }

        await EmitUpdaterManifestAsync(manifest, artifacts, outDir, privateKey).ConfigureAwait(false);

        _console.Section();
        _console.Info($"Build artifacts are in {outDir}");
        return 0;
    }

    private async Task RunBeforeBuildAsync(AppManifest manifest, CliPaths paths)
    {
        if (string.IsNullOrWhiteSpace(manifest.Build.BeforeBuildCommand))
        {
            _console.Warn("No build.beforeBuildCommand configured; skipping the frontend build.");
            return;
        }

        var (shellFile, shellArguments) = ShellCommand.For(manifest.Build.BeforeBuildCommand);
        var workingDirectory = paths.FrontendWorkingDirectory(manifest.Build);
        _console.Section();
        _console.Info($"Running beforeBuildCommand: {manifest.Build.BeforeBuildCommand}");
        _console.Info($"  cwd: {workingDirectory}");
        var result = await ProcessRunner.RunAsync(
            shellFile,
            shellArguments,
            workingDirectory,
            output: _console.Out,
            error: _console.Out).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            throw new CliException($"beforeBuildCommand failed with exit code {result.ExitCode}.");
        }
    }

    private static void ValidateFrontendDist(string frontendDist)
    {
        if (!File.Exists(Path.Combine(frontendDist, "index.html")))
        {
            throw new CliException(
                $"frontendDist does not contain index.html: {frontendDist}. " +
                "Run the frontend build or fix build.frontendDist in tarui.app.json.");
        }
    }

    private async Task<string> PublishAsync(string desktopProject, string rid, string outDir, CliPaths paths)
    {
        Directory.CreateDirectory(outDir);
        var binDir = Path.Combine(outDir, "bin");
        _console.Section();
        _console.Info($"Publishing desktop app ({rid}, self-contained) ...");
        var arguments = new List<string>
        {
            "publish",
            desktopProject,
            "-c", "Release",
            "-r", rid,
            "--self-contained", "true",
            "-o", binDir
        };
        var result = await ProcessRunner.RunAsync(
            "dotnet",
            arguments,
            paths.ManifestDirectory,
            output: _console.Out,
            error: _console.Out).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            throw new CliException($"dotnet publish failed with exit code {result.ExitCode}.");
        }

        return binDir;
    }

    private void ValidateCefRuntime(string rid, CliPaths paths)
    {
        var runtimeRoot = Path.Combine(paths.ManifestDirectory, "runtime", "cef", rid);
        if (!Directory.Exists(runtimeRoot))
        {
            _console.Warn(
                $"CEF runtime not found at runtime/cef/{rid}. The published app will not start until it exists. " +
                $"Run: ./eng/cef/install-runtime.ps1 -RuntimeIdentifier {rid}");
        }
    }

    /// <summary>Merges all referenced plugins' permission schemas into the publish output.</summary>
    private void SynthesizePermissions(string binDir)
    {
        _console.Section();
        try
        {
            var schema = SchemaSynthesizer.Synthesize(binDir);
            var path = SchemaSynthesizer.Write(binDir, schema);
            var count = schema.Plugins?.Count ?? 0;
            _console.Info(
                count == 0
                    ? "No plugin permission schemas found; skipping (capabilities/*.json remain authoritative)."
                    : $"Synthesized {count} plugin permission schema(s) into {path}");
        }
        catch (CliException exception)
        {
            _console.Warn(exception.Message);
        }
    }

    private async Task<List<BundleArtifact>> BundleAsync(
        string target,
        AppManifest manifest,
        string binDir,
        string outDir,
        string rid)
    {
        _console.Section();
        _console.Info($"Bundling target '{target}' ...");
        return target switch
        {
            "zip" => await BundleZipAsync(manifest, binDir, outDir, rid).ConfigureAwait(false),
            "msix" => await BundleMsixAsync(manifest, binDir, outDir, rid).ConfigureAwait(false),
            _ => throw new CliException($"Unsupported bundle target '{target}'.")
        };
    }

    private async Task<List<BundleArtifact>> BundleZipAsync(AppManifest manifest, string binDir, string outDir, string rid)
    {
        var zipName = $"{ToBundleFileName(manifest.Product.Name)}-{manifest.Product.Version}-{rid}.zip";
        var zipPath = Path.Combine(outDir, zipName);
        if (File.Exists(zipPath))
        {
            File.Delete(zipPath);
        }

        _console.Info($"Creating {zipName} ...");
        await Task.Run(() => ZipFile.CreateFromDirectory(binDir, zipPath, CompressionLevel.Optimal, false))
            .ConfigureAwait(false);

        var sha256 = await ComputeSha256Async(zipPath).ConfigureAwait(false);
        _console.Info($"Wrote {zipName} (sha256 {sha256[..16]}...).");
        return
        [
            new BundleArtifact(
                RelativeName: zipName,
                AbsolutePath: zipPath,
                Sha256: sha256)
        ];
    }

    private async Task<List<BundleArtifact>> BundleMsixAsync(AppManifest manifest, string binDir, string outDir, string rid)
    {
        var exe = LocateAppExecutable(manifest.Product.Name, binDir);
        _console.Info($"Packaging MSIX (unsigned unless a certificate is configured) from {binDir} ...");
        var result = await MsixPacker.PackAsync(manifest, exe, binDir, outDir, rid).ConfigureAwait(false);
        var relativeName = Path.GetFileName(result.Path);
        _console.Info(
            result.Signed
                ? $"Wrote and signed {relativeName} (sha256 {result.Sha256[..16]}...)."
                : $"Wrote unsigned {relativeName} (sha256 {result.Sha256[..16]}...).");
        return
        [
            new BundleArtifact(
                RelativeName: relativeName,
                AbsolutePath: result.Path,
                Sha256: result.Sha256)
        ];
    }

    /// <summary>
    /// Writes <c>latest.json</c> using the same schema and ECDSA-P384/SHA-384 signature algorithm the
    /// runtime <c>UpdaterService</c> verifies, so the CLI's output is directly consumable by the
    /// updater. When no <c>--sign-key</c> is supplied the manifest is still emitted but with an
    /// empty signature, and the build is flagged so a CI consumer can fail before publishing.
    /// </summary>
    private async Task EmitUpdaterManifestAsync(
        AppManifest manifest,
        List<BundleArtifact> artifacts,
        string outDir,
        ECDsa? privateKey)
    {
        if (artifacts.Count == 0)
        {
            _console.Warn("No bundle artifacts were produced; skipping latest.json.");
            return;
        }

        var orderedArtifacts = artifacts
            .OrderBy(static artifact => artifact.RelativeName, StringComparer.Ordinal)
            .ToArray();

        var files = orderedArtifacts.Select(static artifact => artifact.RelativeName).ToArray();
        var sha256Table = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var artifact in orderedArtifacts)
        {
            sha256Table[artifact.RelativeName] = artifact.Sha256;
        }

        var unsigned = new LatestManifestDto
        {
            SchemaVersion = UpdateContracts.SchemaVersion,
            Version = manifest.Product.Version,
            Files = files,
            Sha256 = sha256Table,
            Signature = string.Empty,
        };

        string signature = string.Empty;
        if (privateKey is not null)
        {
            var canonical = CanonicalizeForSigning(unsigned);
            signature = Convert.ToBase64String(privateKey.SignData(canonical, HashAlgorithmName.SHA384));
        }

        var signed = unsigned with { Signature = signature };
        var json = System.Text.Json.JsonSerializer.Serialize(signed, TaruiCliJsonContext.Default.LatestManifestDto);
        var latestPath = Path.Combine(outDir, "latest.json");
        await File.WriteAllTextAsync(latestPath, json).ConfigureAwait(false);

        if (privateKey is null)
        {
            _console.Warn(
                "Wrote latest.json without a signature because --sign-key was not provided. " +
                "The runtime UpdaterService will reject this manifest until signing is configured.");
        }
        else
        {
            _console.Info($"Wrote signed latest.json (version {signed.Version}, {files.Length} file(s)).");
        }
    }

    /// <summary>
    /// Produces the deterministic byte stream the runtime <c>UpdateVerifier</c> signs/checks; both
    /// the CLI producer and the desktop consumer must agree on this exact canonicalization.
    /// </summary>
    internal static byte[] CanonicalizeForSigning(LatestManifestDto manifest)
    {
        var builder = new StringBuilder();
        builder.Append(manifest.SchemaVersion).Append('\n');
        builder.Append(manifest.Version).Append('\n');
        foreach (var file in manifest.Files)
        {
            builder.Append(file).Append('\n');
        }
        foreach (var pair in manifest.Sha256.OrderBy(static p => p.Key, StringComparer.Ordinal))
        {
            builder.Append(pair.Key).Append('=').Append(pair.Value).Append('\n');
        }
        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    private static ECDsa? LoadPrivateKeyOrNull(string? base64)
    {
        if (string.IsNullOrWhiteSpace(base64))
        {
            return null;
        }

        try
        {
            var pkcs8 = Convert.FromBase64String(base64);
            var key = ECDsa.Create();
            key.ImportPkcs8PrivateKey(pkcs8, out _);
            return key;
        }
        catch (CryptographicException exception)
        {
            throw new CliException(
                $"--sign-key is not a valid base64 PKCS#8 ECDSA private key: {exception.Message}");
        }
        catch (FormatException exception)
        {
            throw new CliException(
                $"--sign-key must be base64-encoded: {exception.Message}");
        }
    }

    private static string LocateAppExecutable(string productName, string binDir)
    {
        var direct = Path.Combine(binDir, $"{productName}.exe");
        if (File.Exists(direct))
        {
            return $"{productName}.exe";
        }

        var exes = Directory.GetFiles(binDir, "*.exe", SearchOption.TopDirectoryOnly);
        var matching = exes
            .Where(path => string.Equals(Path.GetFileNameWithoutExtension(path), productName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path.Length)
            .FirstOrDefault();
        return Path.GetFileName(matching ?? exes.First(path => !path.EndsWith(".api.exe", StringComparison.OrdinalIgnoreCase)));
    }

    private static string ToBundleFileName(string name)
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

    internal sealed record BundleArtifact(string RelativeName, string AbsolutePath, string Sha256);
}
