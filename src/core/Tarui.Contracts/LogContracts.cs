namespace Tarui.Contracts;

/// <summary>
/// A renderer-originated log record forwarded into the host logging pipeline. <see cref="Level"/>
/// is a <c>LogLevel</c> name (<c>Trace</c>..<c>Critical</c>); unknown levels degrade to Information.
/// <see cref="Target"/> selects the logger category (defaults to the caller window).
/// </summary>
public sealed record LogRecordOptions(string Level, string Message, string? Target = null, long? TimestampMs = null);

/// <summary>
/// A log entry streamed to authorized windows on the <c>log://entry</c> event. Native code produces
/// these from <c>Microsoft.Extensions.Logging</c> via the remote log provider, so renderer diagnostics
/// join the desktop logging pipeline and can be surfaced in the UI.
/// </summary>
public sealed record LogEntry(string Level, string Message, string? Target = null, long TimestampMs = 0);