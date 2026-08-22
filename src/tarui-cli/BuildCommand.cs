using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

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

        await RunBeforeBuildAsync(manifest, paths).ConfigureAwait(false);
        ValidateFrontendDist(frontendDist);
        var binDir = await PublishAsync(desktopProject, rid, outDir, paths).ConfigureAwait(false);
        ValidateCefRuntime(rid, paths);

        foreach (var target in bundleTargets)
        {
            await BundleAsync(target, manifest, binDir, outDir, rid).ConfigureAwait(false);
        }

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

    private async Task BundleAsync(
        string target,
        AppManifest manifest,
        string binDir,
        string outDir,
        string rid)
    {
        _console.Section();
        _console.Info($"Bundling target '{target}' ...");
        switch (target)
        {
            case "zip":
                await BundleZipAsync(manifest, binDir, outDir, rid).ConfigureAwait(false);
                break;
            case "msix":
                _console.Warn("MSIX bundling is planned for W5 and is not available yet.");
                break;
            default:
                throw new CliException($"Unsupported bundle target '{target}'.");
        }
    }

    private async Task BundleZipAsync(AppManifest manifest, string binDir, string outDir, string rid)
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
        var latest = new LatestManifestDto
        {
            Version = manifest.Product.Version,
            Url = zipName,
            Sha256 = sha256,
            Signature = string.Empty
        };
        var json = System.Text.Json.JsonSerializer.Serialize(latest, TaruiCliJsonContext.Default.LatestManifestDto);
        await File.WriteAllTextAsync(Path.Combine(outDir, "latest.json"), json).ConfigureAwait(false);
        _console.Info($"Wrote latest.json (sha256 {sha256[..16]}...).");
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
}
