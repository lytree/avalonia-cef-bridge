using Microsoft.Extensions.Logging;
using Tarui.Contracts;
using Tarui.Ipc;
using Tarui.Plugins.Log;

namespace Tarui.Log.Tests;

internal static class Program
{
    public static async Task<int> Main()
    {
        try
        {
            await RecordForwardsToLoggerWithLevelAndCategoryAsync();
            await UnknownLevelDegradesToInformationAsync();
            await RecordUsesRendererDefaultCategoryWhenTargetAbsentAsync();
            await PluginRegistersRecordCommandAsync();
            RemoteProviderDefersToConfiguredCategory();
            RemoteLoggerFormatsMessageAndAppendsException();
            RemoteLoggerFiltersOutNoneLevel();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.ToString());
            return 1;
        }

        Console.WriteLine("Tarui.Log self-tests passed.");
        return 0;
    }

    private static async Task RecordForwardsToLoggerWithLevelAndCategoryAsync()
    {
        var logger = new CapturingLogger();
        var service = new LogService(new FakeLoggerFactory(logger));

        await service.RecordAsync(new LogRecordOptions(Level: "Warning", Message: "low disk", Target: "app"), default);

        Assert(logger.LastLevel == LogLevel.Warning, $"Warning level must be forwarded, but was {logger.LastLevel}.");
        Assert(logger.LastMessage == "low disk", "The message must reach the logger verbatim.");
        Assert(logger.LastCategory == "app", "The target must select the logger category.");
    }

    private static async Task UnknownLevelDegradesToInformationAsync()
    {
        var logger = new CapturingLogger();
        var service = new LogService(new FakeLoggerFactory(logger));

        await service.RecordAsync(new LogRecordOptions(Level: "Verbose", Message: "x", Target: "app"), default);

        Assert(logger.LastLevel == LogLevel.Information,
            $"Unknown levels must degrade to Information, but was {logger.LastLevel}.");
    }

    private static async Task RecordUsesRendererDefaultCategoryWhenTargetAbsentAsync()
    {
        var logger = new CapturingLogger();
        var service = new LogService(new FakeLoggerFactory(logger));

        await service.RecordAsync(new LogRecordOptions(Level: "Information", Message: "hello"), default);

        Assert(logger.LastCategory == "renderer", "Absent target must default the category to renderer.");
    }

    private static async Task PluginRegistersRecordCommandAsync()
    {
        var builder = new CommandRouterBuilder();
        var service = new RecordingLogService();
        new LogPlugin(service).ConfigureCommands(builder);
        var router = builder.Build();

        Assert(router.Commands.Contains("plugin:log|record"), "The plugin must register plugin:log|record.");
        Assert(router.RegisteredPermissions.Count == 1, "The log plugin must register exactly one permission.");
    }

    private static void RemoteProviderDefersToConfiguredCategory()
    {
        var sink = new CapturingSink();
        var provider = new RemoteLoggerProvider(sink);
        var logger = provider.CreateLogger("shell");

        logger.Log(LogLevel.Debug, 0, "booted", null, static (_, _) => "booted");

        Assert(sink.Entries.Count == 1, "A remote log must be published exactly once.");
        Assert(sink.Entries[0].Target == "shell", "The logger category must flow to the entry.");
        Assert(sink.Entries[0].Level == "Debug", "The entry level must reflect the LogLevel.");
        Assert(sink.Entries[0].Message == "booted", "The entry must carry the formatted message.");
        Assert(sink.Entries[0].TimestampMs > 0, "The entry must carry a non-zero epoch timestamp.");
    }

    private static void RemoteLoggerFormatsMessageAndAppendsException()
    {
        var sink = new CapturingSink();
        var logger = new RemoteLogger(sink, "app");

        logger.Log(
            LogLevel.Error,
            0,
            "failed 500",
            new InvalidOperationException("boom"),
            static (state, exception) => $"{state}{Environment.NewLine}{exception}");

        Assert(sink.Entries.Count == 1, "A single successful operation may log one entry.");
        Assert(sink.Entries[0].Message.Contains("500", StringComparison.Ordinal), "Structured args must be formatted into the message.");
        Assert(sink.Entries[0].Message.Contains("boom", StringComparison.Ordinal), "An exception must be appended for diagnostics.");
    }

    private static void RemoteLoggerFiltersOutNoneLevel()
    {
        var sink = new CapturingSink();
        var logger = new RemoteLogger(sink, "app");

        logger.Log(LogLevel.None, 0, "ignored", null, static (_, _) => "ignored");

        Assert(sink.Entries.Count == 0, "LogLevel.None must be filtered out.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class FakeLoggerFactory(CapturingLogger logger) : ILoggerFactory
    {
        public ILogger CreateLogger(string categoryName)
        {
            logger.LastCategory = categoryName;
            return logger;
        }

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class CapturingLogger : ILogger
    {
        public LogLevel LastLevel { get; private set; }
        public string? LastMessage { get; private set; }
        public string? LastCategory { get; set; }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            LastLevel = logLevel;
            LastMessage = formatter(state, exception);
        }
    }

    private sealed class CapturingSink : IRemoteLogSink
    {
        public List<LogEntry> Entries { get; } = [];

        public void Publish(LogEntry entry) => Entries.Add(entry);
    }

    private sealed class RecordingLogService : ILogService
    {
        public ValueTask<Unit> RecordAsync(LogRecordOptions options, CancellationToken cancellationToken) => ValueTask.FromResult(new Unit());
    }
}