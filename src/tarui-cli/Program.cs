namespace Tarui.Cli;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var console = new CliConsole();
        try
        {
            var options = CommandLineParser.Parse(args);
            return options.Command switch
            {
                TaruiCommand.Help => ShowHelp(console),
                TaruiCommand.Version => ShowVersion(console),
                TaruiCommand.Init => await new InitCommand(console).RunAsync(options),
                TaruiCommand.Plugin => await new PluginCommand(console).RunAsync(options),
                TaruiCommand.Info => await new InfoCommand(console).RunAsync(options),
                TaruiCommand.Dev => await new DevCommand(console).RunAsync(options),
                TaruiCommand.Build => await new BuildCommand(console).RunAsync(options),
                _ => ShowHelp(console)
            };
        }
        catch (CliUsageException exception)
        {
            console.Error(exception.Message);
            console.Error("Run 'tarui --help' for usage.");
            return 2;
        }
        catch (CliException exception)
        {
            console.Error(exception.Message);
            return 1;
        }
        catch (OperationCanceledException)
        {
            console.Warn("Cancelled.");
            return 130;
        }
        catch (Exception exception)
        {
            console.Error($"Unexpected failure: {exception.Message}");
            return 1;
        }
    }

    private static int ShowHelp(CliConsole console)
    {
        console.WriteLine(CliInfo.HelpText);
        return 0;
    }

    private static int ShowVersion(CliConsole console)
    {
        console.WriteLine($"tarui-cli {CliInfo.Version}");
        return 0;
    }
}
