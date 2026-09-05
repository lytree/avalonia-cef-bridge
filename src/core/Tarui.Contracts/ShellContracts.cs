namespace Tarui.Contracts;

/// <summary>
/// Request for the <c>plugin:shell|spawn</c> command. <see cref="Program"/> is validated against the caller
/// capability's allow/deny program scopes (deny-by-default) before any process is started. When
/// <see cref="Channel"/> is set, stdout/stderr are streamed to it node by node and a termination frame closes
/// it; the call resolves once the process is spawned and its output pump is running.
/// </summary>
public sealed record ShellSpawnOptions(
    string Program,
    string[]? Args = null,
    string? Channel = null,
    string? WorkingDir = null,
    bool CaptureStdout = true,
    bool CaptureStderr = true);

/// <summary>Handle for a spawned child process; used by <c>plugin:shell|stdin</c> and <c>plugin:shell|kill</c>.</summary>
public sealed record ShellSpawnResult(string Id);

/// <summary>Writes raw bytes to a child process's redirected stdin. Operates on an already-authorized handle.</summary>
public sealed record ShellWriteStdinOptions(string Id, byte[] Data);

/// <summary>Stops a spawned child process, killing its process tree when <see cref="KillTree"/> is set.</summary>
public sealed record ShellKillOptions(string Id, bool KillTree = true);

/// <summary>
/// A single frame streamed over the child's <c>TaruiChannel</c>. <c>Kind</c> is <c>"stdout"</c>/<c>"stderr"</c>
/// carrying <see cref="Data"/>, or <c>"terminated"</c> carrying the process exit <see cref="Code"/> (null when it
/// was killed by a signal/force). A terminated frame signals the end of the stream.
/// </summary>
public sealed record ShellStreamEvent(string Kind, byte[]? Data = null, int? Code = null);