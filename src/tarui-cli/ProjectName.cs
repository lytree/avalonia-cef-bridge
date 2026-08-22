using System.Text;

namespace Tarui.Cli;

/// <summary>
/// Normalizes a user-supplied application name into forms the template and the
/// generated .NET code actually accept.
/// </summary>
internal static class ProjectName
{
    /// <summary>
    /// Produces a C#-identifier-safe name used as the dotnet template's source
    /// name target. Dashes and other invalid characters are stripped and the
    /// result is PascalCased so it is a valid namespace and assembly name.
    /// </summary>
    public static string ToIdentifier(string name, string fallback)
    {
        var words = SplitWords(name);
        if (words.Count == 0)
        {
            words = SplitWords(fallback);
        }

        var builder = new StringBuilder();
        foreach (var word in words)
        {
            if (char.IsDigit(word[0]))
            {
                continue;
            }

            builder.Append(char.ToUpperInvariant(word[0]));
            builder.Append(word.AsSpan(1));
        }

        var result = builder.ToString();
        if (result.Length == 0 || (!char.IsLetter(result[0]) && result[0] != '_'))
        {
            return ToIdentifier(fallback, "App");
        }

        return result;
    }

    /// <summary>
    /// Derives a reverse-DNS style identifier from the app name, e.g. "my-app"
    /// becomes "dev.myapp".
    /// </summary>
    public static string ToIdentifierName(string name)
    {
        var builder = new StringBuilder("dev.");
        foreach (var ch in name)
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
            }
        }

        return builder.ToString();
    }

    private static List<string> SplitWords(string value)
    {
        var words = new List<string>();
        var current = new StringBuilder();
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch))
            {
                current.Append(ch);
                continue;
            }

            if (current.Length > 0)
            {
                words.Add(current.ToString());
                current.Clear();
            }
        }

        if (current.Length > 0)
        {
            words.Add(current.ToString());
        }

        return words;
    }
}