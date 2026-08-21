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

    public IReadOnlyCollection<string> RegisteredPermissions => _registeredPermissions;

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
            // deny wins over allow; the authorizer observes both lists and decides.
            if (!scopeAuthorizer(args, scope.Allow, scope.Deny))
            {
                throw new ScopeDeniedException(request.Command);
            }
        }

        var result = await handler(args, context, cancellationToken);
        var payload = JsonSerializer.SerializeToElement(result, resultType);
        return InvokeResponse.Ok(request.Id, payload);
    }
}
