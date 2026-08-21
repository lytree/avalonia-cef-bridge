using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Tarui.Contracts;
using Tarui.Ipc;
using Tarui.Shell;
using Tarui.SingleInstance;

namespace Tarui.SingleInstance.Tests;

internal static class Program
{
    private const string SecondInstanceEvent = "app://second-instance";

    public static async Task<int> Main(string[] args)
    {
        // Child-process probe for the real two-process activation test.
        if (TryRunChildProbe(args, out var probeExit))
        {
            return probeExit;
        }

        try
        {
            await QueuedActivationFlushesOnceWindowReadyAsync();
            DoesNotDeliverToUnauthorizedWindowAsync();
            await RealSecondProcessForwardsArgumentsToPrimaryAsync();
            CoordinatorStartsListenerAndAcceptsPayloadAsync();
            CoordinatorNotifiesSecondActivationSinksAsync();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.ToString());
            return 1;
        }

        Console.WriteLine("Tarui.SingleInstance self-tests passed.");
        return 0;
    }

    /// <summary>Runs the child-process probe when the process is spawned with <c>--si-probe</c>.</summary>
    private static bool TryRunChildProbe(string[] args, out int exitCode)
    {
        exitCode = 0;
        if (args.Length < 3 || args[0] != "--si-probe")
        {
            return false;
        }

        // args: --si-probe <applicationId> <channel> [forward...]
        var identity = new SingleInstanceIdentity(args[1], args[2]);
        using var handle = SingleInstanceGuard.Acquire(
            identity,
            args.Skip(3).ToArray(),
            Directory.GetCurrentDirectory());

        // The primary already holds the lock, so the probe must always be the secondary instance.
        exitCode = handle.Role == InstanceRole.Secondary ? 0 : 1;
        return true;
    }

    private static async Task QueuedActivationFlushesOnceWindowReadyAsync()
    {
        var (router, registry, sink, _) = BuildShell();
        var coordinator = new SingleInstanceCoordinator(Identity(), router, registry, []);
        coordinator.Start();
        try
        {
            // No window registered yet -> activation must be queued, not delivered.
            coordinator.Receive(new SecondInstanceArgs(["--open", "note.txt"], "/tmp", Stamp()));
            await Task.Delay(100);
            Assert(sink.Received.IsEmpty, "An activation must be queued while the main window is unregistered.");

            // Register the main window (authorized to receive) then flush.
            registry.AddMain(sink, new CapabilitySet([], [SecondInstanceEvent], []));
            coordinator.Flush();
            await Task.Delay(100);

            Assert(sink.Received.Count == 1, "Flush must deliver the queued activation.");
            var (eventName, payload) = sink.Received.First();
            Assert(eventName == SecondInstanceEvent, $"Queued activation must be delivered as '{SecondInstanceEvent}'.");
            var delivered = payload.Deserialize(TaruiJsonContext.Default.SecondInstanceArgs);
            Assert(delivered is { Arguments: ["--open", "note.txt"] },
                "Flush must preserve the second instance's forwarded arguments.");
        }
        finally
        {
            coordinator.Dispose();
        }
    }

    private static void DoesNotDeliverToUnauthorizedWindowAsync()
    {
        var (router, registry, sink, _) = BuildShell();
        // Main window exists but lacks the app://second-instance receive authorization.
        registry.AddMain(sink, new CapabilitySet([], [], []));

        var coordinator = new SingleInstanceCoordinator(Identity(), router, registry, []);
        coordinator.Start();
        try
        {
            coordinator.Receive(new SecondInstanceArgs(["launch"], "/tmp", Stamp()));
            Thread.Sleep(200);
            Assert(sink.Received.IsEmpty,
                "A second-instance event must not reach a window without receive authorization.");
        }
        finally
        {
            coordinator.Dispose();
        }
    }

    private static async Task RealSecondProcessForwardsArgumentsToPrimaryAsync()
    {
        var run = Guid.NewGuid().ToString("N");
        var identity = new SingleInstanceIdentity($"selftest-{run}", $"ch-{run}");

        // The primary acquires the instance lock and hosts the channel listener.
        using var primary = SingleInstanceGuard.Acquire(identity, [], null);
        Assert(primary.Role == InstanceRole.Primary, "The first process must become the primary instance.");

        var (router, registry, sink, _) = BuildShell();
        registry.AddMain(sink, new CapabilitySet([], [SecondInstanceEvent], []));
        var coordinator = new SingleInstanceCoordinator(identity, router, registry, []);
        coordinator.Start();
        try
        {
            var workingDirectory = Path.GetTempPath().TrimEnd('\\', '/');
            var startInfo = new ProcessStartInfo
            {
                FileName = Environment.ProcessPath!,
                WorkingDirectory = Path.GetTempPath(),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("--si-probe");
            startInfo.ArgumentList.Add(identity.ApplicationId);
            startInfo.ArgumentList.Add(identity.ChannelName);
            startInfo.ArgumentList.Add("--open");
            startInfo.ArgumentList.Add("docs/plan.md");

            using var child = Process.Start(startInfo)!;
            Assert(child.WaitForExit(10_000), "The second-instance probe must exit promptly.");
            Assert(child.ExitCode == 0,
                $"The second instance must forward and exit as Secondary (exit {child.ExitCode}).\n" +
                $"{child.StandardOutput.ReadToEnd()}\n{child.StandardError.ReadToEnd()}");

            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (sink.Received.IsEmpty && DateTime.UtcNow < deadline)
            {
                await Task.Delay(25);
            }

            Assert(sink.Received.Count == 1, "The primary must receive the forwarded activation.");
            var (_, payload) = sink.Received.First();
            var delivered = payload.Deserialize(TaruiJsonContext.Default.SecondInstanceArgs);
            Assert(delivered is { Arguments: ["--open", "docs/plan.md"] },
                "The primary must receive the second instance's forwarded arguments.");
            Assert(string.Equals(
                    delivered!.WorkingDirectory.TrimEnd('\\', '/'),
                    workingDirectory,
                    StringComparison.OrdinalIgnoreCase),
                "The primary must receive the second instance's working directory.");
        }
        finally
        {
            coordinator.Dispose();
        }
    }

    private static void CoordinatorStartsListenerAndAcceptsPayloadAsync()
    {
        // Smoke check that Start()/Dispose() round-trips without throwing and the listener thread
        // is torn down, leaving the identity reusable.
        var (router, registry, sink, _) = BuildShell();
        registry.AddMain(sink, new CapabilitySet([], [SecondInstanceEvent], []));
        var coordinator = new SingleInstanceCoordinator(Identity(), router, registry, []);
        coordinator.Start();
        coordinator.Dispose();
        Assert(sink.Received.IsEmpty, "Disposing the coordinator must not fabricate deliveries.");
    }

    private static void CoordinatorNotifiesSecondActivationSinksAsync()
    {
        var (router, registry, _, _) = BuildShell();
        var probe = new RecordingActivationSink();
        var coordinator = new SingleInstanceCoordinator(Identity(), router, registry, [probe]);
        coordinator.Start();
        try
        {
            var forwarded = new SecondInstanceArgs(["tarui://open/doc?id=1"], "/tmp", Stamp());
            coordinator.Receive(forwarded);
            Assert(probe.Args.Count == 1, "A forwarded activation must be reported to registered sinks.");
            Assert(probe.Args[0].Arguments is ["tarui://open/doc?id=1"],
                "The sink must receive the forwarded arguments verbatim.");
        }
        finally
        {
            coordinator.Dispose();
        }
    }

    private static (EventRouter Router, FakeWindowSinkRegistry Registry, RecordingSink Sink, RecordingActivationSink ActivationProbe) BuildShell()
    {
        var registry = new FakeWindowSinkRegistry();
        var router = new EventRouter(registry, new EventHub());
        var sink = new RecordingSink();
        return (router, registry, sink, new RecordingActivationSink());
    }

    private static SingleInstanceIdentity Identity() =>
        new($"selftest-{Guid.NewGuid():N}", $"ch-{Guid.NewGuid():N}");

    private static string Stamp() => DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture);

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class RecordingSink : IEventSink
    {
        public ConcurrentQueue<(string Event, JsonElement Payload)> Received { get; } = new();

        public ValueTask SendEventAsync(string eventName, JsonElement payload, CancellationToken cancellationToken)
        {
            Received.Enqueue((eventName, payload));
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingActivationSink : ISecondActivationSink
    {
        public List<SecondInstanceArgs> Args { get; } = [];

        public void OnSecondActivation(SecondInstanceArgs args) => Args.Add(args);
    }

    private sealed class FakeWindowSinkRegistry : IWindowSinkRegistry
    {
        private readonly Dictionary<string, (IEventSink Sink, CapabilitySet Capabilities)> _windows = new(StringComparer.Ordinal);
        private readonly object _gate = new();

        public IReadOnlyCollection<string> Labels
        {
            get
            {
                lock (_gate)
                {
                    return _windows.Keys.ToArray();
                }
            }
        }

        public void AddMain(IEventSink sink, CapabilitySet capabilities)
        {
            lock (_gate)
            {
                _windows["main"] = (sink, capabilities);
            }
        }

        public bool TryGetSink(string label, out IEventSink sink)
        {
            lock (_gate)
            {
                if (_windows.TryGetValue(label, out var entry))
                {
                    sink = entry.Sink;
                    return true;
                }
            }

            sink = null!;
            return false;
        }

        public bool TryGetCapabilities(string label, out CapabilitySet capabilities)
        {
            lock (_gate)
            {
                if (_windows.TryGetValue(label, out var entry))
                {
                    capabilities = entry.Capabilities;
                    return true;
                }
            }

            capabilities = null!;
            return false;
        }
    }
}