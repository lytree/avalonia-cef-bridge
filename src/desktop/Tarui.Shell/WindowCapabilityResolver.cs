using Tarui.Ipc;

namespace Tarui.Shell;

/// <summary>
/// Resolves which <see cref="CapabilitySet"/> applies to a window label and guards window
/// creation against privilege escalation. A window must always resolve to an explicitly declared
/// profile; it never silently falls back to another window's capability set.
/// </summary>
public sealed class WindowCapabilityResolver(ICapabilityProvider provider)
{
    /// <summary>Resolves the capability for a window label or throws if no profile exists.</summary>
    public CapabilitySet Resolve(string label)
    {
        if (provider.Capabilities.TryGetValue(label, out var capability))
        {
            return capability;
        }

        throw new CapabilityNotFoundException(label);
    }

    /// <summary>
    /// Resolves the capability for a label to be created by <paramref name="caller"/>. The target
    /// must have an explicitly declared profile and its permission set must not exceed the caller's,
    /// so creator cannot escalate privileges by naming a more privileged label.
    /// </summary>
    public CapabilitySet ResolveForCreate(string label, CommandContext? caller)
    {
        var target = Resolve(label);
        if (caller is not null)
        {
            EnsureNotEscalation(target, caller, label);
        }

        return target;
    }

    private static void EnsureNotEscalation(CapabilitySet target, CommandContext caller, string label)
    {
        if (caller.Capabilities.Allows("*"))
        {
            return;
        }

        foreach (var permission in target.Permissions)
        {
            if (!caller.Capabilities.Allows(permission))
            {
                throw new PermissionDeniedException(
                    $"Creating window '{label}' would grant '{permission}' that the caller does not hold.");
            }
        }
    }
}