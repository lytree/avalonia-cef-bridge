using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Tarui.Contracts;
using Tarui.Ipc;
using Tarui.Plugins.DeepLink;
using Tarui.Shell;

namespace Tarui.DeepLink.Tests;

internal static class Program
{
    public static async Task<int> Main()
    {
        try
        {
            UriRejectsMalformed();
            UriExtractsRegisteredScheme();
            UriRejectsUnregisteredScheme();
            UriRejectsControlCharacters();
            UriRejectsOversizedUrl();
            SchemeValidationEnforcesTokenFormat();
            ConfigurationFiltersInvalidAndDedupes();
            await ColdStartSeedsFirstRegisteredUrlAsync();
            await ColdStartLeavesCurrentNullWithoutActivationAsync();
            await SecondActivationDeliversAndReportsAsync();
            await DeliverEmitsPerSchemeEventAsync();
            await DeliverIgnoresInvalidUrlAsync();
            await FeedAsyncReproducesValidationPathAsync();
            await DeliverDeduplicatesRepeatedUrlAsync();
            await DeliverKeepsCurrentUrlAfterDeduplicationAsync();
            PluginRegistersGetCurrentAndFeedCommands();
            LinuxDesktopEntryAdvertisesScheme();
            LinuxDesktopEntryQuotesExecAndUrlPlaceholder();
            await MacDeepLinkBridgeStartsAndStopsAsync();
            await MacDeepLinkBridgeNonMacIsNoOpAsync();
            MacDeepLinkBridgeFactorySelectsImplementation();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.ToString());
            return 1;
        }

        Console.WriteLine("Tarui.DeepLink self-tests passed.");
        return 0;
    }

    private static void UriRejectsMalformed()
    {
        var schemes = Schemes(["tarui"]);
        Assert(DeepLinkUri.TryExtractScheme(null, schemes) is null, "A null value must be rejected.");
        Assert(DeepLinkUri.TryExtractScheme(string.Empty, schemes) is null, "An empty value must be rejected.");
        Assert(DeepLinkUri.TryExtractScheme("tarui:open", schemes) is null, "A URL without :// must be rejected.");
        Assert(DeepLinkUri.TryExtractScheme("://slash", schemes) is null, "A missing scheme before :// must be rejected.");
    }

    private static void UriExtractsRegisteredScheme()
    {
        var schemes = Schemes(["tarui"]);
        var url = "tarui://open/doc?id=7&tab=main";
        Assert(DeepLinkUri.TryExtractScheme(url, schemes) == "tarui", "A registered scheme must be extracted.");
    }

    private static void UriRejectsUnregisteredScheme()
    {
        var schemes = Schemes(["tarui"]);
        Assert(DeepLinkUri.TryExtractScheme("market://home", schemes) is null, "An unregistered scheme must be rejected.");
    }

    private static void UriRejectsControlCharacters()
    {
        var schemes = Schemes(["tarui"]);
        Assert(DeepLinkUri.TryExtractScheme("tarui://open\r\n?x=1", schemes) is null,
            "Control characters that could poison logs must be rejected.");
    }

    private static void UriRejectsOversizedUrl()
    {
        var schemes = Schemes(["tarui"]);
        var oversized = "tarui://" + new string('a', DeepLinkUri.MaxLength);
        Assert(DeepLinkUri.TryExtractScheme(oversized, schemes) is null,
            "A URL over the length bound must be rejected.");
    }

    private static void SchemeValidationEnforcesTokenFormat()
    {
        Assert(DeepLinkUri.IsValidScheme("tarui"), "An alphabetic scheme must be valid.");
        Assert(DeepLinkUri.IsValidScheme("tar-ui.net"), "Alnum plus - / . / + separators must be valid.");
        Assert(!DeepLinkUri.IsValidScheme("1tarui"), "A scheme starting with a digit must be rejected.");
        Assert(!DeepLinkUri.IsValidScheme("tar ui"), "A scheme containing spaces must be rejected.");
        Assert(!DeepLinkUri.IsValidScheme(string.Empty), "An empty scheme must be rejected.");
    }

    private static void ConfigurationFiltersInvalidAndDedupes()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Tarui:Application:DeepLinkSchemes:0"] = "tarui",
                ["Tarui:Application:DeepLinkSchemes:1"] = "TARUI", // case-insensitive duplicate
                ["Tarui:Application:DeepLinkSchemes:2"] = "market",
                ["Tarui:Application:DeepLinkSchemes:3"] = "1bad",  // invalid token
                ["Tarui:Application:DeepLinkSchemes:4"] = "  ",    // whitespace
            })
            .Build();

        var schemes = DeepLinkConfiguration.ReadSchemes(configuration);

        Assert(schemes.Count == 2, $"Only valid unique schemes must survive, but got {schemes.Count}.");
        Assert(schemes.Contains("tarui", StringComparer.Ordinal), "tarui must be present.");
        Assert(schemes.Contains("market", StringComparer.Ordinal), "market must be present.");
    }

    private static async Task ColdStartSeedsFirstRegisteredUrlAsync()
    {
        var (router, _) = BuildRouter();
        var service = new DeepLinkService(
            ["--flag", "tarui://open/doc?id=1", "other"],
            Schemes(["tarui"]), router);

        var result = await service.GetCurrentAsync(default);
        Assert(result.Url == "tarui://open/doc?id=1", "The first registered-scheme startup URL must seed the current URL.");
    }

    private static async Task ColdStartLeavesCurrentNullWithoutActivationAsync()
    {
        var (router, _) = BuildRouter();
        var service = new DeepLinkService(
            ["--flag", "note.txt", "market://home"],
            Schemes(["tarui"]), router);

        var result = await service.GetCurrentAsync(default);
        Assert(result.Url is null, "Normal launches must report no active deep-link URL.");
    }

    private static async Task SecondActivationDeliversAndReportsAsync()
    {
        var (router, captured) = BuildRouter();
        var service = new DeepLinkService([], Schemes(["tarui"]), router);

        service.OnSecondActivation(new SecondInstanceArgs(
            ["--open", "tarui://open/doc?id=9"], "/tmp", Stamp()));

        var result = await service.GetCurrentAsync(default);
        Assert(result.Url == "tarui://open/doc?id=9", "A warm activation URL must become the current URL.");
        Assert(captured.Count == 1 && captured[0] == "tarui://open/doc?id=9",
            "A warm activation must emit the deeplink://tarui event carrying the URL.");
    }

    private static async Task DeliverEmitsPerSchemeEventAsync()
    {
        var (router, captured) = BuildRouter();
        var service = new DeepLinkService([], Schemes(["tarui", "market"]), router);

        service.Deliver("market://home?tab=favorites");

        Assert(captured.Count == 1 && captured[0] == "market://home?tab=favorites",
            "Deliver must route the URL to its own deeplink://<scheme> event.");
        Assert((await service.GetCurrentAsync(default)).Url == "market://home?tab=favorites",
            "A delivered URL must become the current URL.");
    }

    private static async Task DeliverIgnoresInvalidUrlAsync()
    {
        var (router, captured) = BuildRouter();
        var service = new DeepLinkService([], Schemes(["tarui"]), router);

        service.Deliver("market://home");

        Assert(captured.Count == 0, "An unregistered-scheme URL must never produce an event.");
        Assert((await service.GetCurrentAsync(default)).Url is null,
            "An invalid URL must not overwrite the current URL.");
    }

    private static async Task FeedAsyncReproducesValidationPathAsync()
    {
        var (router, captured) = BuildRouter();
        var service = new DeepLinkService([], Schemes(["tarui"]), router);

        await service.FeedAsync(new DeepLinkFeedOptions(Url: "tarui://feed#demo"), default);

        Assert(captured.Count == 1 && captured[0] == "tarui://feed#demo",
            "Feeding a valid URL must exercise the same validation and emit the scheme event.");
        Assert((await service.GetCurrentAsync(default)).Url == "tarui://feed#demo",
            "Feeding a valid URL must update the current URL.");
    }

    private static void PluginRegistersGetCurrentAndFeedCommands()
    {
        var builder = new CommandRouterBuilder();
        new DeepLinkPlugin(new NoopDeepLinkService()).ConfigureCommands(builder);
        var router = builder.Build();

        Assert(router.Commands.Contains("plugin:deep-link|get-current"), "get-current must be registered.");
        Assert(router.Commands.Contains("plugin:deep-link|feed"), "feed must be registered.");
        Assert(router.RegisteredPermissions.Count == 2, "Two deep-link permissions must be registered.");
    }

    private static async Task DeliverDeduplicatesRepeatedUrlAsync()
    {
        var (router, captured) = BuildRouter();
        var service = new DeepLinkService([], Schemes(["tarui"]), router);

        service.Deliver("tarui://open/doc?id=1");
        // Same URL delivered again inside the dedup window must not emit a second event.
        service.Deliver("tarui://open/doc?id=1");

        Assert(captured.Count == 1,
            $"Repeated URLs inside the dedup window must emit only one event; captured {captured.Count}.");
    }

    private static async Task DeliverKeepsCurrentUrlAfterDeduplicationAsync()
    {
        var (router, captured) = BuildRouter();
        var service = new DeepLinkService([], Schemes(["tarui"]), router);

        service.Deliver("tarui://open/doc?id=1");
        service.Deliver("tarui://open/doc?id=1");
        service.Deliver("tarui://open/doc?id=2");

        Assert(captured.Count == 2,
            $"A distinct URL must still emit after a dedup-suppressed duplicate; captured {captured.Count}.");
        var current = await service.GetCurrentAsync(default);
        Assert(current.Url == "tarui://open/doc?id=2",
            "The current URL must reflect the most recent valid delivery, even when dedup suppressed an event.");
    }

    private static async Task MacDeepLinkBridgeStartsAndStopsAsync()
    {
        if (!OperatingSystem.IsMacOS())
        {
            // The factory still produces a non-null bridge, but it is the no-op partial; skip the
            // fake extractor wiring when Cocoa bindings would not be exercised.
            return;
        }

        var (router, captured) = BuildRouter();
        var service = new DeepLinkService([], Schemes(["tarui"]), router);
        var extractor = new ScriptedExtractor("tarui://open/from-appleevent");

        var bridge = MacDeepLinkBridge.Factory(service, extractor);
        await bridge.StartAsync(default);
        // The bridge routes through IMacDeepLinkUrlExtractor → DeepLinkService.Deliver; on macOS
        // the AppleEvent manager is replaced by the bridge, but the fake extractor means no real
        // URL flows until a fake descriptor is processed. The StartAsync contract is "registered".
        Assert(extractor.LastCalledWith == IntPtr.Zero,
            "StartAsync must not synthesize events; the bridge waits for a real AppleEvent.");

        // Smoke-check: invoking the extractor through the bridge hand-off path produces an event.
        // We cannot construct a real NSAppleEventDescriptor outside Cocoa; the bridge registers
        // but we do not exercise its selector here because no fake descriptor is available.
        await bridge.StopAsync(default);
    }

    private static async Task MacDeepLinkBridgeNonMacIsNoOpAsync()
    {
        var (router, captured) = BuildRouter();
        var service = new DeepLinkService([], Schemes(["tarui"]), router);

        var bridge = MacDeepLinkBridge.Factory(service, new ScriptedExtractor("tarui://ignored"));
        await bridge.StartAsync(default);
        await bridge.StopAsync(default);

        Assert(captured.Count == 0,
            "The non-macOS bridge partial must never emit events; no AppleEvent manager is involved.");
    }

    private static void MacDeepLinkBridgeFactorySelectsImplementation()
    {
        var (router, _) = BuildRouter();
        var service = new DeepLinkService([], Schemes(["tarui"]), router);

        var bridge = MacDeepLinkBridge.Factory(service, new ScriptedExtractor(null));
        if (OperatingSystem.IsMacOS())
        {
            Assert(bridge.GetType().Name == "Macos",
                "On macOS the factory must select the Cocoa-backed partial implementation.");
        }
        else
        {
            Assert(bridge.GetType().Name == "NoOp",
                "Off macOS the factory must select the no-op partial implementation.");
        }
    }

    private sealed class ScriptedExtractor(string? url) : IMacDeepLinkUrlExtractor
    {
        public IntPtr LastCalledWith { get; private set; }

        public string? TryExtractUrl(IntPtr eventDescriptor)
        {
            LastCalledWith = eventDescriptor;
            return url;
        }
    }

    private static void LinuxDesktopEntryAdvertisesScheme()
    {
        var entry = LinuxDeepLinkRegistrar.BuildDesktopEntry("tarui", "/opt/tarui.net");

        Assert(entry.Contains("x-scheme-handler/tarui;", StringComparison.Ordinal),
            "The desktop entry must advertise x-scheme-handler for the scheme.");
        Assert(entry.Contains("[Desktop Entry]", StringComparison.Ordinal), "A [Desktop Entry] header is required.");
    }

    private static void LinuxDesktopEntryQuotesExecAndUrlPlaceholder()
    {
        var entry = LinuxDeepLinkRegistrar.BuildDesktopEntry("market", "/opt/tarui.net");

        Assert(entry.Contains("Exec=\"/opt/tarui.net\" %u", StringComparison.Ordinal),
            "The Exec line must quote the executable and forward the URL via %u.");
        Assert(!entry.Contains("%U", StringComparison.Ordinal), "A single URL placeholder (%u) is expected.");
    }

    private static (EventRouter Router, List<string> Captured) BuildRouter()
    {
        var captured = new List<string>();
        var registry = new WindowRegistry();
        var router = new EventRouter(registry, new EventHub());
        router.Subscribe<JsonElement>("deeplink://tarui", p => captured.Add(p.GetString()!));
        router.Subscribe<JsonElement>("deeplink://market", p => captured.Add(p.GetString()!));
        return (router, captured);
    }

    private static HashSet<string> Schemes(params string[] schemes) =>
        new(schemes, StringComparer.OrdinalIgnoreCase);

    private static string Stamp() => DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture);

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class NoopDeepLinkService : IDeepLinkService
    {
        public ValueTask<DeepLinkCurrentResult> GetCurrentAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(new DeepLinkCurrentResult(null));

        public ValueTask<Unit> FeedAsync(DeepLinkFeedOptions options, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new Unit());
    }
}