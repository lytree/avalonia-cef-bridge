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

public sealed record SaveDialogOptions(string? DefaultName = null, string[]? Extensions = null);

public sealed record SaveDialogResult(string? Path);
