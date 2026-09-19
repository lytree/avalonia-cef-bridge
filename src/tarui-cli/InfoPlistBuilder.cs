using System.Globalization;
using System.Text;

namespace Tarui.Cli;

/// <summary>
/// Renders the <c>Info.plist</c> for a macOS application bundle. The output conforms to
/// <c>com.apple.application</c> + <c>CFBundleURLTypes</c> grammar; <c>plutil -lint</c> on macOS
/// accepts the produced document and the runtime <c>MacDeepLinkBridge</c> handler picks up
/// <c>kAEGetURL</c> activations for every <see cref="AppManifestMacOs.Schemes"/> entry.
/// </summary>
internal static class InfoPlistBuilder
{
    /// <summary>Builds the <c>Info.plist</c> body for the bundle root.</summary>
    /// <param name="manifest">Resolved application manifest; supplies product identity and macOS overrides.</param>
    /// <param name="rid">Target RID; used to default <c>LSMinimumSystemVersion</c> when the manifest does not set one.</param>
    public static string Build(AppManifest manifest, string rid)
    {
        var product = manifest.Product;
        var macOs = manifest.Bundle.MacOs ?? new AppManifestMacOs(null, null, null, []);

        var bundleId = macOs.BundleId ?? product.Identifier;
        var displayName = product.Name;
        var executable = macOs.ExecutableName ?? product.Name;
        var minimumVersion = macOs.MinimumSystemVersion ?? DefaultMinimumSystemVersion(rid);

        ValidateBundleId(bundleId);
        ValidateExecutableName(executable);
        ValidateSchemes(macOs.Schemes);

        var version = ToFourPartVersion(product.Version);
        var shortVersion = ToShortVersion(product.Version);

        var urlTypes = BuildUrlTypes(macOs.Schemes);

        var builder = new StringBuilder(1024);
        builder.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        builder.AppendLine();
        builder.AppendLine("<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">");
        builder.AppendLine("<plist version=\"1.0\">");
        builder.AppendLine("<dict>");
        AppendKeyValue(builder, "CFBundleIdentifier", bundleId, indent: 1);
        AppendKeyValue(builder, "CFBundleName", displayName, indent: 1);
        AppendKeyValue(builder, "CFBundleDisplayName", displayName, indent: 1);
        AppendKeyValue(builder, "CFBundleExecutable", executable, indent: 1);
        AppendKeyValue(builder, "CFBundleVersion", version, indent: 1);
        AppendKeyValue(builder, "CFBundleShortVersionString", shortVersion, indent: 1);
        AppendKeyValue(builder, "CFBundlePackageType", "APPL", indent: 1);
        AppendKeyValue(builder, "CFBundleSignature", "????", indent: 1);
        AppendKeyValue(builder, "LSMinimumSystemVersion", minimumVersion, indent: 1);
        AppendKeyValue(builder, "NSHighResolutionCapable", "true", indent: 1);
        AppendKeyValue(builder, "NSPrincipalClass", "NSApplication", indent: 1);
        AppendRaw(builder, urlTypes, indent: 1);
        builder.AppendLine("</dict>");
        builder.AppendLine("</plist>");
        return builder.ToString();
    }

    /// <summary>Renders the <c>CFBundleURLTypes</c> array; empty when no schemes are configured.</summary>
    internal static string BuildUrlTypes(IReadOnlyList<string> schemes)
    {
        if (schemes.Count == 0)
        {
            return string.Empty;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var builder = new StringBuilder();
        builder.AppendLine();
        builder.AppendLine("    <key>CFBundleURLTypes</key>");
        builder.AppendLine("    <array>");
        foreach (var scheme in schemes)
        {
            if (!seen.Add(scheme))
            {
                // Validator already rejects duplicates, but the builder is defensive: never emit
                // two URL-type entries for the same scheme because that races the OS resolver.
                continue;
            }

            builder.AppendLine("      <dict>");
            builder.AppendLine("        <key>CFBundleURLName</key>");
            builder.Append(CultureInfo.InvariantCulture, $"        <string>net.tarui.{XmlEscape(scheme)}</string>");
            builder.AppendLine();
            builder.AppendLine("        <key>CFBundleTypeRole</key>");
            builder.AppendLine("        <string>Viewer</string>");
            builder.AppendLine("        <key>CFBundleURLSchemes</key>");
            builder.AppendLine("        <array>");
            builder.Append(CultureInfo.InvariantCulture, $"          <string>{XmlEscape(scheme)}</string>");
            builder.AppendLine();
            builder.AppendLine("        </array>");
            builder.AppendLine("      </dict>");
        }

        builder.Append("    </array>");
        return builder.ToString();
    }

    private static void AppendKeyValue(StringBuilder builder, string key, string value, int indent)
    {
        var pad = new string(' ', indent * 2);
        builder.AppendLine(CultureInfo.InvariantCulture, $"{pad}<key>{key}</key>");
        builder.AppendLine(CultureInfo.InvariantCulture, $"{pad}<string>{XmlEscape(value)}</string>");
    }

    private static void AppendRaw(StringBuilder builder, string raw, int indent)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return;
        }

        var pad = new string(' ', indent * 2);
        // Normalize the first line to match indent; remaining lines keep their existing padding.
        var lines = raw.TrimStart('\r', '\n').Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (i == 0 && !line.StartsWith(pad, StringComparison.Ordinal))
            {
                builder.Append(pad);
            }

            builder.AppendLine(line);
        }
    }

    private static string DefaultMinimumSystemVersion(string rid) =>
        rid.Contains("arm64", StringComparison.OrdinalIgnoreCase) ? "11.0" : "10.15";

    private static string ToFourPartVersion(string version)
    {
        var parts = version.Split('.');
        return parts.Length switch
        {
            >= 4 => $"{parts[0]}.{parts[1]}.{parts[2]}.{parts[3]}",
            3 => $"{parts[0]}.{parts[1]}.{parts[2]}.0",
            2 => $"{parts[0]}.{parts[1]}.0.0",
            1 => $"{parts[0]}.0.0.0",
            _ => "0.0.0.0",
        };
    }

    private static string ToShortVersion(string version)
    {
        var parts = version.Split('.');
        return parts.Length >= 2 ? $"{parts[0]}.{parts[1]}" : parts[0];
    }

    private static void ValidateBundleId(string bundleId)
    {
        if (string.IsNullOrWhiteSpace(bundleId))
        {
            throw new CliException("CFBundleIdentifier requires a non-empty value.");
        }

        if (!System.Text.RegularExpressions.Regex.IsMatch(bundleId, "^[A-Za-z0-9\\-]+(\\.[A-Za-z0-9\\-]+)+$"))
        {
            throw new CliException($"CFBundleIdentifier must be a reverse-DNS identifier, got '{bundleId}'.");
        }
    }

    private static void ValidateExecutableName(string executable)
    {
        if (string.IsNullOrWhiteSpace(executable))
        {
            throw new CliException("CFBundleExecutable requires a non-empty value.");
        }

        if (executable.Contains(' '))
        {
            throw new CliException($"CFBundleExecutable must not contain spaces, got '{executable}'.");
        }

        if (executable.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
        {
            throw new CliException($"CFBundleExecutable must not end with .app, got '{executable}'.");
        }
    }

    private static void ValidateSchemes(IReadOnlyList<string> schemes)
    {
        foreach (var scheme in schemes)
        {
            if (string.IsNullOrEmpty(scheme) || !char.IsAsciiLetter(scheme[0]))
            {
                throw new CliException($"CFBundleURLSchemes entries must start with an ASCII letter, got '{scheme}'.");
            }

            foreach (var c in scheme)
            {
                if (!(char.IsAsciiLetterOrDigit(c) || c is '+' or '-' or '.'))
                {
                    throw new CliException($"CFBundleURLSchemes entries must follow RFC 3986 grammar, got '{scheme}'.");
                }
            }
        }
    }

    private static string XmlEscape(string value) =>
        value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);
}