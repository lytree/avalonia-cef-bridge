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

/// <summary>Writes rich HTML to the clipboard, optionally with a plain-text fallback.</summary>
public sealed record ClipboardWriteHtmlOptions(string Html, string? PlainText = null);

/// <summary>Result of reading the clipboard. <see cref="Available"/> is false when no HTML is present.</summary>
public sealed record ClipboardReadHtmlResult(bool Available, string? Html = null);

/// <summary>Writes a PNG image to the clipboard as a bitmap.</summary>
public sealed record ClipboardWriteImageOptions(byte[] Png);

/// <summary>Result of reading a clipboard image, carried as PNG bytes. <see cref="Available"/> is false
/// when the clipboard holds no image.</summary>
public sealed record ClipboardReadImageResult(bool Available, byte[]? Png = null);

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
