using Microsoft.Extensions.DependencyInjection;
using Tarui.Ipc;

namespace Tarui.Shell;

public static class CommandRouterComposer
{
    /// <summary>
    /// Builds the command router from the composition-time registration pass and validates that every
    /// permission a capability file references actually exists. Capability manifests may reference
    /// permission sets (<c>core:window|default</c>); the loader has already expanded those into
    /// concrete permission ids by the time this check runs, so a set member that no plugin registered
    /// still fails startup.
    /// </summary>
    public static CommandRouter Compose(IServiceProvider services)
    {
        var builder = services.GetService<CommandRouterBuilder>() ?? CreateBuilder(services);

        var capabilities = services.GetRequiredService<ICapabilityProvider>().Capabilities;
        var missingPermissions = capabilities.Values
            .SelectMany(static capability => capability.Permissions)
            .Where(permission => permission != "*" && !builder.RegisteredPermissions.Contains(permission))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (missingPermissions.Length > 0)
        {
            var knownSets = builder.BuildPermissionSets()
                .Select(static definition => definition.Identifier)
                .Order(StringComparer.Ordinal)
                .ToArray();
            var hint = knownSets.Length == 0
                ? string.Empty
                : $" Permission sets available in this composition: {string.Join(", ", knownSets)}.";
            throw new InvalidOperationException(
                $"Capability files reference unregistered permissions: {string.Join(", ", missingPermissions)}.{hint}");
        }

        return builder.Build();
    }

    /// <summary>
    /// Runs the single composition-time plugin pass. <see cref="AddTaruiShell"/> registers the result as
    /// a singleton so the permission-set catalog and the router observe the same registrations instead
    /// of traversing the plugins twice.
    /// </summary>
    public static CommandRouterBuilder CreateBuilder(IServiceProvider services)
    {
        var builder = new CommandRouterBuilder();
        foreach (var plugin in services.GetServices<ITaruiPlugin>())
        {
            plugin.ConfigureCommands(builder);
        }

        return builder;
    }
}
