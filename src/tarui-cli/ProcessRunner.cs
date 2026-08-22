using System.Diagnostics;
using System.Text;

namespace Tarui.Cli;

/// <summary>Result of a run-to-completion child process.</summary>
internal sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

/// <summary>
/// Runs child processes to completion, capturing and optionally forwarding output.
/// Used by <c>tarui build</c> and <c>tarui info</c>.
/// </summary>
internal static class ProcessRunner
{
    public static async Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string>? environment = null,
        TextWriter? output = null,
        TextWriter? error = null,
        CancellationToken cancellationToken = default)
    {
        var startInfo = CreateStartInfo(fileName, arguments, workingDirectory, environment);
        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new CliException($"Failed to start '{fileName}'.");
        }

        var stdoutTask = PumpAsync(process.StandardOutput, output, null, cancellationToken);
        var stderrTask = PumpAsync(process.StandardError, error, null, cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        return new ProcessResult(process.ExitCode, stdout, stderr);
    }

    public static async Task<ProcessResult> RunShellAsync(
        string command,
        string workingDirectory,
        IReadOnlyDictionary<string, string>? environment = null,
        TextWriter? output = null,
        TextWriter? error = null,
        CancellationToken cancellationToken = default)
    {
        var (fileName, arguments) = ShellCommand.For(command);
        return await RunAsync(
            fileName,
            arguments,
            workingDirectory,
            environment,
            output,
            error,
            cancellationToken);
    }

    private static async Task<string> PumpAsync(
        StreamReader reader,
        TextWriter? writer,
        string? prefix,
        CancellationToken cancellationToken)
    {
        var captured = new StringBuilder();
        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                break;
            }

            captured.AppendLine(line);
            if (writer is not null)
            {
                await writer.WriteLineAsync(prefix is null ? line : $"{prefix}{line}").ConfigureAwait(false);
            }
        }

        return captured.ToString();
    }

    internal static ProcessStartInfo CreateStartInfo(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        IReadOnlyDictionary<string, string>? environment)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (environment is not null)
        {
            foreach (var (key, value) in environment)
            {
                startInfo.Environment[key] = value;
            }
        }

        return startInfo;
    }
}

/// <summary>Resolves a manifest command string into a shell invocation (cmd.exe on Windows, /bin/sh elsewhere).</summary>
internal static class ShellCommand
{
    public static (string FileName, IReadOnlyList<string> Arguments) For(string command)
    {
        if (OperatingSystem.IsWindows())
        {
            return ("cmd.exe", ["/d", "/s", "/c", command]);
        }

        return ("/bin/sh", ["-c", command]);
    }
}
