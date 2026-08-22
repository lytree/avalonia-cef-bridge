namespace Tarui.Cli;

/// <summary>
/// Hand-written command-line parser (zero dependencies, per design §12-3).
/// Supports <c>--option value</c>, <c>--option=value</c> and bare flags.
/// </summary>
internal static class CommandLineParser
{
    public static CliOptions Parse(IReadOnlyList<string> args)
    {
        var positional = new List<string>();
        string? manifestPath = null;
        string? project = null;
        var noWatch = false;
        var verbose = false;
        string? rid = null;
        List<string>? bundles = null;
        string? outDir = null;
        string? template = null;
        string? manager = null;
        string? output = null;
        string? local = null;

        var index = 0;
        while (index < args.Count)
        {
            var arg = args[index];
            if (arg == "--")
            {
                for (index++; index < args.Count; index++)
                {
                    positional.Add(args[index]);
                }

                break;
            }

            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                var name = arg;
                string? inlineValue = null;
                var equals = arg.IndexOf('=');
                if (equals >= 0)
                {
                    name = arg[..equals];
                    inlineValue = arg[(equals + 1)..];
                }

                switch (name)
                {
                    case "--help":
                        return new CliOptions { Command = TaruiCommand.Help };
                    case "--version":
                        return new CliOptions { Command = TaruiCommand.Version };
                    case "--config":
                        manifestPath = ReadOptionValue(name, inlineValue, args, ref index);
                        break;
                    case "--project":
                        project = ReadOptionValue(name, inlineValue, args, ref index);
                        break;
                    case "--no-watch":
                        noWatch = true;
                        break;
                    case "--verbose":
                        verbose = true;
                        break;
                    case "--rid":
                        rid = ReadOptionValue(name, inlineValue, args, ref index);
                        break;
                    case "--bundle":
                        bundles = ReadOptionValue(name, inlineValue, args, ref index)
                            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                            .ToList();
                        break;
                    case "--out":
                        outDir = ReadOptionValue(name, inlineValue, args, ref index);
                        break;
                    case "--template":
                        template = ReadOptionValue(name, inlineValue, args, ref index);
                        break;
                    case "--manager":
                        manager = ReadOptionValue(name, inlineValue, args, ref index);
                        break;
                    case "--output":
                        output = ReadOptionValue(name, inlineValue, args, ref index);
                        break;
                    case "--local":
                        local = ReadOptionValue(name, inlineValue, args, ref index);
                        break;
                    default:
                        throw new CliUsageException($"Unknown option '{name}'.");
                }

                index++;
                continue;
            }

            if (arg == "-h")
            {
                return new CliOptions { Command = TaruiCommand.Help };
            }

            if (arg == "-V")
            {
                return new CliOptions { Command = TaruiCommand.Version };
            }

            if (arg.StartsWith('-'))
            {
                throw new CliUsageException($"Unknown option '{arg}'.");
            }

            positional.Add(arg);
            index++;
        }

        var command = positional.Count == 0 ? TaruiCommand.Help : ParseCommand(positional[0]);
        var commandArgs = positional.Skip(1).ToArray();

        // tarui init accepts a single positional application name.
        if (command == TaruiCommand.Init)
        {
            if (commandArgs.Length > 1)
            {
                throw new CliUsageException(
                    $"Unexpected argument(s) for 'init': {string.Join(' ', commandArgs.Skip(1))}");
            }

            return new CliOptions
            {
                Command = command,
                Name = commandArgs.Length == 1 ? commandArgs[0] : null,
                Template = template,
                Manager = manager,
                Output = output,
                Local = local,
                Verbose = verbose
            };
        }

        // tarui plugin <init|pack> [<name>]
        if (command == TaruiCommand.Plugin)
        {
            if (commandArgs.Length == 0 || string.IsNullOrWhiteSpace(commandArgs[0]))
            {
                throw new CliUsageException(
                    "tarui plugin requires a sub-command (init | pack). See 'tarui --help'.");
            }

            var action = commandArgs[0] switch
            {
                "init" => PluginAction.Init,
                "pack" => PluginAction.Pack,
                _ => throw new CliUsageException(
                    $"Unknown plugin sub-command '{commandArgs[0]}'. Expected 'init' or 'pack'.")
            };

            // plugin init takes one positional plugin name; pack takes none.
            var remaining = commandArgs.Skip(1).ToArray();
            if (action == PluginAction.Init)
            {
                if (remaining.Length > 1)
                {
                    throw new CliUsageException(
                        $"Unexpected argument(s) for 'plugin init': {string.Join(' ', remaining.Skip(1))}");
                }

                return new CliOptions
                {
                    Command = command,
                    PluginAction = action,
                    PluginName = remaining.Length == 1 ? remaining[0] : null,
                    Output = output,
                    Local = local,
                    Verbose = verbose
                };
            }

            if (remaining.Length > 0)
            {
                throw new CliUsageException(
                    $"Unexpected argument(s) for 'plugin pack': {string.Join(' ', remaining)}");
            }

            return new CliOptions
            {
                Command = command,
                PluginAction = action,
                Verbose = verbose
            };
        }

        if (commandArgs.Length > 0)
        {
            throw new CliUsageException(
                $"Unexpected argument(s) for '{positional[0]}': {string.Join(' ', commandArgs)}");
        }

        return new CliOptions
        {
            Command = command,
            ManifestPath = manifestPath,
            Project = project,
            NoWatch = noWatch,
            Verbose = verbose,
            Rid = rid,
            Bundles = bundles,
            OutDir = outDir
        };
    }

    private static TaruiCommand ParseCommand(string value) => value switch
    {
        "dev" => TaruiCommand.Dev,
        "build" => TaruiCommand.Build,
        "info" => TaruiCommand.Info,
        "init" => TaruiCommand.Init,
        "plugin" => TaruiCommand.Plugin,
        "help" => TaruiCommand.Help,
        "version" => TaruiCommand.Version,
        _ => throw new CliUsageException($"Unknown command '{value}'. Run 'tarui --help' for usage.")
    };

    private static string ReadOptionValue(
        string name,
        string? inlineValue,
        IReadOnlyList<string> args,
        ref int index)
    {
        if (inlineValue is not null)
        {
            return inlineValue;
        }

        if (index + 1 >= args.Count)
        {
            throw new CliUsageException($"Option '{name}' requires a value.");
        }

        var value = args[index + 1];
        if (value.StartsWith("--", StringComparison.Ordinal))
        {
            throw new CliUsageException($"Option '{name}' requires a value.");
        }

        index++;
        return value;
    }
}