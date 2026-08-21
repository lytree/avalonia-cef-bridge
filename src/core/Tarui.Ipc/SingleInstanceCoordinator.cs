using Tarui.Contracts;

namespace Tarui.Ipc;

/// <summary>
/// Routes second-instance activations to the running primary instance's webviews. The primary
/// process owns its instance port; every new launch either acquires the port (becomes primary) or
/// forwards its command-line arguments to the primary over a per-user communication endpoint before
/// exiting. Forwarded payloads are delivered as <c>app://second-instance</c> events only to windows
/// that declared receive authorization for them.
/// </summary>
public interface ISingleInstanceCoordinator
{
    /// <summary>
    /// Delivers any second-instance payloads that arrived before a receiver window became available.
    /// The shell calls this once the main window is registered so startup-time activations are not
    /// lost. Payloads are dropped (oldest first) when the bounded queue is full.
    /// </summary>
    void Flush();

    /// <summary>Stops the instance listener and releases the instance port.</summary>
    void Dispose();
}

/// <summary>A no-op coordinator used when the composition root does not opt into single instance.</summary>
public sealed class NoopSingleInstanceCoordinator : ISingleInstanceCoordinator
{
    public void Flush()
    {
    }

    public void Dispose()
    {
    }
}

/// <summary>
/// Observes every second-instance activation received by the primary coordinator. Native features
/// that are expressed through command-line arguments (e.g. deep-link URLs) subscribe to this so they
/// can interpret forwarded argument payloads without coupling to the forwarding transport itself.
/// Sinks resolve through DI as singletons; the coordinator captures and stores them at construction,
/// so they are materialized once, before the listener starts.
/// </summary>
public interface ISecondActivationSink
{
    /// <summary>
    /// Invoked for every forwarded activation, whether delivered immediately or replayed from the
    /// bounded queue during <see cref="ISingleInstanceCoordinator.Flush"/>. Implementations must not
    /// throw; the coordinator treats notification as best-effort.
    /// </summary>
    void OnSecondActivation(SecondInstanceArgs args);
}