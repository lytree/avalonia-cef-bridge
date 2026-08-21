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
            PluginRegistersGetCurrentAndFeedCommands();
            LinuxDesktopEntryAdvertisesScheme();
            LinuxDesktopEntryQuotesExecAndUrlPlaceholder();
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