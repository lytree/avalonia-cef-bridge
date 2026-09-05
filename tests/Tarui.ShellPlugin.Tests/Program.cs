using System.Text;
using System.Text.Json;
using Tarui.Contracts;
using Tarui.Ipc;
using Tarui.Plugins.Shell;

namespace Tarui.Plugins.Shell.Tests;

internal static class Program
{
    public static async Task<int> Main()
    {
        DeniesSpawnWithoutAnyScopeAsync();
        await DeniesSpawnOfNotAllowedProgramAsync();
        await SpawnsAndStreamsStdoutAsync();
        await StreamsStderrSeparatelyAsync();
        await ReportsExitCodeAsync();
        await WritesToChildStdinAsync();
        await KillsLongRunningChildAsync();
        await StdinCommandWithoutSessionFailsAsync();
        Console.WriteLine("Tarui.ShellPlugin self-tests passed.");
        return 0;
    }

    private static void DeniesSpawnWithoutAnyScopeAsync()
    {
        using var service = new ChildProcessService();
        // 裸权限（无程序作用域）必须默认拒绝，绝不能静默放开任意子进程执行。
        var caps = new CapabilitySet([ShellPlugin.SpawnCommand]);
        var denied = DispatchSpawn(NewDispatcher(service), caps,
            new ShellSpawnOptions(EchoProgram().Program, EchoPrintArgs())).GetAwaiter().GetResult();
        Assert(!denied!.Success, "A bare spawn permission with no program scope must be denied.");
        Assert(denied.Error?.Code == "SCOPE_DENIED", "A scopeless spawn must surface as SCOPE_DENIED.");
    }

    private static async Task DeniesSpawnOfNotAllowedProgramAsync()
    {
        using var service = new ChildProcessService();
        // 允许清单只允许别的程序，正在请求的程序必须被拒。
        var caps = ShellCapability(["not-the-requested-program.exe"]);
        var denied = await DispatchSpawn(NewDispatcher(service), caps,
            new ShellSpawnOptions(EchoProgram().Program, EchoPrintArgs()));
        Assert(!denied!.Success, "A program outside the allow scope must be denied.");
        Assert(denied.Error?.Code == "SCOPE_DENIED", "An out-of-scope program must surface as SCOPE_DENIED.");
    }

    private static async Task SpawnsAndStreamsStdoutAsync()
    {
        using var service = new ChildProcessService();
        var dispatcher = NewDispatcher(service);
        var sink = new RecordingChannelSink();

        var (response, id) = await Spawn(dispatcher, sink, new ShellSpawnOptions(EchoProgram().Program, EchoPrintArgs()),
            ShellCapability([AllowedProgram()]));
        Assert(response!.Success, "An allowed spawn must succeed.");

        await WaitForTerminatedAsync(sink);
        var stdout = Reassemble(sink.Frames, "stdout");
        Assert(stdout.Contains("hello"), $"The child stdout must carry its output, got '{stdout}'.");
        var terminated = sink.TerminatedCode();
        Assert(terminated == 0, $"A clean echo must exit 0, got {terminated}.");
        Assert(!string.IsNullOrEmpty(id), "A spawn must return a session id.");
    }

    private static async Task StreamsStderrSeparatelyAsync()
    {
        using var service = new ChildProcessService();
        var dispatcher = NewDispatcher(service);
        var sink = new RecordingChannelSink();

        var (response, _) = await Spawn(dispatcher, sink, new ShellSpawnOptions(EchoProgram().Program, EchoErrArgs()),
            ShellCapability([AllowedProgram()]));
        Assert(response!.Success, "An allowed stderr-writing spawn must succeed.");

        await WaitForTerminatedAsync(sink);
        var stderr = Reassemble(sink.Frames, "stderr");
        Assert(stderr.Contains("boom"), $"The child stderr must carry its output, got '{stderr}'.");
        var stdout = Reassemble(sink.Frames, "stdout");
        Assert(stdout.Length == 0, "Nothing must be written to stdout for a stderr-only child.");
    }

    private static async Task ReportsExitCodeAsync()
    {
        using var service = new ChildProcessService();
        var dispatcher = NewDispatcher(service);
        var sink = new RecordingChannelSink();

        var (response, _) = await Spawn(dispatcher, sink, new ShellSpawnOptions(EchoProgram().Program, ExitArgs(7)),
            ShellCapability([AllowedProgram()]));
        Assert(response!.Success, "An allowed exit-code spawn must succeed.");

        await WaitForTerminatedAsync(sink);
        Assert(sink.TerminatedCode() == 7, $"The terminated frame must report exit code 7.");
    }

    private static async Task WritesToChildStdinAsync()
    {
        using var service = new ChildProcessService();
        var dispatcher = NewDispatcher(service);
        var sink = new RecordingChannelSink();

        var (response, id) = await Spawn(dispatcher, sink, new ShellSpawnOptions(EchoProgram().Program, EchoLineArgs()),
            ShellCapability([AllowedProgram()]));
        Assert(response!.Success, "An allowed echo-from-stdin spawn must succeed.");

        var caps = new CapabilitySet([ShellPlugin.StdinCommand]);
        var stdinRequest = new InvokeRequest(1, "sh-stdin", ShellPlugin.StdinCommand,
            JsonSerializer.SerializeToElement(new ShellWriteStdinOptions(id, Encoding.UTF8.GetBytes("hello\n")), TaruiJsonContext.Default.ShellWriteStdinOptions));
        var stdinResp = await dispatcher.DispatchJsonAsync(
            JsonSerializer.Serialize(stdinRequest, TaruiJsonContext.Default.InvokeRequest),
            new CommandContext("main", "main", caps));
        Assert(JsonSerializer.Deserialize(stdinResp, TaruiJsonContext.Default.InvokeResponse)!.Success,
            "Writing to a running child's stdin must succeed.");

        await WaitForTerminatedAsync(sink);
        var stdout = Reassemble(sink.Frames, "stdout");
        Assert(stdout.Contains("hello"), $"The child must echo the written line, got '{stdout}'.");
    }

    private static async Task KillsLongRunningChildAsync()
    {
        using var service = new ChildProcessService();
        var dispatcher = NewDispatcher(service);
        var sink = new RecordingChannelSink();

        var (response, id) = await Spawn(dispatcher, sink, new ShellSpawnOptions(LongRunningProgram().Program, LongRunningArgs()),
            ShellCapability([LongRunningProgram().Program]));
        Assert(response!.Success, "An allowed long-running spawn must succeed.");

        // 先确保子进程已起来、输出泵已接线，再 kill。
        await Task.Delay(300);
        var caps = new CapabilitySet([ShellPlugin.KillCommand]);
        var killRequest = new InvokeRequest(1, "sh-kill", ShellPlugin.KillCommand,
            JsonSerializer.SerializeToElement(new ShellKillOptions(id, KillTree: true), TaruiJsonContext.Default.ShellKillOptions));
        var killResp = await dispatcher.DispatchJsonAsync(
            JsonSerializer.Serialize(killRequest, TaruiJsonContext.Default.InvokeRequest),
            new CommandContext("main", "main", caps));
        Assert(JsonSerializer.Deserialize(killResp, TaruiJsonContext.Default.InvokeResponse)!.Success,
            "Killing a running child must succeed.");

        await WaitForTerminatedAsync(sink);
        // 被强杀后流必须仍以 terminated 帧收尾，且会话被回收；下一命令找不到该会话。
        var stale = JsonSerializer.Deserialize(
            await dispatcher.DispatchJsonAsync(
                JsonSerializer.Serialize(new InvokeRequest(1, "sh-stale", ShellPlugin.StdinCommand,
                    JsonSerializer.SerializeToElement(new ShellWriteStdinOptions(id, [1]),
                        TaruiJsonContext.Default.ShellWriteStdinOptions)), TaruiJsonContext.Default.InvokeRequest),
                new CommandContext("main", "main", new CapabilitySet([ShellPlugin.StdinCommand]))),
            TaruiJsonContext.Default.InvokeResponse);
        Assert(!stale!.Success, "The killed session must no longer accept stdin writes.");
    }

    private static async Task StdinCommandWithoutSessionFailsAsync()
    {
        using var service = new ChildProcessService();
        var dispatcher = NewDispatcher(service);
        var caps = new CapabilitySet([ShellPlugin.StdinCommand]);
        var request = new InvokeRequest(1, "sh-stdin-missing", ShellPlugin.StdinCommand,
            JsonSerializer.SerializeToElement(new ShellWriteStdinOptions("sh-does-not-exist", Encoding.UTF8.GetBytes("x")),
                TaruiJsonContext.Default.ShellWriteStdinOptions));
        var response = JsonSerializer.Deserialize(
            await dispatcher.DispatchJsonAsync(
                JsonSerializer.Serialize(request, TaruiJsonContext.Default.InvokeRequest),
                new CommandContext("main", "main", caps)),
            TaruiJsonContext.Default.InvokeResponse);
        Assert(!response!.Success, "Writing stdin to an unknown session must fail.");
    }

    // ---------- helpers ----------

    private static IpcDispatcher NewDispatcher(IChildProcessService service)
    {
        var builder = new CommandRouterBuilder();
        new ShellPlugin(service).ConfigureCommands(builder);
        return new IpcDispatcher(builder.Build());
    }

    private static async Task<(InvokeResponse? Response, string Id)> Spawn(
        IpcDispatcher dispatcher, RecordingChannelSink sink, ShellSpawnOptions options, CapabilitySet caps)
    {
        // 流式帧必须绑定到前端通道；未指定则强制分配一个，使输出泵能写入录制 sink。
        options = options with { Channel = options.Channel ?? "chan-" + DateTime.UtcNow.Ticks };
        var request = new InvokeRequest(1, "sh-" + DateTime.UtcNow.Ticks, ShellPlugin.SpawnCommand,
            JsonSerializer.SerializeToElement(options, TaruiJsonContext.Default.ShellSpawnOptions));
        var json = JsonSerializer.Serialize(request, TaruiJsonContext.Default.InvokeRequest);
        var responseText = await dispatcher.DispatchJsonAsync(
            json, new CommandContext("main", "main", caps), channelSink: sink);
        var response = JsonSerializer.Deserialize(responseText, TaruiJsonContext.Default.InvokeResponse);
        var id = response?.Payload?.Deserialize(TaruiJsonContext.Default.ShellSpawnResult)?.Id ?? string.Empty;
        return (response, id);
    }

    private static async Task<InvokeResponse?> DispatchSpawn(IpcDispatcher dispatcher, CapabilitySet caps, ShellSpawnOptions options)
    {
        var request = new InvokeRequest(1, "sh-" + DateTime.UtcNow.Ticks, ShellPlugin.SpawnCommand,
            JsonSerializer.SerializeToElement(options, TaruiJsonContext.Default.ShellSpawnOptions));
        var json = JsonSerializer.Serialize(request, TaruiJsonContext.Default.InvokeRequest);
        var responseText = await dispatcher.DispatchJsonAsync(json, new CommandContext("main", "main", caps));
        return JsonSerializer.Deserialize(responseText, TaruiJsonContext.Default.InvokeResponse);
    }

    private static CapabilitySet ShellCapability(string[] allow)
    {
        var scope = new PermissionScope([.. allow.Select(pattern => new PathScope(Path: pattern))], []);
        return new CapabilitySet(
            [ShellPlugin.SpawnCommand, ShellPlugin.StdinCommand, ShellPlugin.KillCommand],
            events: [],
            scopedPermissions: [new KeyValuePair<string, PermissionScope>(ShellPlugin.SpawnCommand, scope)]);
    }

    private static string AllowedProgram()
    {
        var (program, _) = EchoProgram();
        return program;
    }

    private static async Task WaitForTerminatedAsync(RecordingChannelSink sink, int timeoutMs = 15_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (sink.TerminatedCode() is not null)
            {
                return;
            }

            await Task.Delay(25);
        }

        throw new InvalidOperationException("Timed out waiting for the child process to emit a terminated frame.");
    }

    private static string Reassemble(IReadOnlyList<JsonElement> frames, string kind)
    {
        using var buffer = new MemoryStream();
        foreach (var frame in frames)
        {
            var item = frame.Deserialize(TaruiJsonContext.Default.ShellStreamEvent)!;
            if (item.Kind == kind && item.Data is not null)
            {
                buffer.Write(item.Data);
            }
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    // 使用真实本机命令作为子进程，保证跨平台确定性。

    private static (string Program, string[] Args) EchoProgram() =>
        OperatingSystem.IsWindows()
            ? ("cmd.exe", ["/c", "echo", "hello"])
            : ("/bin/sh", ["-c", "echo hello"]);

    private static string[] EchoPrintArgs() => EchoProgram().Args;

    private static string[] EchoErrArgs() =>
        OperatingSystem.IsWindows() ? ["/c", "echo boom 1>&2"] : ["-c", "echo boom >&2"];

    private static string[] EchoLineArgs() =>
        OperatingSystem.IsWindows() ? ["/v:on", "/c", "set /p x=& echo !x!"] : ["-c", "read x; echo $x"];

    private static string[] ExitArgs(int code)
    {
        var invariant = System.Globalization.CultureInfo.InvariantCulture;
        var token = code.ToString(invariant);
        return OperatingSystem.IsWindows() ? ["/c", "exit", token] : ["-c", $"exit {token}"];
    }

    private static (string Program, string[] Args) LongRunningProgram() =>
        OperatingSystem.IsWindows()
            ? ("cmd.exe", ["/c", "ping", "-t", "127.0.0.1"])
            : ("/bin/sh", ["-c", "sleep 60"]);

    private static string[] LongRunningArgs() => LongRunningProgram().Args;

    private sealed class RecordingChannelSink : IChannelSink
    {
        public List<JsonElement> Frames { get; } = [];

        public ValueTask SendAsync(string channelId, JsonElement payload, CancellationToken cancellationToken = default)
        {
            Frames.Add(payload);
            return ValueTask.CompletedTask;
        }

        public int? TerminatedCode()
        {
            foreach (var frame in Frames)
            {
                var item = frame.Deserialize(TaruiJsonContext.Default.ShellStreamEvent)!;
                if (item.Kind == "terminated")
                {
                    return item.Code;
                }
            }

            return null;
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
