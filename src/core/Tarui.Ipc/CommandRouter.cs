using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Tarui.Contracts;

namespace Tarui.Ipc;

public interface ICommandInvoker
{
    ValueTask<InvokeResponse> InvokeAsync(
        InvokeRequest request,
        CommandContext context,
        CancellationToken cancellationToken);
}

public sealed class CommandRouterBuilder
{
    private readonly Dictionary<string, ICommandInvoker> _commands = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _permissions = new(StringComparer.Ordinal);
    private readonly HashSet<string> _registeredPermissions = new(StringComparer.Ordinal);
    private readonly List<PermissionSetDefinition> _permissionSets = [];
    private readonly HashSet<string> _permissionSetIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> _defaultSetExclusions = new(StringComparer.Ordinal);

    public IReadOnlyCollection<string> RegisteredPermissions => _registeredPermissions;

    public IReadOnlyCollection<string> DefinedPermissionSets => _permissionSetIds;

    public CommandRouterBuilder Add<TArgs, TResult>(
        string command,
        JsonTypeInfo<TArgs> argsType,
        JsonTypeInfo<TResult> resultType,
        Func<TArgs, CommandContext, CancellationToken, ValueTask<TResult>> handler,
        string permission,
        Func<TArgs, IReadOnlyList<PathScope>, IReadOnlyList<PathScope>, bool>? scopeAuthorizer = null)
    {
        if (!_commands.TryAdd(command, new JsonCommandInvoker<TArgs, TResult>(argsType, resultType, handler, permission, scopeAuthorizer)))
        {
            throw new InvalidOperationException($"Duplicate command '{command}'.");
        }

        _permissions.Add(command, permission);
        _registeredPermissions.Add(permission);
        return this;
    }

    /// <summary>
    /// Registers a permission ID that has no dedicated command but may be referenced by capability
    /// files. Used for guard permissions such as the <c>-other-window</c> variants that authorize
    /// operating on a window other than the caller's own window.
    /// </summary>
    public CommandRouterBuilder AddPermission(string permission)
    {
        _registeredPermissions.Add(permission);
        return this;
    }

    /// <summary>
    /// Declares a curated permission set (Tauri ACL set) a capability manifest may reference as a
    /// single entry. Members may be other set identifiers; expansion happens while manifests load and
    /// rejects cycles. A curated set replaces the auto-materialized set of the same identifier.
    /// </summary>
    public CommandRouterBuilder DefinePermissionSet(
        string identifier,
        IReadOnlyList<string> permissions,
        string? description = null)
    {
        if (!_permissionSetIds.Add(identifier))
        {
            throw new InvalidOperationException($"Duplicate permission set '{identifier}'.");
        }

        _permissionSets.Add(new PermissionSetDefinition(identifier, permissions, description));
        return this;
    }

    /// <summary>
    /// Keeps <paramref name="permission"/> out of its namespace's materialized <c>|default</c> set. Used
    /// for permissions that must stay opt-in even though they map to an ordinary command — the DevTools
    /// protocol channel is the canonical case: granting <c>plugin:webview|default</c> must not hand a
    /// window the ability to drive CDP.
    /// </summary>
    public CommandRouterBuilder ExcludeFromDefaultSet(string permission)
    {
        _defaultSetExclusions.Add(permission);
        return this;
    }

    /// <summary>
    /// Materializes the permission-set catalog. Curated sets come first, then one set per namespace
    /// derived from the registered command permissions: <c>{namespace}|default</c> for the namespace's
    /// own permissions and <c>{namespace}|other-window</c> / <c>{namespace}|other-webview</c> for its
    /// cross-target guards. Splitting the guards keeps <c>|default</c> from silently authorizing
    /// operations on another window or web view. Application-defined commands (no <c>core:</c> /
    /// <c>plugin:</c> prefix) land in the <c>app</c> namespace, which is what gives every app an
    /// implicit <c>app|default</c> covering all of its own commands.
    /// </summary>
    public IReadOnlyList<PermissionSetDefinition> BuildPermissionSets()
    {
        var declarations = new List<PermissionSetDefinition>(_permissionSets);
        var declared = new HashSet<string>(_permissionSetIds, StringComparer.Ordinal);

        var materialized = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var permission in _registeredPermissions)
        {
            string setId;
            if (PermissionSetNames.IsCrossTargetGuard(permission))
            {
                setId = PermissionSetNames.CrossTargetSetOf(permission);
            }
            else if (_defaultSetExclusions.Contains(permission))
            {
                continue;
            }
            else
            {
                setId = PermissionSetNames.DefaultSetOf(permission);
            }

            if (!materialized.TryGetValue(setId, out var members))
            {
                members = new SortedSet<string>(StringComparer.Ordinal);
                materialized.Add(setId, members);
            }

            members.Add(permission);
        }

        foreach (var pair in materialized)
        {
            if (declared.Contains(pair.Key))
            {
                continue;
            }

            declarations.Add(new PermissionSetDefinition(pair.Key, [.. pair.Value]));
        }

        return declarations;
    }

    public CommandRouter Build() => new(
        _commands.ToFrozenDictionary(StringComparer.Ordinal),
        _permissions.ToFrozenDictionary(StringComparer.Ordinal),
        _registeredPermissions.ToFrozenSet(StringComparer.Ordinal));
}

public sealed class CommandRouter(
    FrozenDictionary<string, ICommandInvoker> commands,
    FrozenDictionary<string, string> permissions,
    FrozenSet<string> registeredPermissions)
{
    public IReadOnlyCollection<string> Commands => commands.Keys;

    public IReadOnlyCollection<string> RegisteredPermissions => registeredPermissions;

    public async ValueTask<InvokeResponse> InvokeAsync(
        InvokeRequest request,
        CommandContext context,
        CancellationToken cancellationToken = default)
    {
        if (!commands.TryGetValue(request.Command, out var invoker))
        {
            return InvokeResponse.Fail(request.Id, "COMMAND_NOT_FOUND", $"Command '{request.Command}' is not registered.");
        }

        if (!permissions.TryGetValue(request.Command, out var permission) || !context.Capabilities.Allows(permission))
        {
            return InvokeResponse.Fail(request.Id, "PERMISSION_DENIED", $"Command '{request.Command}' is not allowed.");
        }

        try
        {
            return await invoker.InvokeAsync(request, context, cancellationToken);
        }
        catch (InvalidPayloadException)
        {
            return InvokeResponse.Fail(request.Id, "INVALID_ARGUMENTS", "The command payload is invalid.");
        }
        catch (ScopeDeniedException)
        {
            return InvokeResponse.Fail(request.Id, "SCOPE_DENIED", $"Command '{request.Command}' is denied by its capability scope.");
        }
        catch (PermissionDeniedException)
        {
            return InvokeResponse.Fail(request.Id, "PERMISSION_DENIED", $"Command '{request.Command}' is not allowed.");
        }
        catch (CapabilityNotFoundException)
        {
            return InvokeResponse.Fail(request.Id, "CAPABILITY_NOT_FOUND", $"No capability profile is declared for the target window.");
        }
        catch (EventNamespaceDeniedException)
        {
            return InvokeResponse.Fail(request.Id, "EVENT_NOT_ALLOWED", $"The event name is reserved for native events.");
        }
        catch (PathAccessDeniedException exception)
        {
            return InvokeResponse.Fail(request.Id, "PATH_DENIED", $"The path is denied by the file access policy ({exception.Reason}).");
        }
        catch (OperationCanceledException)
        {
            return InvokeResponse.Fail(request.Id, "CANCELLED", "The command was cancelled.");
        }
        catch (NotSupportedException exception)
        {
            return InvokeResponse.Fail(request.Id, "NOT_SUPPORTED", exception.Message);
        }
        catch (TaruiRuntimeTypeMismatchException exception)
        {
            // A runtime-specific surface was requested from a runtime that does not implement it
            // (Tauri v3 RuntimeTypeMismatch). Distinct from NOT_SUPPORTED: the command exists, the
            // selected backend is simply the wrong one.
            return InvokeResponse.Fail(
                request.Id,
                TaruiRuntimeTypeMismatchException.ErrorCode,
                exception.Message);
        }
        catch (Exception exception)
        {
            return InvokeResponse.Fail(request.Id, "COMMAND_FAILED", exception.Message);
        }
    }
}

internal sealed class JsonCommandInvoker<TArgs, TResult>(
    JsonTypeInfo<TArgs> argsType,
    JsonTypeInfo<TResult> resultType,
    Func<TArgs, CommandContext, CancellationToken, ValueTask<TResult>> handler,
    string permission,
    Func<TArgs, IReadOnlyList<PathScope>, IReadOnlyList<PathScope>, bool>? scopeAuthorizer) : ICommandInvoker
{
    public async ValueTask<InvokeResponse> InvokeAsync(
        InvokeRequest request,
        CommandContext context,
        CancellationToken cancellationToken)
    {
        var args = request.Payload.Deserialize(argsType)
            ?? throw new InvalidPayloadException();

        if (scopeAuthorizer is not null && context.Capabilities.TryGetScope(permission, out var scope))
        {
            // The runtime overlay only extends scopes the capability manifest already declared:
            // deny entries always take effect and allow entries are appended, but a permission
            // without a declared scope never gains checks from the overlay (least surprise).
            var allow = scope.Allow;
            var deny = scope.Deny;
            if (context.ScopeOverlay is not null && context.ScopeOverlay.TryGetOverlay(permission, out var extraAllow, out var extraDeny))
            {
                allow = allow.Count == 0 ? extraAllow : [.. allow, .. extraAllow];
                deny = deny.Count == 0 ? extraDeny : [.. deny, .. extraDeny];
            }

            // deny wins over allow; the authorizer observes both lists and decides.
            if (!scopeAuthorizer(args, allow, deny))
            {
                throw new ScopeDeniedException(request.Command);
            }
        }

        var result = await handler(args, context, cancellationToken);
        var payload = JsonSerializer.SerializeToElement(result, resultType);
        return InvokeResponse.Ok(request.Id, payload);
    }
}
