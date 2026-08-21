using Microsoft.Extensions.DependencyInjection;
using Tarui.Contracts;
using Tarui.Ipc;

namespace Tarui.Plugins.Log;

/// <summary>The reserved event carrying <see cref="LogEntry"/> payloads to authorized windows.</summary>
public static class LogEventNames
{
    public const string Entry = "log://entry";
}

/// <summary>
/// Forward serialized log lines into the host logging pipeline so renderer diagnostics share the
/// desktop sink chain (file, console, subsystem providers).
/// </summary>
public interface ILogService
{
    ValueTask<Unit> RecordAsync(LogRecordOptions options, CancellationToken cancellationToken);
}

public sealed class LogPlugin(ILogService service) : ITaruiPlugin
{
    public void ConfigureCommands(CommandRouterBuilder commands)
    {
        commands.Add(
            "plugin:log|record",
            TaruiJsonContext.Default.LogRecordOptions,
            TaruiJsonContext.Default.Unit,
            (options, _, ct) => service.RecordAsync(options, ct),
            "plugin:log|record");
    }
}

public static class LogPluginServiceCollectionExtensions
{
    public static IServiceCollection AddLogPlugin(this IServiceCollection services) => services
        .AddSingleton<ILogService, LogService>()
        .AddPlugin<LogPlugin>();
}