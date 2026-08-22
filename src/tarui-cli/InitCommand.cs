namespace Tarui.Cli;

using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>Scaffolds a new Tarui application from the <c>tarui-app</c> template (<c>tarui init</c>).</summary>
internal sealed class InitCommand
{
    private const string TemplateShortName = "tarui-app";
    private const string TemplateFolder = "react-ts";
    private const string DefaultManager = "pnpm";

    private readonly CliConsole _console;

    public InitCommand(CliConsole console) => _console = console;

    public async Task<int> RunAsync(CliOptions options)
    {
        var name = options.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new CliUsageException("tarui init requires an application name, e.g. 'tarui init my-app'.");
        }

        ValidateName(name);
        var csharpName = ProjectName.ToIdentifier(name, "App");
        var manager = string.IsNullOrWhiteSpace(options.Manager) ? DefaultManager : options.Manager;

        var outputDir = Path.GetFullPath(options.Output ?? Path.Combine(Environment.CurrentDirectory, name));
        if (Directory.Exists(outputDir))
        {
            throw new CliException($"Target directory already exists: {outputDir}");
        }

        string? localTemplate = null;
        var repoRoot = string.Empty;
        if (!string.IsNullOrWhiteSpace(options.Local))
        {
            repoRoot = Path.GetFullPath(options.Local);
            localTemplate = Path.Combine(repoRoot, "src", "templates", "Tarui.Templates");
            if (!File.Exists(Path.Combine(localTemplate, ".template.config", "template.json")))
            {
                throw new CliException($"Local template not found at {localTemplate}.");
            }

            await InstallTemplateAsync(localTemplate).ConfigureAwait(false);
        }

        _console.Command($"dotnet new {TemplateShortName} -n {csharpName} -o {outputDir}");
        var instantiate = await ProcessRunner.RunAsync(
            "dotnet",
            ["new", TemplateShortName, "-n", csharpName, "-o", outputDir],
            Environment.CurrentDirectory,
            output: _console.Out,
            error: _console.ErrorWriter).ConfigureAwait(false);
        if (instantiate.ExitCode != 0)
        {
            var hint = string.IsNullOrWhiteSpace(options.Local)
                ? $"\nHint: install the template with 'dotnet new install Tarui.Templates' or use '--local <tarui-source-dir>'. "
                : string.Empty;
            throw new CliException(
                $"Failed to scaffold '{name}':{Environment.NewLine}{instantiate.StandardOutput}{instantiate.StandardError}{hint}");
        }

        var manifestPath = Path.Combine(outputDir, "tarui.app.json");
        PatchManifest(manifestPath, name);

        var desktopProject = Path.Combine(outputDir, csharpName + ".Desktop", csharpName + ".Desktop.csproj");
        if (!string.IsNullOrWhiteSpace(options.Local))
        {
            RewriteLocal(desktopProject, repoRoot);
        }

        await InstallFrontendDependenciesAsync(Path.Combine(outputDir, "web"), manager, name).ConfigureAwait(false);

        _console.Section();
        _console.Info($"Created application '{name}' ({TemplateFolder}) at {outputDir}");
        _console.Info("Next steps:");
        _console.Command($"cd {name}");
        _console.Command("tarui dev");
        return 0;
    }

    private async Task InstallTemplateAsync(string templatePath)
    {
        _console.Info("Installing local Tarui template...");
        var result = await ProcessRunner.RunAsync(
            "dotnet",
            ["new", "install", templatePath, "--force"],
            Environment.CurrentDirectory,
            output: _console.Out,
            error: _console.ErrorWriter).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            throw new CliException(
                $"Failed to install the Tarui template:{Environment.NewLine}{result.StandardOutput}{result.StandardError}");
        }
    }

    private async Task InstallFrontendDependenciesAsync(string webDir, string manager, string appName)
    {
        if (!Directory.Exists(webDir))
        {
            _console.Warn($"No web directory found; skipping dependency install at {webDir}.");
            return;
        }

        _console.Info($"Installing frontend dependencies with {manager}...");
        ProcessResult result;
        try
        {
            result = await ProcessRunner.RunAsync(
                manager,
                ["install"],
                webDir,
                output: _console.Out,
                error: _console.ErrorWriter).ConfigureAwait(false);
        }
        catch (System.ComponentModel.Win32Exception) when (manager == "pnpm")
        {
            _console.Warn("pnpm was not found on PATH. Install Node.js + pnpm, then retry with: cd <app>/web && pnpm install");
            return;
        }

        if (result.ExitCode != 0)
        {
            _console.Warn($"'{manager} install' failed. Retry later with: cd {appName}/web && {manager} install");
        }
    }

    private void RewriteLocal(string desktopProject, string repoRoot)
    {
        if (!File.Exists(desktopProject))
        {
            throw new CliException($"Desktop project not found at {desktopProject}.");
        }

        _console.Info("Rewriting package references to the local source tree...");
        LocalReferenceRewriter.RewriteFile(desktopProject, repoRoot);
    }

    private static void PatchManifest(string manifestPath, string name)
    {
        if (!File.Exists(manifestPath))
        {
            throw new CliException($"Generated manifest not found at {manifestPath}.");
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(File.ReadAllText(manifestPath));
        }
        catch (JsonException)
        {
            throw new CliException("The generated tarui.app.json could not be parsed.");
        }

        var product = root?["product"]?.AsObject();
        if (product is null)
        {
            throw new CliException("Unexpected tarui.app.json layout: the product section is missing.");
        }

        // The template engine lower-cases the placeholder during scaffolding, so patch by structure
        // rather than by matching the template's original literal values.
        product["name"] = name;
        product["identifier"] = ProjectName.ToIdentifierName(name);
        File.WriteAllText(
            manifestPath,
            root!.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void ValidateName(string name)
    {
        foreach (var ch in name)
        {
            if (!char.IsLetterOrDigit(ch) && ch is not ('-' or '_' or '.'))
            {
                throw new CliUsageException(
                    $"Invalid application name '{name}'. Use letters, digits, '-', '_' or '.'.");
            }
        }
    }
}