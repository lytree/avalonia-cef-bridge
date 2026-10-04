using Microsoft.Extensions.DependencyInjection;
using Tarui.Contracts;
using Tarui.Ipc;

namespace Tarui.Plugins.PersistedScope;

/// <summary>
/// Receives persisted-scope change notifications so the shell can fan them out to windows as the
/// native <c>scope://changed</c> event. The shell owns the wiring; when no sink is registered the
/// plugin still works and simply skips the notification.
/// </summary>
public interface IPersistedScopeEventSink
{
    void Changed(PersistedScopeChangedEvent e);
}

/// <summary>
/// Registers the <c>plugin:persisted-scope|scope-allow</c>, <c>scope-deny</c>, and
/// <c>scope-reset</c> commands. Mutations are validated against the fs/http allowlist inside the
/// service (a disallowed permission surfaces as COMMAND_FAILED) and announced through the optional
/// event sink once the change is durable.
/// </summary>
public sealed class PersistedScopePlugin(
    IPersistedScopeService service,
    IPersistedScopeEventSink? sink = null) : ITaruiPlugin
{
    public const string AllowCommand = "plugin:persisted-scope|scope-allow";
    public const string DenyCommand = "plugin:persisted-scope|scope-deny";
    public const string ResetCommand = "plugin:persisted-scope|scope-reset";

    public void ConfigureCommands(CommandRouterBuilder commands)
    {
        commands.Add(
            AllowCommand,
            TaruiJsonContext.Default.PersistedScopeOptions,
            TaruiJsonContext.Default.Unit,
            async (options, _, ct) =>
            {
                var result = await service.AllowAsync(options, ct);
                sink?.Changed(new PersistedScopeChangedEvent(options.Permission, "allow"));
                return result;
            },
            AllowCommand);

        commands.Add(
            DenyCommand,
            TaruiJsonContext.Default.PersistedScopeOptions,
            TaruiJsonContext.Default.Unit,
            async (options, _, ct) =>
            {
                var result = await service.DenyAsync(options, ct);
                sink?.Changed(new PersistedScopeChangedEvent(options.Permission, "deny"));
                return result;
            },
            DenyCommand);

        commands.Add(
            ResetCommand,
            TaruiJsonContext.Default.PersistedScopeResetOptions,
            TaruiJsonContext.Default.Unit,
            async (options, _, ct) =>
            {
                var result = await service.ResetAsync(options, ct);
                sink?.Changed(new PersistedScopeChangedEvent(options.Permission ?? "*", "reset"));
                return result;
            },
            ResetCommand);
    }
}

public static class PersistedScopePluginServiceCollectionExtensions
{
    public static IServiceCollection AddPersistedScopePlugin(this IServiceCollection services) => services
        .AddFileAccessPolicy()
        .AddSingleton<PersistedScopeService>()
        .AddSingleton<IRuntimeScopeOverlay>(static services => services.GetRequiredService<PersistedScopeService>())
        .AddSingleton<IPersistedScopeService>(static services => services.GetRequiredService<PersistedScopeService>())
        .AddPlugin<PersistedScopePlugin>();
}
