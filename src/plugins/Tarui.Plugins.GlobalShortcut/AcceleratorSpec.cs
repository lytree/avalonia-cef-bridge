using Tarui.Ipc;

namespace Tarui.Plugins.GlobalShortcut;

/// <summary>
/// A normalized, parsed accelerator. Raw Web input like <c>Ctrl+Shift+a</c> or <c>alt-shift-F7</c>
/// is normalized to a canonical order (<c>Alt+Control+Shift+Meta</c> followed by the key). Modifier
/// names are case-insensitive; the key must be a letter, digit or named key and at least one
/// modifier must be present so a plain key can never be registered as a global shortcut.
/// </summary>
public sealed record AcceleratorSpec(
    string Normalized,
    bool Alt,
    bool Control,
    bool Shift,
    bool Meta,
    string Key)
{
    public static AcceleratorSpec Parse(string accelerator)
    {
        if (string.IsNullOrWhiteSpace(accelerator))
        {
            throw new InvalidPayloadException();
        }

        var parts = accelerator.Split(['+', '-'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || parts.Length > 6)
        {
            throw new InvalidPayloadException();
        }

        bool alt = false, control = false, shift = false, meta = false;
        string? key = null;
        foreach (var raw in parts)
        {
            var name = raw.ToLowerInvariant();
            if (name is "alt" or "option" or "ctrl" or "control" or "shift" or
                "super" or "meta" or "win" or "cmd" or "command")
            {
                // A modifier must never follow the bare key: Ctrl+Shift+B+Alt is invalid.
                if (key is not null)
                {
                    throw new InvalidPayloadException();
                }

                if (name is "alt" or "option")
                {
                    alt = true;
                }
                else if (name is "ctrl" or "control")
                {
                    control = true;
                }
                else if (name is "shift")
                {
                    shift = true;
                }
                else
                {
                    meta = true;
                }

                continue;
            }

            if (key is not null)
            {
                throw new InvalidPayloadException();
            }

            key = NormalizeKey(raw);
        }

        if (key is null || !(alt || control || shift || meta))
        {
            throw new InvalidPayloadException();
        }

        var tokens = new List<string>(5);
        if (control)
        {
            tokens.Add("Control");
        }

        if (shift)
        {
            tokens.Add("Shift");
        }

        if (meta)
        {
            tokens.Add("Meta");
        }

        if (alt)
        {
            tokens.Add("Alt");
        }

        tokens.Add(key);
        var normalized = string.Join('+', tokens);

        return new AcceleratorSpec(
            normalized,
            Alt: alt,
            Control: control,
            Shift: shift,
            Meta: meta,
            Key: key);
    }

    /// <summary>Returns <see langword="true"/> when the canonical name matches a glob pattern in the scope list.</summary>
    public bool Matches(IReadOnlyList<Tarui.Contracts.PathScope> scopes) => scopes.Any(scope => Matches(scope));

    public bool Matches(Tarui.Contracts.PathScope scope) =>
        scope.Path is { } pattern && Glob(Normalized, NormalizeScopePattern(pattern));

    /// <summary>
    /// Normalizes a capability scope glob so alias forms like <c>Ctrl</c>, <c>cmd</c> or <c>option</c>
    /// compare equal to the canonical accelerator (for example <c>Control</c>, <c>Meta</c>, <c>Alt</c>).
    /// Wildcard tokens (<c>*</c>, <c>?</c>) are preserved so scope matching behaves like the raw
    /// accelerator matching in <see cref="Parse"/>.
    /// </summary>
    private static string NormalizeScopePattern(string pattern)
    {
        var tokens = pattern.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];
            if (token is "*" or "?")
            {
                continue;
            }

            tokens[i] = token.ToLowerInvariant() switch
            {
                "alt" or "option" => "Alt",
                "ctrl" or "control" => "Control",
                "shift" => "Shift",
                "super" or "meta" or "win" or "cmd" or "command" => "Meta",
                _ when token.Length == 1 && char.IsLetterOrDigit(token[0]) => char.ToUpperInvariant(token[0]).ToString(),
                _ => token,
            };
        }

        return string.Join('+', tokens);
    }

    private static bool Glob(string value, string pattern)
    {
        if (string.Equals(pattern, "*", StringComparison.Ordinal))
        {
            return true;
        }

        return Search(value, pattern, 0, 0);

        static bool Search(string value, string pattern, int vi, int pi)
        {
            while (pi < pattern.Length)
            {
                if (pattern[pi] == '*')
                {
                    var next = pi + 1;
                    if (next == pattern.Length)
                    {
                        return true;
                    }

                    for (var i = vi; i <= value.Length; i++)
                    {
                        if (Search(value, pattern, i, next))
                        {
                            return true;
                        }
                    }

                    return false;
                }

                if (pi >= pattern.Length || vi >= value.Length ||
                    !char.Equals(char.ToUpperInvariant(value[vi]), char.ToUpperInvariant(pattern[pi])))
                {
                    return false;
                }

                vi++;
                pi++;
            }

            return vi == value.Length;
        }
    }

    private static string NormalizeKey(string key)
    {
        if (key.Length == 1 && char.IsLetterOrDigit(key[0]))
        {
            return char.ToUpperInvariant(key[0]).ToString();
        }

        var upper = key.ToUpperInvariant();
        if (upper.Length is >= 2 and <= 3 && upper[0] == 'F' && int.TryParse(upper[1..], out var fn) && fn is >= 1 and <= 24)
        {
            return upper;
        }

        throw new InvalidPayloadException();
    }
}