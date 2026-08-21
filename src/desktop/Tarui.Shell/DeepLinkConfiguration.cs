using Microsoft.Extensions.Configuration;

namespace Tarui.Shell;

/// <summary>
/// Reads and validates the configured deep-link scheme set from
/// <c>Tarui:Application:DeepLinkSchemes</c> (a JSON string array). Schemes are validated so an
/// invalid token never reaches the URL parser, the protocol registrar, or capability events.
/// </summary>
internal static class DeepLinkConfiguration
{
    public static IReadOnlyCollection<string> ReadSchemes(IConfiguration? configuration)
    {
        if (configuration is null)
        {
            return [];
        }

        var section = configuration.GetSection("Tarui:Application:DeepLinkSchemes");
        var schemes = new List<string>();
        foreach (var child in section.GetChildren())
        {
            var scheme = child.Value;
            if (string.IsNullOrWhiteSpace(scheme))
            {
                continue;
            }

            if (!DeepLinkUri.IsValidScheme(scheme))
            {
                continue;
            }

            if (!schemes.Contains(scheme, StringComparer.OrdinalIgnoreCase))
            {
                schemes.Add(scheme);
            }
        }

        return schemes;
    }
}