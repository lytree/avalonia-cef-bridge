namespace Tarui.Contracts;

public sealed record PathResolveOptions(string Kind, string? Path = null);

public sealed record PathResolveResult(string Path);

public sealed record OsInfo(
    string Platform,
    string Arch,
    string Version,
    string Family,
    string Locale);

public sealed record ProcessExitOptions(int Code = 0);

public sealed record ShellOpenOptions(string Target);

public sealed record ShellOpenResult(bool Opened, string? Error = null);

public sealed record ClipboardWriteTextOptions(string Text);

public sealed record ClipboardReadTextResult(string Text);

/// <summary>
/// Runtime availability of OS-coupled features on the current platform, surfaced by
/// <c>core:platform|capabilities</c> so the web layer can disable or hide UI instead of hitting an
/// honest runtime degraded no-op. A false support flag carries a machine-readable <c>*Reason</c>.
/// </summary>
public sealed record PlatformCapabilities(
    bool NotificationSupported,
    string? NotificationReason,
    bool GlobalShortcutSupported,
    string? GlobalShortcutReason,
    bool AutostartSupported,
    bool DeepLinkSupported,
    string? DeepLinkReason);

public sealed record SaveDialogOptions(string? DefaultName = null, string[]? Extensions = null);

public sealed record SaveDialogResult(string? Path);
