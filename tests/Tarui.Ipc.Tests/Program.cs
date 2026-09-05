using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tarui.Contracts;
using Tarui.Ipc;
using Tarui.Plugins.FileSystem;

namespace Tarui.Ipc.Tests;

internal static class Program
{
    public static async Task<int> Main()
    {
        await DeniesCommandsOutsideCapability();
        await DispatchesRegisteredCommandWithoutDynamicBinding();
        ResolvesPluginSingletonThroughServiceProvider();
        DeduplicatesRegisteredPermissions();
        ExposesRouterRegisteredPermissions();
        await HandlerExceptionsDoNotCorruptDispatcherAsync();
        await StreamsProgressFramesThroughBoundChannelAsync();
        await ChannelDegradesToInMemoryBufferWithoutSinkAsync();
        await DeniesStreamingCommandOutsideCapabilityAsync();
        await StreamsFileThroughBoundChannelAsync();
        await ReadStreamRespectsCumulativeBudgetAsync();
        await ReadStreamDegradesToInMemoryBufferWithoutSinkAsync();
        await DeniesReadFileStreamOutsideCapabilityAsync();
        await WriteChunkedCommitsAtomicallyAsync();
        await WriteCancelLeavesNoTempAndPreservesTargetAsync();
        await WriteChunkRejectsOutOfOrderSequenceAsync();
        await WriteCleanupRemovesSessionsForWindowAsync();
        LazilyCreatesAppOwnedBaseDirectory();
        Console.WriteLine("Tarui.Ipc self-tests passed.");
        return 0;
    }

    private static void LazilyCreatesAppOwnedBaseDirectory()
    {
        // 回归：首启时 %APPDATA%/tarui.net 等应用自有基目录不存在，store/fs 的读路径
        // （TryGetBaseDirectory）必须自动创建，否则报 "Base directory 'appData' is not
        // available on this system."。
        var policy = new FileAccessPolicy();
        var resolved = policy.ResolveBase("appData");
        Assert(resolved is not null, "The appData base must resolve to a physical path.");
        var createdByTest = !Directory.Exists(resolved);
        try
        {
            Assert(
                policy.TryGetBaseDirectory("appData", out var path, out var readOnly),
                "A missing app-owned base must be created lazily on first access.");
            Assert(Directory.Exists(path), "The created base directory must exist on disk.");
            Assert(!readOnly, "appData must be writable.");
        }
        finally
        {
            // 只清理测试自己创建的目录，绝不删除既有用户数据。
            if (createdByTest)
            {
                Directory.Delete(resolved!);
            }
        }
    }

    private static async Task DeniesCommandsOutsideCapability()
    {
        var builder = new CommandRouterBuilder();
        builder.Add(
            "test:echo",
            TaruiJsonContext.Default.EmptyArgs,
            TaruiJsonContext.Default.Unit,
            static (_, _, _) => ValueTask.FromResult(new Unit()),
            "test:echo");

        var response = await builder.Build().InvokeAsync(
            Request("1"),
            new CommandContext("main", "main", new CapabilitySet([])));

        Assert(!response.Success, "A command outside the capability must fail.");
        Assert(response.Error?.Code == "PERMISSION_DENIED", "The error must be PERMISSION_DENIED.");
    }

    private static async Task DispatchesRegisteredCommandWithoutDynamicBinding()
    {
        var builder = new CommandRouterBuilder();
        builder.Add(
            "test:echo",
            TaruiJsonContext.Default.EmptyArgs,
            TaruiJsonContext.Default.Unit,
            static (_, _, _) => ValueTask.FromResult(new Unit()),
            "test:echo");

        var dispatcher = new IpcDispatcher(builder.Build());
        var json = JsonSerializer.Serialize(Request("2"), TaruiJsonContext.Default.InvokeRequest);
        var response = await dispatcher.DispatchJsonAsync(
            json,
            new CommandContext("main", "main", new CapabilitySet(["test:echo"])));
        var parsed = JsonSerializer.Deserialize(response, TaruiJsonContext.Default.InvokeResponse);

        Assert(parsed is not null, "The dispatcher must return a response.");
        Assert(parsed!.Success, "An allowed command must succeed.");
    }

    private static void ResolvesPluginSingletonThroughServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddPlugin<TestPlugin>();

        using var provider = services.BuildServiceProvider();
        var first = provider.GetServices<ITaruiPlugin>().ToArray();
        var second = provider.GetServices<ITaruiPlugin>().ToArray();

        Assert(first.Length == 1, "AddPlugin must register exactly one plugin instance.");
        Assert(first[0] is TestPlugin, "The resolved plugin must be the registered implementation.");
        Assert(ReferenceEquals(first[0], second[0]), "Repeated resolutions must return the same singleton.");
    }

    private static void DeduplicatesRegisteredPermissions()
    {
        var builder = new CommandRouterBuilder();
        new TestPlugin().ConfigureCommands(builder);

        var permissions = builder.RegisteredPermissions;

        Assert(permissions.Count == 2, "Duplicate permissions must be deduplicated.");
        Assert(permissions.Contains("test:plugin|read"), "The shared permission must be registered.");
        Assert(permissions.Contains("test:plugin|write"), "The distinct permission must be registered.");
    }

    private static void ExposesRouterRegisteredPermissions()
    {
        var builder = new CommandRouterBuilder();
        new TestPlugin().ConfigureCommands(builder);
        var router = builder.Build();

        var expected = builder.RegisteredPermissions
            .OrderBy(static permission => permission, StringComparer.Ordinal)
            .ToArray();
        var actual = router.RegisteredPermissions
            .OrderBy(static permission => permission, StringComparer.Ordinal)
            .ToArray();

        Assert(expected.Length == 2, "The builder must expose the deduplicated permissions.");
        Assert(expected.SequenceEqual(actual), "The router must expose the builder's registered permissions.");
    }

    private static async Task HandlerExceptionsDoNotCorruptDispatcherAsync()
    {
        // A handler that throws must surface as a Web-facing failure on the response and leave
        // the dispatcher usable for subsequent invocations. Without this guard the bridge would
        // leak an unhandled rejection to the web layer and pin the dispatcher.
        var builder = new CommandRouterBuilder();
        builder.Add(
            "test:explode",
            TaruiJsonContext.Default.EmptyArgs,
            TaruiJsonContext.Default.Unit,
            static (_, _, _) => throw new InvalidOperationException("simulated-handler-failure"),
            "test:explode");
        builder.Add(
            "test:echo",
            TaruiJsonContext.Default.EmptyArgs,
            TaruiJsonContext.Default.Unit,
            static (_, _, _) => ValueTask.FromResult(new Unit()),
            "test:echo");

        var dispatcher = new IpcDispatcher(builder.Build());
        var explodingJson = JsonSerializer.Serialize(Request("explode-1"), TaruiJsonContext.Default.InvokeRequest);
        var exploding = JsonSerializer.Deserialize(
            await dispatcher.DispatchJsonAsync(
                explodingJson,
                new CommandContext("main", "main", new CapabilitySet(["test:explode"]))),
            TaruiJsonContext.Default.InvokeResponse);

        Assert(exploding is not null, "The dispatcher must produce a response envelope after a handler throws.");
        Assert(!exploding!.Success, "A handler exception must surface as a non-success response.");
        Assert(exploding.Error is not null, "The error envelope must be populated.");
        Assert(!exploding.Error!.Code.Contains("internal", StringComparison.OrdinalIgnoreCase),
            "Handler exceptions must not leak implementation details to the web layer.");

        // The dispatcher must remain usable for the next call after a handler throws.
        var echoJson = JsonSerializer.Serialize(Request("echo-1"), TaruiJsonContext.Default.InvokeRequest);
        var echo = JsonSerializer.Deserialize(
            await dispatcher.DispatchJsonAsync(
                echoJson,
                new CommandContext("main", "main", new CapabilitySet(["test:echo"]))),
            TaruiJsonContext.Default.InvokeResponse);
        Assert(echo is not null && echo.Success, "The dispatcher must continue to serve subsequent calls after a handler throws.");
    }

    private static async Task StreamsProgressFramesThroughBoundChannelAsync()
    {
        // The front-end serializes a Channel into payload marker { id }, the native side deserializes
        // StreamEchoArgs and binds the TaruiChannel to the invoking webview's IChannelSink. Every
        // SendAsync from the handler must stream a ChannelEnvelope frame back to that sink.
        const string channelId = "chan-7";
        var builder = AddStreamEcho(StreamEchoHandler);
        var dispatcher = new IpcDispatcher(builder.Build());
        var sink = new RecordingChannelSink();

        var response = await dispatcher.DispatchJsonAsync(
            JsonSerializer.Serialize(StreamEchoRequest("stream-1", channelId, 3), TaruiJsonContext.Default.InvokeRequest),
            new CommandContext("main", "main", new CapabilitySet([StreamEchoPermission])),
            channelSink: sink);

        var parsed = JsonSerializer.Deserialize(response, TaruiJsonContext.Default.InvokeResponse);
        Assert(parsed is not null && parsed!.Success, "A streaming command must succeed.");

        Assert(sink.Frames.Count == 3, $"The handler must stream {3} frames, got {sink.Frames.Count}.");
        Assert(sink.Frames.All(frame => frame.Id == channelId),
            "Every frame must be routed to the bound channel id.");
        foreach (var (frame, expectedStep) in sink.Frames.Select((f, i) => (f, i)))
        {
            var progress = frame.Payload.Deserialize(TaruiJsonContext.Default.StreamProgress)!;
            Assert(progress.Step == expectedStep, $"Frame {expectedStep} must report step {expectedStep}.");
            Assert(progress.Total == 3, "Every frame must report the total count.");
        }
    }

    private static async Task ChannelDegradesToInMemoryBufferWithoutSinkAsync()
    {
        // Without a sink (library/test path) the channel must fall back to an in-memory buffer rather
        // than crash, and reads round-trip through ReadAllAsync.
        var builder = AddStreamEcho(StreamEchoHandler);
        var router = builder.Build();

        var payload = JsonSerializer.SerializeToElement(UnboundStreamEchoArgs(5), TaruiJsonContext.Default.StreamEchoArgs);
        var response = await router.InvokeAsync(
            new InvokeRequest(1, "stream-2", StreamEchoCommand, payload),
            new CommandContext("main", "main", new CapabilitySet([StreamEchoPermission])));

        Assert(response.Success, "A streaming command without a sink must still succeed.");
    }

    private static async Task DeniesStreamingCommandOutsideCapabilityAsync()
    {
        var builder = AddStreamEcho(StreamEchoHandler);
        var dispatcher = new IpcDispatcher(builder.Build());
        var sink = new RecordingChannelSink();

        var response = await dispatcher.DispatchJsonAsync(
            JsonSerializer.Serialize(StreamEchoRequest("stream-3", "chan-8", 2), TaruiJsonContext.Default.InvokeRequest),
            new CommandContext("main", "main", new CapabilitySet([])),
            channelSink: sink);

        var parsed = JsonSerializer.Deserialize(response, TaruiJsonContext.Default.InvokeResponse);
        Assert(parsed is not null && !parsed!.Success, "A streaming command outside the capability must fail.");
        Assert(parsed!.Error?.Code == "PERMISSION_DENIED", "The streaming denial must be PERMISSION_DENIED.");
        Assert(sink.Frames.Count == 0, "No frames may be streamed when the command is denied.");
    }

    private static async Task StreamsFileThroughBoundChannelAsync()
    {
        // A file larger than the 8 MiB text cap is streamed in chunks through a bound channel.
        // The first frame carries meta (size), the rest carry data; the byte content must round-trip.
        using var fixture = new TempFileFixture(700_000);
        var service = new FileSystemService(new FileAccessPolicy());
        var builder = AddReadFileStream(service);
        var dispatcher = new IpcDispatcher(builder.Build());
        var sink = new RecordingChannelSink();

        var response = await dispatcher.DispatchJsonAsync(
            JsonSerializer.Serialize(
                ReadFileStreamRequest("fs-stream-1", fixture.Name, 200_000, "chan-fs"),
                TaruiJsonContext.Default.InvokeRequest),
            new CommandContext("main", "main", new CapabilitySet([ReadFileStreamPermission])),
            channelSink: sink);

        var parsed = JsonSerializer.Deserialize(response, TaruiJsonContext.Default.InvokeResponse);
        Assert(parsed is not null && parsed!.Success,
            $"read-file-stream must succeed, got: {parsed?.Error?.Message}");

        var totalBytes = parsed!.Payload!.Value.Deserialize(TaruiJsonContext.Default.FsStreamResult);
        Assert(totalBytes!.Size == fixture.Content.Length,
            $"The result must report the file size, got {totalBytes.Size}.");

        var chunks = sink.Frames.Count;
        Assert(chunks > 1, $"A multi-chunk file must stream more than one frame, got {chunks}.");
        Assert(sink.Frames.All(frame => frame.Id == "chan-fs"), "Every frame must route to the bound channel.");

        var (reassembled, metaSize, received) = ReassembleFrames(sink.Frames);
        Assert(metaSize == fixture.Content.Length, "The meta frame must carry the true file size.");
        Assert(received == fixture.Content.Length, "The chunk data must sum to the file size.");
        Assert(Sha256(reassembled) == fixture.Hash, "The streamed bytes must match the original file.");
    }

    private static async Task ReadStreamRespectsCumulativeBudgetAsync()
    {
        // 流式读豁免 per-operation 上限，但累计预算必须仍生效：超过 MaxTotalBytes 拒绝。
        using var fixture = new TempFileFixture(2048);
        var policy = new FileAccessPolicy(new FileAccessPolicyOptions(MaxTotalBytes: 512));
        var service = new FileSystemService(policy);
        var dispatcher = new IpcDispatcher(AddReadFileStream(service).Build());
        var sink = new RecordingChannelSink();

        var response = await dispatcher.DispatchJsonAsync(
            JsonSerializer.Serialize(
                ReadFileStreamRequest("fs-stream-budget", fixture.Name, 1024, "chan-fs-budget"),
                TaruiJsonContext.Default.InvokeRequest),
            new CommandContext("main", "main", new CapabilitySet([ReadFileStreamPermission])),
            channelSink: sink);

        var parsed = JsonSerializer.Deserialize(response, TaruiJsonContext.Default.InvokeResponse);
        Assert(parsed is not null && !parsed!.Success,
            "A read exceeding the cumulative budget must fail.");
        Assert(parsed!.Error?.Code == "PATH_DENIED",
            "The budget rejection must surface as PATH_DENIED.");
        Assert(sink.Frames.Count == 0, "No frames may stream when the cumulative budget is exhausted.");
    }

    private static async Task ReadStreamDegradesToInMemoryBufferWithoutSinkAsync()
    {
        // Without a sink (library/test path) streaming must fall back to the in-memory channel
        // and the command still succeeds, mirroring the generic channel degradation behavior.
        using var fixture = new TempFileFixture(4096);
        var service = new FileSystemService(new FileAccessPolicy());
        var router = AddReadFileStream(service).Build();

        var payload = JsonSerializer.SerializeToElement(
            new FsReadStreamOptions("temp", fixture.Name, ChunkBytes: 1024, Channel: "chan-fs-unbound"),
            TaruiJsonContext.Default.FsReadStreamOptions);
        var response = await router.InvokeAsync(
            new InvokeRequest(1, "fs-stream-unbound", ReadFileStreamCommand, payload),
            new CommandContext("main", "main", new CapabilitySet([ReadFileStreamPermission])));

        Assert(response.Success, "A streamed read without a sink must still succeed.");
        var result = response.Payload?.Deserialize(TaruiJsonContext.Default.FsStreamResult);
        Assert(result?.Size == fixture.Content.Length, "The unbound stream must report the file size.");
    }

    private static async Task DeniesReadFileStreamOutsideCapabilityAsync()
    {
        using var fixture = new TempFileFixture(64);
        var service = new FileSystemService(new FileAccessPolicy());
        var dispatcher = new IpcDispatcher(AddReadFileStream(service).Build());
        var sink = new RecordingChannelSink();

        var response = await dispatcher.DispatchJsonAsync(
            JsonSerializer.Serialize(
                ReadFileStreamRequest("fs-stream-denied", fixture.Name, 64, "chan-fs-denied"),
                TaruiJsonContext.Default.InvokeRequest),
            new CommandContext("main", "main", new CapabilitySet([])),
            channelSink: sink);

        var parsed = JsonSerializer.Deserialize(response, TaruiJsonContext.Default.InvokeResponse);
        Assert(parsed is not null && !parsed!.Success, "A streamed read outside the capability must fail.");
        Assert(parsed!.Error?.Code == "PERMISSION_DENIED", "The streaming denial must be PERMISSION_DENIED.");
        Assert(sink.Frames.Count == 0, "No frames may be streamed when the command is denied.");
    }

    private static async Task WriteChunkedCommitsAtomicallyAsync()
    {
        // Target file is created on disk only on commit; until then the old content survives and
        // the write stays buffered to a temp file (atomic-replace semantics).
        using var fixture = new TempFileFixture(0);
        var targetPath = Path.Combine(WriteTempBase, fixture.Name);
        File.WriteAllText(targetPath, "OLD");
        var service = new FileSystemService(new FileAccessPolicy());
        var dispatcher = new IpcDispatcher(AddWriteSessions(service).Build());
        var caps = new CapabilitySet(WriteSessionPermissions);

        var begin = await DispatchWrite(dispatcher, BeginRequest("w-1", fixture.Name, 4), caps);
        Assert(begin.Success, "write-begin must succeed.");
        var writeId = begin.Payload!.Value.Deserialize(TaruiJsonContext.Default.FsWriteBeginResult)!.WriteId;
        Assert(writeId.Length > 0, "write-begin must return a write id.");
        Assert(File.ReadAllText(targetPath) == "OLD", "The target must stay untouched before commit.");
        Assert(CountTempLeftovers(fixture.Name) == 1, "A buffered temp file must exist before commit.");

        Assert((await DispatchWrite(dispatcher, ChunkRequest("w-2", writeId, 0, "AB"), caps)).Success, "chunk 0 must succeed.");
        Assert((await DispatchWrite(dispatcher, ChunkRequest("w-3", writeId, 1, "CD"), caps)).Success, "chunk 1 must succeed.");
        Assert(File.ReadAllText(targetPath) == "OLD", "The target must still be untouched after chunks, before commit.");

        var commit = await DispatchWrite(dispatcher, CommitRequest("w-4", writeId), caps);
        Assert(commit.Success, "write-commit must succeed.");
        Assert(File.ReadAllText(targetPath) == "ABCD", "The committed file must contain the streamed bytes.");
        Assert(CountTempLeftovers(fixture.Name) == 0, "No temp file may remain after commit.");
    }

    private static async Task WriteCancelLeavesNoTempAndPreservesTargetAsync()
    {
        using var fixture = new TempFileFixture(0);
        var targetPath = Path.Combine(WriteTempBase, fixture.Name);
        File.WriteAllText(targetPath, "OLD");
        var service = new FileSystemService(new FileAccessPolicy());
        var dispatcher = new IpcDispatcher(AddWriteSessions(service).Build());
        var caps = new CapabilitySet(WriteSessionPermissions);

        var begin = await DispatchWrite(dispatcher, BeginRequest("wc-1", fixture.Name, null), caps);
        var writeId = begin.Payload!.Value.Deserialize(TaruiJsonContext.Default.FsWriteBeginResult)!.WriteId;
        Assert((await DispatchWrite(dispatcher, ChunkRequest("wc-2", writeId, 0, "aa"), caps)).Success, "chunk must succeed.");
        Assert(CountTempLeftovers(fixture.Name) == 1, "A buffered temp file must exist before cancel.");

        var cancel = await DispatchWrite(dispatcher, CancelRequest("wc-3", writeId), caps);
        Assert(cancel.Success, "write-cancel must succeed.");
        Assert(File.ReadAllText(targetPath) == "OLD", "A cancelled write must preserve the target.");
        Assert(CountTempLeftovers(fixture.Name) == 0, "A cancelled write must remove its temp file.");
    }

    private static async Task WriteChunkRejectsOutOfOrderSequenceAsync()
    {
        using var fixture = new TempFileFixture(0);
        var service = new FileSystemService(new FileAccessPolicy());
        var dispatcher = new IpcDispatcher(AddWriteSessions(service).Build());
        var caps = new CapabilitySet(WriteSessionPermissions);

        var begin = await DispatchWrite(dispatcher, BeginRequest("wo-1", fixture.Name, null), caps);
        var writeId = begin.Payload!.Value.Deserialize(TaruiJsonContext.Default.FsWriteBeginResult)!.WriteId;

        // The first chunk must be sequence 0; a chunk at sequence 1 is out of order and rejected.
        var rejected = await DispatchWrite(dispatcher, ChunkRequest("wo-2", writeId, 1, "zz"), caps);
        Assert(!rejected.Success, "An out-of-order chunk must be rejected.");
        Assert(rejected.Error?.Code == "PATH_DENIED", "An out-of-order chunk must surface as PATH_DENIED.");

        // 被拒绝后会话仍存活，可正常按序补写完再取消，验证未被污染。
        var okChunk = await DispatchWrite(dispatcher, ChunkRequest("wo-3", writeId, 0, "ok"), caps);
        Assert(okChunk.Success, "The session must survive an out-of-order rejection.");
        var cancel = await DispatchWrite(dispatcher, CancelRequest("wo-4", writeId), caps);
        Assert(cancel.Success, "write-cancel must succeed after a rejected chunk.");
    }

    private static async Task WriteCleanupRemovesSessionsForWindowAsync()
    {
        using var fixture = new TempFileFixture(0);
        var service = new FileSystemService(new FileAccessPolicy());

        // 直接调用服务以精确控制 windowLabel。
        var other = await service.WriteBeginAsync(
            new FsWriteBeginOptions("temp", fixture.Name, TotalBytes: 4), "other", default);
        var doomed = await service.WriteBeginAsync(
            new FsWriteBeginOptions("temp", fixture.Name, TotalBytes: 4), "main", default);

        Assert(CountTempLeftovers(fixture.Name) == 2, "Both windows must have buffered temp files.");

        service.CleanupWindow("main");

        Assert(CountTempLeftovers(fixture.Name) == 1, "CleanupWindow must abandon only the target window's session.");
        await ThrowsAsync<InvalidOperationException>(
            async () => await service.WriteCommitAsync(new FsWriteCommitOptions(doomed.WriteId), default),
            "Committing a cleaned-up session must fail.");

        // 其他窗口的会话不受影响，仍可正常提交。
        await service.WriteChunkAsync(new FsWriteChunkOptions(other.WriteId, System.Text.Encoding.UTF8.GetBytes("WXYZ"), 0), default);
        await service.WriteCommitAsync(new FsWriteCommitOptions(other.WriteId), default);
        Assert(File.ReadAllText(Path.Combine(WriteTempBase, fixture.Name)) == "WXYZ",
            "The surviving window's write must commit intact.");
    }

    private const string WriteBeginCommand = "plugin:fs|write-begin";
    private const string WriteChunkCommand = "plugin:fs|write-chunk";
    private const string WriteCommitCommand = "plugin:fs|write-commit";
    private const string WriteCancelCommand = "plugin:fs|write-cancel";
    private static readonly string[] WriteSessionPermissions =
        [WriteBeginCommand, WriteChunkCommand, WriteCommitCommand, WriteCancelCommand];

    private static readonly string WriteTempBase = new FileAccessPolicy().ResolveBase("temp")!;

    private static CommandRouterBuilder AddWriteSessions(FileSystemService service)
    {
        var builder = new CommandRouterBuilder();
        builder.Add(
            WriteBeginCommand,
            TaruiJsonContext.Default.FsWriteBeginOptions,
            TaruiJsonContext.Default.FsWriteBeginResult,
            (ops, ctx, ct) => service.WriteBeginAsync(ops, ctx.WindowLabel, ct),
            WriteBeginCommand);
        builder.Add(
            WriteChunkCommand,
            TaruiJsonContext.Default.FsWriteChunkOptions,
            TaruiJsonContext.Default.Unit,
            (ops, _ctx, ct) => service.WriteChunkAsync(ops, ct),
            WriteChunkCommand);
        builder.Add(
            WriteCommitCommand,
            TaruiJsonContext.Default.FsWriteCommitOptions,
            TaruiJsonContext.Default.Unit,
            (ops, _ctx, ct) => service.WriteCommitAsync(ops, ct),
            WriteCommitCommand);
        builder.Add(
            WriteCancelCommand,
            TaruiJsonContext.Default.FsWriteCancelOptions,
            TaruiJsonContext.Default.Unit,
            (ops, _ctx, ct) => service.WriteCancelAsync(ops, ct),
            WriteCancelCommand);
        return builder;
    }

    private static async Task<InvokeResponse> DispatchWrite(IpcDispatcher dispatcher, InvokeRequest request, CapabilitySet caps)
        => JsonSerializer.Deserialize(
            await dispatcher.DispatchJsonAsync(JsonSerializer.Serialize(request, TaruiJsonContext.Default.InvokeRequest),
                new CommandContext("main", "main", caps)),
            TaruiJsonContext.Default.InvokeResponse)!;

    private static InvokeRequest BeginRequest(string id, string path, long? total) => new(
        1, id, WriteBeginCommand,
        JsonSerializer.SerializeToElement(new FsWriteBeginOptions("temp", path, total), TaruiJsonContext.Default.FsWriteBeginOptions));

    private static InvokeRequest ChunkRequest(string id, string writeId, long sequence, string data) => new(
        1, id, WriteChunkCommand,
        JsonSerializer.SerializeToElement(new FsWriteChunkOptions(writeId, System.Text.Encoding.UTF8.GetBytes(data), sequence),
            TaruiJsonContext.Default.FsWriteChunkOptions));

    private static InvokeRequest CommitRequest(string id, string writeId) => new(
        1, id, WriteCommitCommand,
        JsonSerializer.SerializeToElement(new FsWriteCommitOptions(writeId), TaruiJsonContext.Default.FsWriteCommitOptions));

    private static InvokeRequest CancelRequest(string id, string writeId) => new(
        1, id, WriteCancelCommand,
        JsonSerializer.SerializeToElement(new FsWriteCancelOptions(writeId), TaruiJsonContext.Default.FsWriteCancelOptions));

    private static int CountTempLeftovers(string fileName) =>
        Directory.GetFiles(WriteTempBase, $".{fileName}.*.tmp").Length;

    private static async Task ThrowsAsync<TException>(Func<ValueTask> action, string message)
        where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException(message);
    }

    private const string ReadFileStreamCommand = "plugin:fs|read-file-stream";
    private const string ReadFileStreamPermission = "plugin:fs|read-file-stream";

    private static CommandRouterBuilder AddReadFileStream(FileSystemService service)
    {
        var builder = new CommandRouterBuilder();
        builder.Add(
            ReadFileStreamCommand,
            TaruiJsonContext.Default.FsReadStreamOptions,
            TaruiJsonContext.Default.FsStreamResult,
            (FsReadStreamOptions options, CommandContext _context, CancellationToken ct)
                => service.ReadFileStreamAsync(options, ct),
            ReadFileStreamPermission);
        return builder;
    }

    private static InvokeRequest ReadFileStreamRequest(string id, string path, long chunkBytes, string channelId) => new(
        1, id, ReadFileStreamCommand,
        JsonSerializer.SerializeToElement(new FsReadStreamOptions("temp", path, chunkBytes, channelId),
            TaruiJsonContext.Default.FsReadStreamOptions));

    private static (byte[] Data, long Size, long Received) ReassembleFrames(IEnumerable<RecordingChannelSink.CapturedFrame> frames)
    {
        using var buffer = new MemoryStream();
        long size = 0;
        foreach (var frame in frames)
        {
            var item = frame.Payload.Deserialize(TaruiJsonContext.Default.FsStreamEvent)!;
            if (item.Kind == "meta")
            {
                size = item.Meta!.Size;
            }
            else if (item.Kind == "chunk" && item.Data is not null)
            {
                buffer.Write(item.Data);
            }
        }

        return (buffer.ToArray(), size, buffer.Length);
    }

    private static string Sha256(byte[] data) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data));

    private static async ValueTask<Unit> StreamEchoHandler(
        StreamEchoArgs args,
        CommandContext context,
        CancellationToken cancellationToken)
    {
        var channel = ChannelContext.Bind<StreamProgress>(args.Channel);
        for (var step = 0; step < args.Count && !cancellationToken.IsCancellationRequested; step++)
        {
            await channel.SendAsync(new StreamProgress(step, args.Count), cancellationToken);
        }

        return new Unit();
    }

    private const string StreamEchoCommand = "core:channel|stream-echo";
    private const string StreamEchoPermission = "core:channel|stream-echo";

    private static CommandRouterBuilder AddStreamEcho(
        Func<StreamEchoArgs, CommandContext, CancellationToken, ValueTask<Unit>> handler)
    {
        var builder = new CommandRouterBuilder();
        builder.Add(
            StreamEchoCommand,
            TaruiJsonContext.Default.StreamEchoArgs,
            TaruiJsonContext.Default.Unit,
            handler,
            StreamEchoPermission);
        return builder;
    }

    private static StreamEchoArgs UnboundStreamEchoArgs(int count)
        => new(Channel: null, count);

    private static InvokeRequest StreamEchoRequest(string id, string channelId, int count)
    {
        var payload = JsonSerializer.Deserialize<JsonElement>(
            $"{{\"channel\":\"{channelId}\",\"count\":{count}}}");
        return new InvokeRequest(1, id, StreamEchoCommand, payload);
    }

    private sealed class RecordingChannelSink : IChannelSink
    {
        public List<CapturedFrame> Frames { get; } = [];

        public ValueTask SendAsync(string channelId, JsonElement payload, CancellationToken cancellationToken = default)
        {
            Frames.Add(new CapturedFrame(channelId, payload));
            return ValueTask.CompletedTask;
        }

        public sealed record CapturedFrame(string Id, JsonElement Payload);
    }

    /// <summary>
    /// Creates a patterned file under the <c>temp</c> base so streaming read tests operate on a real,
    /// policy-resolvable path, and removes it on dispose.
    /// </summary>
    private sealed class TempFileFixture : IDisposable
    {
        private readonly string _path;
        private bool _disposed;

        /// <summary>The base-relative file name the service authorizes.</summary>
        public string Name { get; }

        /// <summary>The exact bytes written to the fixture file.</summary>
        public byte[] Content { get; }

        /// <summary>SHA-256 of <see cref="Content"/>, used to verify streamed round-trip integrity.</summary>
        public string Hash { get; }

        public TempFileFixture(int size)
        {
            var tempBase = new FileAccessPolicy().ResolveBase("temp")!;
            Name = Path.GetFileName(Path.GetRandomFileName()) + ".bin";
            Content = [.. Enumerable.Range(0, size).Select(i => (byte)(i % 251))];
            _path = Path.Combine(tempBase, Name);
            File.WriteAllBytes(_path, Content);
            Hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Content));
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }
        }
    }

    private static InvokeRequest Request(string id) => new(
        1,
        id,
        "test:echo",
        JsonSerializer.SerializeToElement(new EmptyArgs(), TaruiJsonContext.Default.EmptyArgs));

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class TestPlugin : ITaruiPlugin
    {
        public TestPlugin()
        {
        }

        public void ConfigureCommands(CommandRouterBuilder commands)
        {
            commands.Add(
                "test:plugin|read-value",
                TaruiJsonContext.Default.EmptyArgs,
                TaruiJsonContext.Default.Unit,
                static (_, _, _) => ValueTask.FromResult(new Unit()),
                "test:plugin|read");
            commands.Add(
                "test:plugin|read-cache",
                TaruiJsonContext.Default.EmptyArgs,
                TaruiJsonContext.Default.Unit,
                static (_, _, _) => ValueTask.FromResult(new Unit()),
                "test:plugin|read");
            commands.Add(
                "test:plugin|write",
                TaruiJsonContext.Default.EmptyArgs,
                TaruiJsonContext.Default.Unit,
                static (_, _, _) => ValueTask.FromResult(new Unit()),
                "test:plugin|write");
        }
    }
}
