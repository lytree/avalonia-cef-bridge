using System.Text.Json;
using Tarui.Contracts;

namespace Tarui.Hosting.Blazor;

/// <summary>
/// Blazor-side façade over Tarui's IPC dispatcher. Inject this into a Razor component to invoke
/// commands, subscribe to events and open streaming channels without going through the JS bridge.
/// All methods are async and never use runtime reflection — payloads are serialized through the
/// source-generated <see cref="TaruiJsonContext"/>.
/// </summary>
public interface ITaruiIpc
{
    /// <summary>Invokes a Tarui command and returns the typed response.</summary>
    /// <param name="command">The fully qualified command identifier, e.g. <c>core:window|get-state</c>.</param>
    /// <param name="payload">The strongly typed request payload (will be serialized via <see cref="TaruiJsonContext"/>).</param>
    /// <param name="cancellationToken">Cancellation token forwarded to the dispatcher.</param>
    ValueTask<TaruiIpcResult<T>> InvokeAsync<T>(string command, T payload, CancellationToken cancellationToken = default);

    /// <summary>Invokes a command that takes no payload.</summary>
    ValueTask<TaruiIpcResult<T>> InvokeAsync<T>(string command, CancellationToken cancellationToken = default);

    /// <summary>Invokes a command that produces no typed response.</summary>
    ValueTask<TaruiIpcResult<Unit>> InvokeAsync(string command, CancellationToken cancellationToken = default);

    /// <summary>Invokes a command with a raw <see cref="JsonElement"/> payload (escape hatch for generated metadata).</summary>
    ValueTask<TaruiIpcResult<T>> InvokeAsync<T>(string command, JsonElement payload, CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes to a routed Tarui event. The handler runs on the captured synchronization context so
    /// it is safe to mutate component state. The returned <see cref="ValueTask{IAsyncDisposable}"/>
    /// completes when the subscription is active; disposing it removes the subscription.
    /// </summary>
    ValueTask<IAsyncDisposable> ListenAsync<T>(string eventName, Func<T, CancellationToken, ValueTask> handler, CancellationToken cancellationToken = default);

    /// <summary>Strongly typed listener overload accepting a sync handler.</summary>
    ValueTask<IAsyncDisposable> ListenAsync<T>(string eventName, Action<T> handler, CancellationToken cancellationToken = default);
}

/// <summary>Wraps a Tarui <see cref="InvokeResponse"/> into a discriminated result for Blazor consumers.</summary>
public readonly record struct TaruiIpcResult<T>(bool Success, T? Value, IpcError? Error)
{
    /// <summary>Throws when the response is a failure; otherwise returns <see cref="Value"/>.</summary>
    public T EnsureSuccess()
    {
        if (!Success)
        {
            throw new TaruiIpcException(Error ?? new IpcError("UNKNOWN", "Tarui command failed without an error payload."));
        }

        return Value!;
    }
}

/// <summary>Raised when a Tarui invocation returns a failure payload.</summary>
public sealed class TaruiIpcException(IpcError error) : Exception($"{error.Code}: {error.Message}")
{
    public IpcError Error { get; } = error;
}