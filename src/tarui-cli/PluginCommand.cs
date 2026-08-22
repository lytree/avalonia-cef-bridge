namespace Tarui.Cli;

/// <summary>
/// Handles <c>tarui plugin</c> sub-commands. <c>init</c> scaffolds a plugin
/// skeleton (design §8.4); <c>pack</c> runs pre-flight checks before shipping.
/// </summary>
internal sealed class PluginCommand
{
    private readonly CliConsole _console;

    public PluginCommand(CliConsole console) => _console = console;

    public Task<int> RunAsync(CliOptions options)
    {
        return options.PluginAction switch
        {
            PluginAction.Init => RunInitAsync(options),
            PluginAction.Pack => RunPackAsync(options),
            _ => throw new CliUsageException("tarui plugin requires a sub-command (init | pack).")
        };
    }

    private Task<int> RunInitAsync(CliOptions options)
    {
        var name = options.PluginName;
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new CliUsageException("tarui plugin init requires a plugin name, e.g. 'tarui plugin init store'.");
        }

        var outputDir = Path.GetFullPath(options.Output ?? Environment.CurrentDirectory);
        _console.Section();
        _console.Info($"Scaffolding plugin '{name}' ...");
        var root = PluginScaffolder.Scaffold(name, outputDir, options.Local, flat: options.Output is not null);
        _console.Info($"Created plugin at {root}");
        _console.Info("Next steps:");
        _console.Command($"cd {Path.GetFileName(root)}");
        _console.Command($"dotnet build {Path.Combine(Path.GetFileName(root), "src")}");
        _console.Command("tarui plugin pack");
        return Task.FromResult(0);
    }

    private async Task<int> RunPackAsync(CliOptions options)
    {
        var pluginRoot = options.Output is null
            ? Environment.CurrentDirectory
            : Path.GetFullPath(options.Output);
        _console.Section();
        _console.Info($"Running pre-flight checks for plugin at {pluginRoot}");

        var layout = PluginPacker.Detect(pluginRoot);
        var failures = new List<string>();

        var permissionErrors = PluginPacker.ValidatePermissions(
            Path.Combine(layout.PermissionsDirectory, "schema.json"),
            Path.Combine(layout.PermissionsDirectory, "default.json"));
        foreach (var error in permissionErrors)
        {
            failures.Add("permissions: " + error);
        }

        var versionProblem = PluginPacker.CheckVersionConsistency(layout);
        if (versionProblem is not null)
        {
            failures.Add(versionProblem);
        }

        if (failures.Count > 0)
        {
            foreach (var failure in failures)
            {
                _console.Error(failure);
            }

            return 1;
        }

        _console.Info("Layout, permissions and versions look consistent.");

        if (await RunSelfTestsAsync(layout).ConfigureAwait(false) is var selfTestFailed && selfTestFailed)
        {
            return 1;
        }

        if (await PackBackendAsync(layout, options).ConfigureAwait(false) is var packFailed && packFailed)
        {
            return 1;
        }

        if (await PackGuestJsAsync(layout).ConfigureAwait(false) is var guestFailed && guestFailed)
        {
            return 1;
        }

        _console.Section();
        _console.Info("Pre-flight checks passed. Publish with: dotnet nuget push <nupkg> + npm publish.");
        return 0;
    }

    private async Task<bool> RunSelfTestsAsync(PluginLayout layout)
    {
        var testProjects = Directory.GetFiles(Path.Combine(Path.GetDirectoryName(layout.CsprojPath)!, "..", "..", "tests"), "*.csproj", SearchOption.AllDirectories);
        if (testProjects.Length == 0)
        {
            _console.Warn("No tests/ project found; skipping plugin self-tests.");
            return false;
        }

        foreach (var testProject in testProjects)
        {
            _console.Section();
            _console.Info($"Running plugin self-tests: {testProject}");
            var result = await ProcessRunner.RunAsync(
                "dotnet",
                ["run", "--project", testProject],
                workingDirectory: Path.GetDirectoryName(testProject),
                output: _console.Out,
                error: _console.Out).ConfigureAwait(false);
            if (result.ExitCode != 0)
            {
                _console.Error($"Plugin self-tests failed with exit code {result.ExitCode}.");
                return true;
            }
        }

        return false;
    }

    private async Task<bool> PackBackendAsync(PluginLayout layout, CliOptions options)
    {
        var output = Path.Combine(Path.GetDirectoryName(layout.CsprojPath)!, "..", "..", "dist", "pack", "nuget");
        Directory.CreateDirectory(output);
        _console.Section();
        _console.Info("Packaging backend (dotnet pack) ...");

        var packArguments = new List<string> { "pack", layout.CsprojPath, "-c", "Release", "-o", output };
        var result = await ProcessRunner.RunAsync(
            "dotnet",
            packArguments,
            workingDirectory: Path.GetDirectoryName(layout.CsprojPath),
            output: _console.Out,
            error: _console.Out).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            _console.Error($"dotnet pack failed with exit code {result.ExitCode}.");
            return true;
        }

        var nupkg = Directory.GetFiles(output, "*.nupkg", SearchOption.TopDirectoryOnly).FirstOrDefault();
        if (nupkg is null)
        {
            _console.Error("dotnet pack did not produce a .nupkg.");
            return true;
        }

        if (!PackageContainsPermissions(nupkg))
        {
            _console.Error("The NuGet package does not contain its permissions/ descriptors.");
            return true;
        }

        _console.Info($"Packed {Path.GetFileName(nupkg)} with permissions/.");
        return false;
    }

    private async Task<bool> PackGuestJsAsync(PluginLayout layout)
    {
        var guestJs = Path.GetDirectoryName(layout.GuestPackageJson)!;
        _console.Section();
        _console.Info("Packaging frontend (npm pack) ...");
        var result = await ProcessRunner.RunShellAsync(
            "npm pack --json",
            guestJs,
            output: _console.Out,
            error: _console.Out).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            _console.Error($"npm pack failed with exit code {result.ExitCode}.");
            return true;
        }

        _console.Info("Packed guest-js package.");
        return false;
    }

    private static bool PackageContainsPermissions(string nupkgPath)
    {
        using var archive = System.IO.Compression.ZipFile.OpenRead(nupkgPath);
        return archive.Entries.Any(static entry =>
            entry.FullName.StartsWith("permissions/", StringComparison.OrdinalIgnoreCase));
    }
}