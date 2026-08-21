namespace Tarui.Contracts;

/// <summary>
/// Payload that a second application instance sends to the primary instance before it exits.
/// <see cref="Timestamp"/> is the ISO-8601 second-instance activation time (UTC).
/// </summary>
public sealed record SecondInstanceArgs(
    string[] Arguments,
    string WorkingDirectory,
    string Timestamp);

/// <summary>
/// Targets a window for <c>plugin:window-state|save</c>. Without a label the calling window's
/// state is saved.
/// </summary>
public sealed record WindowStateSaveOptions(string? Label = null);

/// <summary>
/// Targets a window for <c>plugin:window-state|restore</c>. Without a label the calling window's
/// state is restored.
/// </summary>
public sealed record WindowStateRestoreOptions(string? Label = null);

/// <summary>
/// Targets a window for <c>plugin:window-state|clear</c>. Without a label the calling window's
/// persisted state is removed.
/// </summary>
public sealed record WindowStateClearOptions(string? Label = null);

/// <summary>
/// Persisted window geometry and state. Coordinates are in device-independent pixels, the same
/// unit reported by <see cref="LogicalPosition"/>; the restorer validates them against the current
/// monitor set so a window is never restored onto a display that has been disconnected.
/// </summary>
public sealed record WindowStateSnapshot(
    string Label,
    double X,
    double Y,
    double Width,
    double Height,
    bool IsMaximized = false,
    bool IsFullscreen = false);

/// <summary>
/// Result of <c>plugin:window-state|restore</c>. <see cref="Applied"/> is <see langword="false"/>
/// when no saved state was found for the window.
/// </summary>
public sealed record WindowStateRestoreResult(bool Applied);