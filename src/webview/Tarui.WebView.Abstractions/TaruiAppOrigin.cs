namespace Tarui.WebView.Abstractions;

/// <summary>
/// The application origin. <see cref="StartUri"/> is where the main window first navigates;
/// <see cref="AllowedSchemes"/> lists every scheme accepted when a window is created or a web view
/// navigates, so an HTTP(S) origin and a custom, portless app scheme (for example
/// <c>tarui://localhost</c>) can coexist in one application. <see cref="SchemeOrigin"/> is the
/// custom-scheme origin served by the local asset resolver, when one is configured.
/// </summary>
public sealed record TaruiAppOrigin(
    Uri StartUri,
    IReadOnlyList<string>? AllowedSchemes = null,
    Uri? SchemeOrigin = null)
{
    /// <summary>
    /// The schemes the application accepts, falling back to the start URI's scheme when no explicit
    /// list is configured.
    /// </summary>
    public IReadOnlyList<string> Schemes => AllowedSchemes is { Count: > 0 } schemes
        ? schemes
        : [StartUri.Scheme];

    /// <summary>Whether <paramref name="scheme"/> may be used for window or web view navigation.</summary>
    public bool AllowsScheme(string scheme) =>
        Schemes.Contains(scheme, StringComparer.OrdinalIgnoreCase);
}
