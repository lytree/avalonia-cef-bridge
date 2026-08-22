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

    private Task<int> RunPackAsync(CliOptions options)
    {
        // Pre-flight checks only for now; actual packaging is refined in W4.
        _console.Warn("tarui plugin pack is not fully implemented yet; no packaging was produced.");
        return Task.FromResult(0);
    }
}