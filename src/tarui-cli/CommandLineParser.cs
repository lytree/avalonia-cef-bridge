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
