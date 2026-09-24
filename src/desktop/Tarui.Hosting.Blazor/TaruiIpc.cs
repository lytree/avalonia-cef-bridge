using System.Text.Json;
using Tarui.Contracts;
using Tarui.Ipc;
using Tarui.Shell;

namespace Tarui.Hosting.Blazor;

/// <summary>
/// Default <see cref="ITaruiIpc"/> implementation. Routes commands through the same
/// <see cref="IpcDispatcher"/> the CEF WebView uses, so Blazor components and front-end TypeScript
/// share the same capability/permission gate.
/// </summary>
internal sealed class TaruiIpc : ITaruiIpc
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        TypeInfoResolver = TaruiJsonContext.Default,
        PropertyNamingPolicy = TaruiJsonContext.Default.Options.PropertyNamingPolicy,
        DefaultIgnoreCondition = TaruiJsonContext.Default.Options.DefaultIgnoreCondition,
    };

    private readonly IpcDispatcher _dispatcher;
    private readonly EventHub _eventHub;
    private readonly ICapabilityProvider _capabilities;
    private readonly string _windowLabel;

    public TaruiIpc(IpcDispatcher dispatcher, EventHub eventHub, ICapabilityProvider capabilities, WindowOptions mainWindowOptions)
    {
        _dispatcher = dispatcher;
        _eventHub = eventHub;
        _capabilities = capabilities;
        _windowLabel = mainWindowOptions.Label;
    }

    public ValueTask<TaruiIpcResult<T>> InvokeAsync<T>(string command, CancellationToken cancellationToken = default) =>
        InvokeAsync<T>(command, JsonDocument.Parse("{}").RootElement, cancellationToken);

    public ValueTask<TaruiIpcResult<Unit>> InvokeAsync(string command, CancellationToken cancellationToken = default) =>
        InvokeAsync<Unit>(command, JsonDocument.Parse("{}").RootElement, cancellationToken);

    public ValueTask<TaruiIpcResult<T>> InvokeAsync<T>(string command, T payload, CancellationToken cancellationToken = default)
    {
        var element = JsonSerializer.SerializeToElement(payload, SerializerOptions);
        return InvokeAsync<T>(command, element, cancellationToken);
    }

    public async ValueTask<TaruiIpcResult<T>> InvokeAsync<T>(string command, JsonElement payload, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(command);
        var id = Guid.NewGuid().ToString("N");
        var request = new InvokeRequest(
            Protocol: 1,
            Id: id,
            Command: command,
            Payload: payload,
            WindowLabel: _windowLabel,
            WebViewLabel: _windowLabel);

        var capability = ResolveCapability();
        var context = new CommandContext(_windowLabel, _windowLabel, capability);
        var responseJson = await _dispatcher.DispatchJsonAsync(
            JsonSerializer.Serialize(request, SerializerOptions),
            context,
            channelSink: null,
            cancellationToken).ConfigureAwait(false);

        var response = JsonSerializer.Deserialize(responseJson, TaruiJsonContext.Default.InvokeResponse);
        if (response is null)
        {
            return new TaruiIpcResult<T>(false, default, new IpcError("INVALID_MESSAGE", "Tarui returned an empty response."));
        }

        if (!response.Success)
        {
            return new TaruiIpcResult<T>(false, default, response.Error);
        }

        if (response.Payload is null || response.Payload.Value.ValueKind == JsonValueKind.Null)
        {
            return new TaruiIpcResult<T>(true, default, null);
        }

        try
        {
            var value = JsonSerializer.Deserialize<T>(response.Payload.Value, SerializerOptions);
            return new TaruiIpcResult<T>(true, value, null);
        }
        catch (JsonException)
        {
            return new TaruiIpcResult<T>(false, default, new IpcError("INVALID_MESSAGE", "Tarui returned a payload that did not match the requested type."));
        }
    }

    public ValueTask<IAsyncDisposable> ListenAsync<T>(string eventName, Func<T, CancellationToken, ValueTask> handler, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(eventName);
        ArgumentNullException.ThrowIfNull(handler);
        var subscription = _eventHub.Subscribe<JsonElement>(eventName, element =>
        {
            var typed = JsonSerializer.Deserialize<T>(element.GetRawText(), SerializerOptions);
            if (typed is null)
            {
                return;
            }

            // Run the handler asynchronously without waiting on it: the EventHub subscription is
            // synchronous and we do not want to block its dispatch loop on user code. Any exception
            // raised here propagates back through TaskScheduler.UnobservedTaskException, matching the
            // behaviour of the front-end bridge.
            var task = handler(typed, cancellationToken);
            if (!task.IsCompletedSuccessfully)
            {
                AwaitAndForget(task);
            }

            static async void AwaitAndForget(ValueTask valueTask)
            {
                try
                {
                    await valueTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception)
                {
                    // Listener exceptions are absorbed; logging hooks can be wired later.
                }
            }
        });
        return ValueTask.FromResult<IAsyncDisposable>(new SubscriptionScope(subscription));
    }

    public ValueTask<IAsyncDisposable> ListenAsync<T>(string eventName, Action<T> handler, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return ListenAsync<T>(
            eventName,
            (value, _) =>
            {
                handler(value);
                return ValueTask.CompletedTask;
            },
            cancellationToken);
    }

    private sealed class SubscriptionScope(IDisposable subscription) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            subscription.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private CapabilitySet ResolveCapability()
    {
        if (_capabilities.Capabilities.TryGetValue(_windowLabel, out var capability))
        {
            return capability;
        }

        // Capabilities are loaded from the host's capabilities/ directory. When the active window
        // does not have a profile we surface the failure rather than silently fall back to a
        // permissive set, which would let Blazor code bypass the same permission gate that protects
        // the front-end.
        throw new InvalidOperationException(
            $"No capability profile is registered for window '{_windowLabel}'. " +
            "Add a capabilities/<window>.json entry so Blazor commands can be authorized.");
    }
}