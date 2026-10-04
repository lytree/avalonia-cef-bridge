namespace Tarui.Plugins.Notification;

/// <summary>
/// Identity of the toast sender on Windows: the AppUserModelId under which toasts are shown
/// and registered in <c>HKCU\Software\Classes\AppUserModelId</c> so they appear in the
/// Action Center with the app's display name and icon.
/// </summary>
public sealed record ToastConfiguration(
    string AumId,
    string DisplayName,
    string? IconUri = null);
