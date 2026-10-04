using System.Runtime.Versioning;
using Microsoft.Win32;
using Tarui.Plugins.Notification;

namespace Tarui.Shell.Toast.Windows;

/// <summary>
/// Publishes the AppUserModelId under <c>HKCU\Software\Classes\AppUserModelId</c> so Windows can
/// attribute toasts of an unpackaged (portable) app to a named, iconized entry in the Action
/// Center. Registration is per-user and idempotent; a failure propagates so the owning service
/// degrades that show to its balloon fallback instead of shipping an unattributed toast.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class ToastAumIdRegistrar
{
    public static void EnsureRegistered(ToastConfiguration configuration)
    {
        using var key = Registry.CurrentUser.CreateSubKey(
            $@"Software\Classes\AppUserModelId\{configuration.AumId}")
            ?? throw new InvalidOperationException(
                $"Could not create the AppUserModelId registry key for '{configuration.AumId}'.");

        key.SetValue("DisplayName", configuration.DisplayName, RegistryValueKind.String);
        if (configuration.IconUri is not null)
        {
            key.SetValue("IconUri", configuration.IconUri, RegistryValueKind.String);
        }
    }
}
