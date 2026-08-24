using System.Globalization;
using System.Text;

namespace Tarui.Hosting;

/// <summary>
/// The single, security-authoritative identity for a hosted Tarui application. Every OS-scoped
/// resource (single-instance endpoint, store directory, window-state file, updater staging root,
/// deep-link registrar) MUST be derived from this identity, never from a bare product name, so two
/// apps built from the same source tree never share storage on the same machine. The
/// <see cref="SanitizedIdentifier"/> property is the only stable string the OS layer may use for
/// filesystem and IPC endpoint naming.
/// </summary>
public sealed record TaruiApplicationIdentity(
    string ProductName,
    string Identifier,
    string Version)
{
    /// <summary>
    /// Default identity used when an app does not supply explicit values during bootstrap. The
    /// values are deliberately generic so they are obviously placeholders; a release build should
    /// override them via <see cref="TaruiApplicationBuilder"/> or HostingOptions.
    /// </summary>
    public static TaruiApplicationIdentity Default { get; } = new(
        ProductName: "Tarui App",
        Identifier: "tarui-app",
        Version: "0.0.0");

    /// <summary>
    /// Lowercase, punctuation-stripped, length-bounded identifier suitable for OS endpoint naming.
    /// The OS layer (single-instance, file paths, IPC) consumes this value directly; never the raw
    /// <see cref="Identifier"/>.
    /// </summary>
    public string SanitizedIdentifier => SanitizeIdentifier(Identifier);

    /// <summary>
    /// Produces an identity from an <see cref="AppManifest"/>-like trio. The
    /// <c>identifier</c> argument is required; the product name and version are accepted as null
    /// and fall back to safe defaults so a partial manifest still produces a usable identity.
    /// </summary>
    public static TaruiApplicationIdentity FromManifest(string identifier, string? productName, string? version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        return new TaruiApplicationIdentity(
            ProductName: string.IsNullOrWhiteSpace(productName) ? "Tarui App" : productName!,
            Identifier: identifier,
            Version: string.IsNullOrWhiteSpace(version) ? "0.0.0" : version!);
    }

    /// <summary>
    /// Normalizes a user-supplied identifier to lowercase ASCII letters, digits, dots, dashes and
    /// underscores, truncating to 64 characters. Mirrors the sanitization in
    /// <c>SingleInstanceIdentity</c>; if the policies diverge, OS endpoint naming will silently
    /// disagree between modules, which is exactly the kind of bug this record exists to prevent.
    /// </summary>
    internal static string SanitizeIdentifier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Default.SanitizedIdentifier;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Trim())
        {
            builder.Append(char.IsLetterOrDigit(character) || character is '.' or '-' or '_'
                ? char.ToLowerInvariant(character)
                : '-');
        }

        var sanitized = builder.ToString().Trim('-');
        if (sanitized.Length == 0)
        {
            return Default.SanitizedIdentifier;
        }

        if (sanitized.Length > MaxIdentifierLength)
        {
            sanitized = sanitized[..MaxIdentifierLength].TrimEnd('-');
            if (sanitized.Length == 0)
            {
                return Default.SanitizedIdentifier;
            }
        }

        return sanitized;
    }

    internal const int MaxIdentifierLength = 64;

    /// <summary>Stable cross-platform identifier, used for diagnostics only.</summary>
    public string CultureInvariantTag =>
        string.Create(CultureInfo.InvariantCulture, $"{SanitizedIdentifier}-{Version}");
}
