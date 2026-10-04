using System.Text;
using Tarui.Contracts;

namespace Tarui.Plugins.Notification;

/// <summary>
/// Pure builder for Windows Toast XML payloads (template <c>ToastGeneric</c>). Kept in the
/// cross-platform plugin so the produced XML is unit-testable without the WinRT projections:
/// it escapes text, maps the optional icon to an <c>appLogoOverride</c> image, maps
/// <see cref="NotificationOptions.Sound"/> false to a silent <c>audio</c> element, and renders
/// <see cref="NotificationOptions.Actions"/> as foreground <c>action</c> buttons whose
/// <c>arguments</c> carry the app-defined action id.
/// </summary>
public static class ToastContentBuilder
{
    /// <summary>Builds the full toast XML document for <paramref name="options"/>.</summary>
    public static string Build(NotificationOptions options)
    {
        var xml = new StringBuilder(512);
        xml.Append("<toast><visual><binding template=\"ToastGeneric\">");
        xml.Append("<text>").Append(Escape(options.Title)).Append("</text>");
        xml.Append("<text>").Append(Escape(options.Body)).Append("</text>");

        if (!string.IsNullOrWhiteSpace(options.Icon))
        {
            xml.Append("<image placement=\"appLogoOverride\" src=\"")
                .Append(Escape(options.Icon))
                .Append("\"/>");
        }

        xml.Append("</binding></visual>");

        if (!options.Sound)
        {
            xml.Append("<audio silent=\"true\"/>");
        }

        if (options.Actions is { Count: > 0 })
        {
            xml.Append("<actions>");
            foreach (var action in options.Actions)
            {
                xml.Append("<action content=\"").Append(Escape(action.Label))
                    .Append("\" arguments=\"").Append(Escape(action.Id))
                    .Append("\" activationType=\"foreground\"/>");
            }

            xml.Append("</actions>");
        }

        xml.Append("</toast>");
        return xml.ToString();
    }

    private static string Escape(string value) => value
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("\"", "&quot;", StringComparison.Ordinal)
        .Replace("'", "&apos;", StringComparison.Ordinal);
}
