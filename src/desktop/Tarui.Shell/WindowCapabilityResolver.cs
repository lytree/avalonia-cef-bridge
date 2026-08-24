using Tarui.Contracts;
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
    /// must have an explicitly declared profile and its permission set, scoped allow/deny, and
    /// reserved-event set must not exceed the caller's, so the creator cannot escalate privileges by
    /// naming a more privileged label.
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

    /// <summary>
    /// Compares <paramref name="target"/> against <paramref name="caller"/> permission by permission.
    /// Caller with wildcard "<c>*</c>" is treated as root and never escalates. Every other case
    /// requires the caller to hold the same permission id, every deny rule the caller has on that
    /// permission must also be present on the target (otherwise target expands what the caller
    /// could deny), every allow rule the caller has must already be covered by the target's allow
    /// list (otherwise target broadens what the caller can read), and every reserved event the
    /// target declares must also be declared by the caller.
    /// </summary>
    private static void EnsureNotEscalation(CapabilitySet target, CommandContext caller, string label)
    {
        if (caller.Capabilities.Allows("*"))
        {
            return;
        }

        // Compare permission scopes. A target with no scope for a permission id is treated as
        // having an empty allow / full deny universe, so any structured scope the caller holds
        // must match the target's.
        foreach (var permission in target.Permissions)
        {
            if (permission == "*")
            {
                // A target wildcard is strictly broader than any caller that does not also hold
                // its own wildcard (handled above).
                throw new PermissionDeniedException(
                    "Creating window '" + label + "' would grant '*' that the caller does not hold.");
            }

            if (!caller.Capabilities.Allows(permission))
            {
                throw new PermissionDeniedException(
                    "Creating window '" + label + "' would grant '" + permission + "' that the caller does not hold.");
            }

            if (!caller.Capabilities.TryGetScope(permission, out var callerScope))
            {
                if (target.TryGetScope(permission, out var targetOnly) && HasAnyEntries(targetOnly))
                {
                    throw new PermissionDeniedException(
                        "Creating window '" + label + "' would widen '" + permission + "' (the caller has no scope but the target has one).");
                }

                continue;
            }

            if (!target.TryGetScope(permission, out var targetScope))
            {
                throw new PermissionDeniedException(
                    "Creating window '" + label + "' would widen '" + permission + "' (the caller has a scope but the target does not).");
            }

            CompareScope(permission, label, callerScope, targetScope);
        }

        // Events: any reserved event the target declares must already be declared by the caller.
        foreach (var eventName in target.Events)
        {
            if (!caller.Capabilities.AllowsEvent(eventName))
            {
                throw new PermissionDeniedException(
                    "Creating window '" + label + "' would expose reserved event '" + eventName + "' that the caller does not receive.");
            }
        }
    }

    private static void CompareScope(string permission, string label, PermissionScope caller, PermissionScope target)
    {
        // Deny rule expansion: caller.deny must be a subset of target.deny (otherwise target
        // removes a deny the caller had, which is a privilege gain).
        if (caller.Deny.Count > 0 && target.Deny.Count == 0)
        {
            throw new PermissionDeniedException(
                "Creating window '" + label + "' would narrow deny on '" + permission + "' (the caller denies entries the target would accept).");
        }

        foreach (var callerDeny in caller.Deny)
        {
            if (!target.Deny.Contains(callerDeny))
            {
                throw new PermissionDeniedException(
                    "Creating window '" + label + "' would narrow deny on '" + permission + "' (entry '" + Format(callerDeny) + "' is denied by the caller but not by the target).");
            }
        }

        // Allow rule expansion: caller.allow must remain a subset of target.allow. An empty
        // caller.allow here already failed the empty-set check earlier (no scope at all), so we
        // treat any caller entry as a constraint. An empty target.allow is dangerous only when
        // the caller does not hold '*', which we have already excluded.
        if (caller.Allow.Count > 0)
        {
            if (target.Allow.Count == 0)
            {
                throw new PermissionDeniedException(
                    "Creating window '" + label + "' would widen allow on '" + permission + "' (the caller restricts entries the target would freely accept).");
            }

            foreach (var callerAllow in caller.Allow)
            {
                if (!target.Allow.Contains(callerAllow))
                {
                    throw new PermissionDeniedException(
                        "Creating window '" + label + "' would widen allow on '" + permission + "' (entry '" + Format(callerAllow) + "' is allowed by the caller but not by the target).");
                }
            }
        }
    }

    private static bool HasAnyEntries(PermissionScope scope) =>
        scope.Allow.Count > 0 || scope.Deny.Count > 0;

    private static string Format(PathScope scope) =>
        (scope.Base ?? "*") + ":" + (scope.Path ?? "*");
}
