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

/// <summary>The kinds a structured CLI option may hold.</summary>
public enum CliArgKind
{
    /// <summary>A boolean switch that takes no value.</summary>
    Flag,
    /// <summary>A single textual value (repeats are last-wins).</summary>
    Text,
    /// <summary>Zero or more textual values collected across repeats.</summary>
    TextList,
    /// <summary>A single 64-bit number value.</summary>
    Number,
    /// <summary>Zero or more number values collected across repeats.</summary>
    NumberList,
}

/// <summary>A declaration for one long (<c>--name</c>) and/or short (<c>-x</c>) CLI option that the parser
/// recognizes. Required options that are absent cause the whole parse to fail with a descriptive error.</summary>
public sealed record CliArgSpec(
    string Name,
    string? ShortName = null,
    CliArgKind Kind = CliArgKind.Flag,
    bool Multiple = false,
    bool Required = false,
    string? Description = null);

/// <summary>Request for <c>core:cli|parse</c>. <see cref="Args"/> defaults to the current process arguments
/// (the executable name is already stripped) when omitted.</summary>
public sealed record CliParseOptions(
    CliArgSpec[] Options,
    string? PositionalName = null,
    bool PositionalRequired = false,
    string[]? Args = null);

/// <summary>One parsed option value. Only the fields matching <see cref="Kind"/> are populated.</summary>
public sealed record CliArgValue(
    string Name,
    CliArgKind Kind,
    bool Present = false,
    string? Value = null,
    long? Number = null,
    string[]? Values = null,
    long[]? Numbers = null);

/// <summary>Result of parsing CLI arguments against a declared schema. <see cref="Success"/> is false and
/// <see cref="Error"/> set when a required option is missing, a value is malformed, or an unknown option appears.</summary>
public sealed record CliParseResult(
    bool Success,
    string? Error,
    CliArgValue[] Values,
    string? PositionalName,
    string[] Positionals);

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
