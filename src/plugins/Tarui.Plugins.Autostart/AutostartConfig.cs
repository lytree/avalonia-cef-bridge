using Tarui.Ipc;

namespace Tarui.Plugins.Autostart;

/// <summary>
/// Pure, dependency-free building blocks for the autostart entry. The command line always targets
/// the current application executable -- never an arbitrary path supplied by Web code -- and any
/// pre-configured arguments are individually validated and quoted.
/// </summary>
public static class AutostartConfig
{
    public const int MaxArgs = 16;
    public const int MaxSingleArgLength = 256;
    public const int MaxCommandLineLength = 12_000;

    public static void ValidateArgs(string[]? args)
    {
        if (args is null)
        {
            return;
        }

        if (args.Length > MaxArgs)
        {
            throw new InvalidPayloadException();
        }

        foreach (var arg in args)
        {
            if (arg.Length > MaxSingleArgLength || ContainsControlChar(arg))
            {
                throw new InvalidPayloadException();
            }
        }

        if (args.Sum(static arg => arg.Length + 1L) > MaxCommandLineLength)
        {
            throw new InvalidPayloadException();
        }
    }

    /// <summary>
    /// Builds the quoted command line for a launch entry: the current process path followed by the
    /// (already validated) arguments. Paths and arguments are wrapped in Windows-style quotes only
    /// when required so non-Windows autostart managers see a clean program + args form.
    /// </summary>
    public static string BuildCommandLine(string executablePath, string[]? args)
    {
        var command = Quote(executablePath);
        if (args is not null)
        {
            foreach (var arg in args)
            {
                command += " " + Quote(arg);
            }
        }

        return command;
    }

    private static string Quote(string value)
    {
        if (!value.Contains(' ') && !value.Contains('\t') && !value.Contains('"'))
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    private static bool ContainsControlChar(string value)
    {
        foreach (var ch in value)
        {
            if (char.IsControl(ch) && ch is not '\n' and not '\r')
            {
                return true;
            }
        }

        return false;
    }
}