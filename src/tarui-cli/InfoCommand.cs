using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Tarui.Cli;

/// <summary>
/// Prints environment, toolchain and manifest diagnostics (<c>tarui info</c>).
/// </summary>
internal sealed class InfoCommand
{
    private readonly CliConsole _console;

    public InfoCommand(CliConsole console) => _console = console;

    public async Task<int> RunAsync(CliOptions options)
    {
        var paths = CliPaths.Resolve(options.ManifestPath);

        _console.WriteLine("Tarui CLI");
        _console.WriteLine($"  version:            {CliInfo.Version}");
        _console.WriteLine($"  platform:           {RuntimeInformation.OSDescription}");
        _console.WriteLine($"  architecture:       {RuntimeInformation.OSArchitecture}");
        _console.WriteLine($"  default RID:        {RuntimeIdentifier.ForCurrentPlatform()}");
        _console.WriteLine($"  dotnet:             {await ProbeToolVersionAsync("dotnet", "--version").ConfigureAwait(false) ?? "not found"}");
        _console.WriteLine($"  pnpm:               {await ProbeToolVersionAsync("pnpm", "--version").ConfigureAwait(false) ?? "not found"}");

        _console.Section();
        _console.WriteLine($"Manifest: {paths.ManifestPath}");
        if (!File.Exists(paths.ManifestPath))
        {
            _console.Warn($"Manifest not found at {paths.ManifestPath}. Run from the app directory.");
            return 1;
        }

        AppManifest manifest;
        try
        {
            manifest = AppManifestLoader.Load(paths.ManifestPath);
        }
        catch (CliException exception)
        {
            _console.Error(exception.Message);
            return 1;
        }

        _console.WriteLine($"  name:               {manifest.Product.Name}");
        _console.WriteLine($"  version:            {manifest.Product.Version}");
        _console.WriteLine($"  identifier:         {manifest.Product.Identifier}");
        _console.WriteLine($"  frontendDist:       {paths.ResolveRelative(manifest.Build.FrontendDist)}");
        _console.WriteLine(
            $"  desktopProject:     {ResolveDisplayPath(manifest.Build.DesktopProject, paths)}");
        _console.WriteLine($"  bundle targets:     {string.Join(", ", manifest.Bundle.Targets)}");

        var errors = AppManifestValidator.Validate(manifest, paths.ManifestDirectory);
        if (errors.Count > 0)
        {
            _console.Warn($"Manifest has {errors.Count} validation issue(s):");
            foreach (var error in errors)
            {
                _console.Warn($"  - {error}");
            }

            return 1;
        }

        _console.WriteLine("Manifest OK.");
        return 0;
    }

    private static string ResolveDisplayPath(string? path, CliPaths paths) =>
        string.IsNullOrWhiteSpace(path) ? "(default)" : paths.ResolveRelative(path);

    private static async Task<string?> ProbeToolVersionAsync(string fileName, string argument)
    {
        try
        {
            var result = await ProcessRunner.RunShellAsync(
                $"{fileName} {argument}",
                Environment.CurrentDirectory,
                output: TextWriter.Null,
                error: TextWriter.Null).ConfigureAwait(false);
            return result.ExitCode == 0 ? result.StandardOutput.Trim() : null;
        }
        catch (Exception exception) when (exception is CliException or Win32Exception)
        {
            return null;
        }
    }
}
