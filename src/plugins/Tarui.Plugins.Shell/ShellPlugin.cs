using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using Tarui.Contracts;
using Tarui.Ipc;

namespace Tarui.Plugins.Shell;

/// <summary>
/// Authorizes a spawned program against the caller capability's allow/deny <see cref="PathScope"/> lists, where
/// each <see cref="PathScope.Path"/> holds a command/executable glob (<c>git</c>, <c>C:\tools\**</c>, <c>node.*</c>).
/// Deny always wins; in contrast to the file matcher, an empty allow list is a deny-all by default so a bare
/// permission never silently opens arbitrary subprocess execution.
/// </summary>
public static class ShellScopeMatcher
{
    public static bool AllowsProgram(IReadOnlyList<PathScope> allow, IReadOnlyList<PathScope> deny, string program)
    {
        foreach (var scope in deny)
        {
            if (Matches(scope.Path, program))
            {
                return false;
            }
        }

        foreach (var scope in allow)
        {
            if (Matches(scope.Path, program))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Matches(string? pattern, string program)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            return false;
        }

        var ignoreCase = !OperatingSystem.IsWindows();
        var p = ignoreCase ? pattern.ToLowerInvariant() : pattern;
        var v = ignoreCase ? program.ToLowerInvariant() : program;
        return MatchGlob(p, v);
    }

    /// <summary>Matches a program pattern where <c>*</c> spans any characters and <c>?</c> spans exactly one.</summary>
    private static bool MatchGlob(string pattern, string value)
    {
        var p = 0;
        var v = 0;
        var star = -1;
        var match = 0;
        while (v < value.Length)
        {
            if (p < pattern.Length && (pattern[p] == value[v] || pattern[p] == '?'))
            {
                p++;
                v++;
            }
            else if (p < pattern.Length && pattern[p] == '*')
            {
                star = p++;
                match = v;
            }
            else if (star >= 0)
            {
                p = star + 1;
                v = ++match;
            }
            else
            {
                return false;
            }
        }

        while (p < pattern.Length && pattern[p] == '*')
        {
            p++;
        }

        return p == pattern.Length;
    }
}

/// <summary>Authorizes <see cref="ShellSpawnOptions.Program"/> for the spawn command.</summary>
public static class ShellScopeAuthorizer
{
    public static bool AllowsProgram(ShellSpawnOptions options, IReadOnlyList<PathScope> allow, IReadOnlyList<PathScope> deny) =>
        ShellScopeMatcher.AllowsProgram(allow, deny, options.Program);
}

/// <summary>Service that owns spawned child processes, streaming their stdio over channels and recycling them on exit.</summary>
public interface IChildProcessService : IDisposable
{
    /// <summary>
    /// Spawns <see cref="ShellSpawnOptions.Program"/> after authorizing it against <paramref name="allow"/>/<paramref name="deny"/>
    /// (deny-by-default), then starts streaming stdout/stderr to the bound channel. The call resolves with the child handle
    /// once the process is running and its output pump is up, so the caller can start writing to stdin immediately.
    /// </summary>
    ValueTask<ShellSpawnResult> SpawnAsync(
        ShellSpawnOptions options,
        string windowLabel,
        IReadOnlyList<PathScope> allow,
        IReadOnlyList<PathScope> deny,
        CancellationToken cancellationToken);

    /// <summary>Writes raw bytes to a spawned child's stdin. The handle must map to an already-authorized session.</summary>
    ValueTask<Unit> WriteStdinAsync(ShellWriteStdinOptions options, CancellationToken cancellationToken);

    /// <summary>Terminates a spawned child (and its process tree when requested), removing the session.</summary>
    ValueTask<Unit> KillAsync(ShellKillOptions options, CancellationToken cancellationToken);
}

/// <summary>
/// Default child-process service. Each spawn gets a session that redirects the child's stdio and pumps it to the
/// bound channel in frames, then emits a <c>terminated</c> frame with the exit code. Sessions are removed on exit,
/// killed on <see cref="Dispose"/>, so no child can outlive the host.
/// </summary>
public sealed class ChildProcessService : IChildProcessService
{
    private readonly ConcurrentDictionary<string, ChildSession> _sessions = new(StringComparer.Ordinal);
    private int _disposed;

    public ValueTask<ShellSpawnResult> SpawnAsync(
        ShellSpawnOptions options,
        string windowLabel,
        IReadOnlyList<PathScope> allow,
        IReadOnlyList<PathScope> deny,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        if (!ShellScopeMatcher.AllowsProgram(allow, deny, options.Program))
        {
            throw new ScopeDeniedException(ShellPlugin.SpawnCommand);
        }

        var channel = ChannelContext.Bind<ShellStreamEvent>(options.Channel);
        var info = new ProcessStartInfo
        {
            FileName = options.Program,
            WorkingDirectory = options.WorkingDir ?? string.Empty,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = options.CaptureStdout,
            RedirectStandardError = options.CaptureStderr,
            RedirectStandardInput = true,
        };
        foreach (var arg in options.Args ?? [])
        {
            info.ArgumentList.Add(arg);
        }

        Process process;
        try
        {
            process = Process.Start(info) ?? throw new InvalidOperationException(
                $"The program '{options.Program}' could not be started.");
        }
        catch (Win32Exception exception)
        {
            throw new InvalidOperationException(
                $"The program '{options.Program}' could not be started: {exception.Message}", exception);
        }

        var session = new ChildSession(
            NewSessionId(), windowLabel, process, channel, options.CaptureStdout, options.CaptureStderr, _sessions);
        _sessions[session.Id] = session;
        session.StartPump();
        return ValueTask.FromResult(new ShellSpawnResult(session.Id));
    }

    public ValueTask<Unit> WriteStdinAsync(ShellWriteStdinOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        var session = Find(options.Id);
        if (options.Data is { Length: > 0 })
        {
            session.Stdin.Write(options.Data.AsSpan());
            session.Stdin.Flush();
        }

        return ValueTask.FromResult(new Unit());
    }

    public ValueTask<Unit> KillAsync(ShellKillOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        var session = Find(options.Id);
        // The exited handler inside the pump observes this and emits the terminated frame; TryRemove is idempotent.
        session.Kill(options.KillTree);
        _sessions.TryRemove(options.Id, out _);
        return ValueTask.FromResult(new Unit());
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        foreach (var (id, session) in _sessions)
        {
            session.Kill(true);
            try
            {
                session.Stdin.Dispose();
            }
            catch (Exception)
            {
                // 释放尽力而为。
            }

            try
            {
                session.Process.Dispose();
            }
            catch (Exception)
            {
                // 释放尽力而为。
            }
        }
    }

    private ChildSession Find(string id) =>
        _sessions.TryGetValue(id, out var session)
            ? session
            : throw new InvalidOperationException($"No running child process '{id}'.");

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed != 0, this);

    private static string NewSessionId() => "sh-" + Guid.NewGuid().ToString("N");

    /// <summary>
    /// One spawned child. <see cref="StartPump"/> owns a pump task that waits for exit while concurrently draining
    /// the redirected stdout/stderr streams into channel frames, then emits a single <c>terminated</c> frame and
    /// removes the session from its owner's registry.
    /// </summary>
    private sealed class ChildSession(
        string id,
        string windowLabel,
        Process process,
        TaruiChannel<ShellStreamEvent> channel,
        bool captureStdout,
        bool captureStderr,
        ConcurrentDictionary<string, ChildSession> owner)
    {
        public string Id { get; } = id;
        public string WindowLabel { get; } = windowLabel;
        public Process Process { get; } = process;

        /// <summary>The redirect target of the child; stdin writes here reach the child.</summary>
        public Stream Stdin { get; } = process.StandardInput.BaseStream;

        public void StartPump() => Task.Run(PumpAsync);

        public void Kill(bool killTree)
        {
            try
            {
                if (killTree)
                {
                    Process.Kill(entireProcessTree: true);
                }
                else
                {
                    Process.Kill();
                }
            }
            catch (InvalidOperationException)
            {
                // 进程已退出，忽略。
            }
            catch (Win32Exception)
            {
                // 终止失败（权限/句柄），忽略；会话仍由退出观察路径回收。
            }
        }

        private async Task PumpAsync()
        {
            var pumps = new List<Task>(3) { Process.WaitForExitAsync() };
            if (captureStdout)
            {
                pumps.Add(PumpStreamAsync(Process.StandardOutput.BaseStream, "stdout"));
            }

            if (captureStderr)
            {
                pumps.Add(PumpStreamAsync(Process.StandardError.BaseStream, "stderr"));
            }

            int? code = null;
            try
            {
                await Task.WhenAll(pumps);
                code = Process.ExitCode;
            }
            catch (Exception)
            {
                // 退出或流关闭竞态下的异常不应泄漏到宿主；以 terminated 帧收尾。
            }

            try
            {
                await channel.SendAsync(new ShellStreamEvent("terminated", Code: code));
            }
            catch (Exception)
            {
                // 前端已断开：放弃该帧。
            }

            channel.Complete();
            owner.TryRemove(Id, out _);
            try
            {
                Process.Dispose();
            }
            catch (Exception)
            {
                // 释放尽力而为。
            }
        }

        private async Task PumpStreamAsync(Stream stream, string kind)
        {
            var buffer = new byte[81920];
            try
            {
                while (true)
                {
                    var read = await stream.ReadAsync(buffer.AsMemory());
                    if (read == 0)
                    {
                        break;
                    }

                    await channel.SendAsync(new ShellStreamEvent(kind, Data: buffer.AsMemory(0, read).ToArray()));
                }
            }
            catch (Exception)
            {
                // 进程被杀时流关闭会抛异常，按流结束处理。
            }
        }
    }
}

/// <summary>Registers the <c>plugin:shell|spawn|stdin|kill</c> commands behind program-scope authorization.</summary>
public sealed class ShellPlugin(IChildProcessService service) : ITaruiPlugin
{
    public const string SpawnCommand = "plugin:shell|spawn";
    public const string StdinCommand = "plugin:shell|stdin";
    public const string KillCommand = "plugin:shell|kill";

    public void ConfigureCommands(CommandRouterBuilder commands)
    {
        commands.Add(
            SpawnCommand,
            TaruiJsonContext.Default.ShellSpawnOptions,
            TaruiJsonContext.Default.ShellSpawnResult,
            (options, context, ct) =>
            {
                // Shell 默认拒绝：未带程序作用域的裸权限不得静默放开任意子进程执行。
                if (!context.Capabilities.TryGetScope(SpawnCommand, out var scope))
                {
                    throw new ScopeDeniedException(SpawnCommand);
                }

                return service.SpawnAsync(options, context.WindowLabel, scope.Allow, scope.Deny, ct);
            },
            SpawnCommand);

        commands.Add(
            StdinCommand,
            TaruiJsonContext.Default.ShellWriteStdinOptions,
            TaruiJsonContext.Default.Unit,
            (options, context, ct) => service.WriteStdinAsync(options, ct),
            StdinCommand);

        commands.Add(
            KillCommand,
            TaruiJsonContext.Default.ShellKillOptions,
            TaruiJsonContext.Default.Unit,
            (options, context, ct) => service.KillAsync(options, ct),
            KillCommand);
    }
}

public static class ShellPluginServiceCollectionExtensions
{
    public static IServiceCollection AddShellPlugin(this IServiceCollection services) => services
        .AddSingleton<IChildProcessService, ChildProcessService>()
        .AddPlugin<ShellPlugin>();
}