using Microsoft.Extensions.Configuration;
using Tarui.Plugins.Notification;

namespace Tarui.Shell.Toast;

/// <summary>
/// Reads <see cref="ToastConfiguration"/> from <c>Tarui:Notification:AumId / DisplayName / IconUri</c>
/// (mirroring the tolerant <c>Tarui:*</c> reading style used across the shell) and falls back to a
/// stable default identity when the host does not configure one.
/// </summary>
public static class ToastConfigurationReader
{
    /// <summary>Default AppUserModelId used when the host does not configure one.</summary>
    public const string DefaultAumId = "net.tarui.app";

    /// <summary>Default Action Center display name used when the host does not configure one.</summary>
    public const string DefaultDisplayName = "Tarui Application";

    public static ToastConfiguration Read(IConfiguration? configuration)
    {
        var section = configuration?.GetSection("Tarui:Notification");
        var aumId = section?["AumId"];
        var displayName = section?["DisplayName"];
        var iconUri = section?["IconUri"];

        return new ToastConfiguration(
            string.IsNullOrWhiteSpace(aumId) ? DefaultAumId : aumId.Trim(),
            string.IsNullOrWhiteSpace(displayName) ? DefaultDisplayName : displayName.Trim(),
            string.IsNullOrWhiteSpace(iconUri) ? null : iconUri.Trim());
    }
}
