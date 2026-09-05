using System.Globalization;
using Tarui.Contracts;

namespace Tarui.Plugins.System;

/// <summary>
/// Declarative command-line parser. Given a set of declared options (<c>--long</c> / <c>-x</c>), positional
/// collection and the raw argument list, it produces a structured <see cref="CliParseResult"/>. Parsing is strict:
/// an unknown option, a missing required option, or a malformed typed value fails the whole parse with a
/// descriptive error. Long options accept both <c>--name value</c> and <c>--name=value</c>; <c>--</c> ends option
/// parsing so the remainder is treated as positional input.
/// </summary>
public static class CliParser
{
    public static CliParseResult Parse(CliParseOptions options)
    {
        var specsByName = new Dictionary<string, CliArgSpec>(StringComparer.Ordinal);
        var specsByShort = new Dictionary<char, CliArgSpec>();
        foreach (var spec in options.Options)
        {
            specsByName[spec.Name] = spec;
            if (!string.IsNullOrEmpty(spec.ShortName) && spec.ShortName!.Length == 1)
            {
                specsByShort[spec.ShortName[0]] = spec;
            }
        }

        var args = options.Args ?? Environment.GetCommandLineArgs().Skip(1).ToArray();
        var positionals = new List<string>();
        var collector = new Collector(specsByName);

        var afterDoubleDash = false;
        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (afterDoubleDash)
            {
                positionals.Add(arg);
                continue;
            }

            if (arg == "--")
            {
                afterDoubleDash = true;
                continue;
            }

            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                if (!TrySplitLong(arg, out var name, out var inline))
                {
                    return Fail($"Unknown option '{arg}'.");
                }

                if (!specsByName.TryGetValue(name, out var spec))
                {
                    return Fail($"Unknown option '--{name}'.");
                }

                var result = ReadOption(spec, inline, args, ref index);
                if (!result.Success)
                {
                    return Fail(result.Error!);
                }

                collector.Add(spec, result.Value!);
                continue;
            }

            if (arg.Length > 1 && arg[0] == '-')
            {
                var shortName = arg[1];
                if (!specsByShort.TryGetValue(shortName, out var spec))
                {
                    return Fail($"Unknown option '-{shortName}'.");
                }

                // Support the -xvalue compact form for value-taking options.
                var inlineRest = arg.Length > 2 ? arg[2..] : null;
                var result = ReadOption(spec, inlineRest, args, ref index);
                if (!result.Success)
                {
                    return Fail(result.Error!);
                }

                collector.Add(spec, result.Value!);
                continue;
            }

            positionals.Add(arg);
        }

        var valuesResult = collector.Build();
        if (valuesResult.Error is not null)
        {
            return Fail(valuesResult.Error!);
        }

        if (options.PositionalRequired && positionals.Count == 0)
        {
            return Fail($"The positional argument '{(options.PositionalName ?? "args")}' is required.");
        }

        return new CliParseResult(
            Success: true,
            Error: null,
            Values: valuesResult.Values,
            PositionalName: options.PositionalName,
            Positionals: [.. positionals]);
    }

    private static bool TrySplitLong(string arg, out string name, out string? inline)
    {
        var body = arg[2..];
        var equals = body.IndexOf('=');
        if (equals < 0)
        {
            name = body;
            inline = null;
            return true;
        }

        name = body[..equals];
        inline = body[(equals + 1)..];
        return true;
    }

    private static OptionResult ReadOption(
        CliArgSpec spec,
        string? inline,
        string[] args,
        ref int index)
    {
        if (spec.Kind == CliArgKind.Flag)
        {
            if (inline is not null)
            {
                return new OptionResult(false, Error: $"The flag '--{spec.Name}' does not take a value.");
            }

            return new OptionResult(true, Value: new CliArgValue(spec.Name, CliArgKind.Flag, Present: true));
        }

        string? raw;
        if (inline is not null)
        {
            raw = inline;
        }
        else if (index + 1 < args.Length && !LooksLikeOption(args[index + 1]))
        {
            raw = args[++index];
        }
        else
        {
            return new OptionResult(false, Error: $"The option '--{spec.Name}' requires a value.");
        }

        var (text, error) = NormalizeValue(spec, raw);
        if (error is not null)
        {
            return new OptionResult(false, Error: error);
        }

        // Single-valued kinds resolve immediately; multi-valued kinds accumulate.
        if (!spec.Multiple)
        {
            return new OptionResult(true, Value: ToSingleValue(spec, text, raw));
        }

        return new OptionResult(true, Value: new CliArgValue(
            spec.Name,
            spec.Kind,
            Present: true,
            Values: spec.Kind is CliArgKind.TextList ? [text] : null,
            Numbers: spec.Kind is CliArgKind.NumberList ? new long[] { ParseLong(raw)!.Value } : null));
    }

    private static (string Text, string? Error) NormalizeValue(CliArgSpec spec, string raw)
    {
        if (spec.Kind is CliArgKind.Text or CliArgKind.TextList)
        {
            return (raw, null);
        }

        return ParseLong(raw) is { } parsed
            ? (parsed.ToString(CultureInfo.InvariantCulture), null)
            : (raw, $"The option '--{spec.Name}' expects an integer, got '{raw}'.");
    }

    private static CliArgValue ToSingleValue(CliArgSpec spec, string text, string raw) => spec.Kind switch
    {
        CliArgKind.Text => new CliArgValue(spec.Name, CliArgKind.Text, Present: true, Value: text),
        CliArgKind.Number => new CliArgValue(spec.Name, CliArgKind.Number, Present: true, Number: ParseLong(raw)),
        _ => new CliArgValue(spec.Name, CliArgKind.Flag, Present: true),
    };

    private static long? ParseLong(string raw) =>
        long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static bool LooksLikeOption(string arg) =>
        arg.Length > 1 && arg[0] == '-';

    private static CliParseResult Fail(string error) =>
        new CliParseResult(Success: false, Error: error, Values: [], PositionalName: null, Positionals: []);

    private readonly record struct OptionResult(bool Success, CliArgValue? Value = null, string? Error = null);

    private sealed class Collector(Dictionary<string, CliArgSpec> specsByName)
    {
        private readonly Dictionary<string, CliArgValue> _seen = new(StringComparer.Ordinal);
        private readonly Dictionary<string, (List<string> Texts, List<long> Numbers)> _multi = new(StringComparer.Ordinal);
        private readonly HashSet<string> _requiredDeclared = specsByName.Values
            .Where(static spec => spec.Required)
            .Select(static spec => spec.Name)
            .ToHashSet(StringComparer.Ordinal);

        public void Add(CliArgSpec spec, CliArgValue value)
        {
            if (!spec.Multiple)
            {
                _seen[spec.Name] = value;
                if (_requiredDeclared.Contains(spec.Name))
                {
                    _requiredDeclared.Remove(spec.Name);
                }

                return;
            }

            if (!_multi.TryGetValue(spec.Name, out var bucket))
            {
                bucket = ([], []);
                _multi[spec.Name] = bucket;
            }

            if (value.Values is not null)
            {
                bucket.Texts.AddRange(value.Values);
            }

            if (value.Numbers is not null)
            {
                bucket.Numbers.AddRange(value.Numbers);
            }

            if (_requiredDeclared.Contains(spec.Name))
            {
                _requiredDeclared.Remove(spec.Name);
            }
        }

        public (CliArgValue[] Values, string? Error) Build()
        {
            if (_requiredDeclared.Count > 0)
            {
                return ([], $"The required option '--{_requiredDeclared.First()}' is missing.");
            }

            var result = new List<CliArgValue>(_seen.Values);
            foreach (var (name, (texts, numbers)) in _multi)
            {
                var spec = specsByName[name];
                result.Add(spec.Kind == CliArgKind.NumberList
                    ? new CliArgValue(name, CliArgKind.NumberList, Present: true, Numbers: [.. numbers])
                    : new CliArgValue(name, CliArgKind.TextList, Present: true, Values: [.. texts]));
            }

            return ([.. result], null);
        }
    }
}