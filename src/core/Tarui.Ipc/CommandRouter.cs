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

    public CommandRouterBuilder Add<TArgs, TResult>(
        string command,
        JsonTypeInfo<TArgs> argsType,
        JsonTypeInfo<TResult> resultType,
        Func<TArgs, CommandContext, CancellationToken, ValueTask<TResult>> handler,
        string permission)
    {
        if (!_commands.TryAdd(command, new JsonCommandInvoker<TArgs, TResult>(argsType, resultType, handler)))
        {
            throw new InvalidOperationException($"Duplicate command '{command}'.");
        }

        _permissions.Add(command, permission);
        return this;
    }

    public CommandRouter Build() => new(
        _commands.ToFrozenDictionary(StringComparer.Ordinal),
        _permissions.ToFrozenDictionary(StringComparer.Ordinal));
}

public sealed class CommandRouter(
    FrozenDictionary<string, ICommandInvoker> commands,
    FrozenDictionary<string, string> permissions)
{
    public IReadOnlyCollection<string> Commands => commands.Keys;

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
    Func<TArgs, CommandContext, CancellationToken, ValueTask<TResult>> handler) : ICommandInvoker
{
    public async ValueTask<InvokeResponse> InvokeAsync(
        InvokeRequest request,
        CommandContext context,
        CancellationToken cancellationToken)
    {
        var args = request.Payload.Deserialize(argsType)
            ?? throw new InvalidPayloadException();
        var result = await handler(args, context, cancellationToken);
        var payload = JsonSerializer.SerializeToElement(result, resultType);
        return InvokeResponse.Ok(request.Id, payload);
    }
}
