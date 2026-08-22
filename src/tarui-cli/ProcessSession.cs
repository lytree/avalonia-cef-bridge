using System.Diagnostics;

namespace Tarui.Cli;

/// <summary>
/// A long-running child process with line-prefixed output forwarding and
/// process-tree termination. Used by <c>tarui dev</c> for the dev server and
/// the <c>dotnet watch</c> host.
/// </summary>
internal sealed class ProcessSession : IAsyncDisposable
{
    private readonly Process _process;
    private readonly Task _exitTask;
    private readonly object _gate = new();
    private bool _stopping;

    private ProcessSession(Process process)
    {
        _process = process;
        _exitTask = process.WaitForExitAsync();
    }

    public int Id => _process.Id;

    public bool HasExited => _process.HasExited;

    public static ProcessSession Start(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string>? environment = null,
        TextWriter? output = null,
        string? linePrefix = null,
        CancellationToken cancellationToken = default)
    {
        var startInfo = ProcessRunner.CreateStartInfo(fileName, arguments, workingDirectory, environment);
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;

        var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new CliException($"Failed to start '{fileName}'.");
        }

        _ = PumpAsync(process.StandardOutput, output, linePrefix, cancellationToken);
        _ = PumpAsync(process.StandardError, output, linePrefix, cancellationToken);

        return new ProcessSession(process);
    }

    public Task<int> WaitForExitAsync() => _exitTask.ContinueWith(
        static (_, state) => ((Process)state!).ExitCode,
        _process,
        CancellationToken.None,
        TaskContinuationOptions.ExecuteSynchronously,
        TaskScheduler.Default);

    /// <summary>Terminates the process and its descendants (CEF subprocesses included).</summary>
    public async Task StopAsync()
    {
        lock (_gate)
        {
            if (_stopping)
            {
                return;
            }

            _stopping = true;
        }

        if (!_process.HasExited)
        {
            try
            {
                _process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Process already exited concurrently.
            }
        }

        try
        {
            await _exitTask.ConfigureAwait(false);
        }
        catch
        {
            // Exit code is not meaningful after a forced kill.
        }
    }

    public ValueTask DisposeAsync()
    {
        _process.Dispose();
        return ValueTask.CompletedTask;
    }

    private static async Task PumpAsync(
        StreamReader reader,
        TextWriter? writer,
        string? prefix,
        CancellationToken cancellationToken)
    {
        if (writer is null)
        {
            try
            {
                await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (IOException)
            {
                // Stream closed when the process tree was killed.
            }

            return;
        }

        try
        {
            while (true)
            {
                var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                await writer.WriteLineAsync(prefix is null ? line : $"{prefix}{line}").ConfigureAwait(false);
            }
        }
        catch (IOException)
        {
            // Stream closed when the process tree was killed.
        }
        catch (OperationCanceledException)
        {
            // Session cancelled.
        }
    }
}
