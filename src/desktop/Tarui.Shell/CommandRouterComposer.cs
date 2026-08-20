using Microsoft.Extensions.DependencyInjection;
using Tarui.Ipc;

namespace Tarui.Shell;

public static class CommandRouterComposer
{
    public static CommandRouter Compose(IServiceProvider services)
    {
        var builder = new CommandRouterBuilder();
        foreach (var plugin in services.GetServices<ITaruiPlugin>())
        {
            plugin.ConfigureCommands(builder);
        }

        var capabilities = services.GetRequiredService<ICapabilityProvider>().Capabilities;
        var missingPermissions = capabilities.Values
            .SelectMany(static capability => capability.Permissions)
            .Where(permission => permission != "*" && !builder.RegisteredPermissions.Contains(permission))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (missingPermissions.Length > 0)
        {
            throw new InvalidOperationException(
                $"Capability files reference unregistered permissions: {string.Join(", ", missingPermissions)}");
        }

        return builder.Build();
    }
}
