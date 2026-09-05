using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Tarui.Contracts;
using Tarui.Ipc;
using Tarui.Plugins.Cookie;
using Tarui.WebView.Abstractions;

namespace Tarui.Cookie.Tests;

internal static class Program
{
    public static async Task<int> Main()
    {
        await ListsCookiesThroughManagerAsync();
        await SetsCookieThroughManagerAsync();
        await RemovesCookieThroughManagerAsync();
        await FlushDelegatesToManagerAsync();
        PluginRegistersFourCommands();
        DtoRoundTripsThroughJsonMetadata();
        await ReportsUnsupportedWhenNoManagerAsync();
        await DeniesCommandsOutsideCapabilityAsync();
        Console.WriteLine("Tarui.Cookie self-tests passed.");
        return 0;
    }

    private static async Task ListsCookiesThroughManagerAsync()
    {
        var manager = new FakeCookieManager
        {
            ListResult =
            [
                new Tarui.Contracts.Cookie("session", "abc", Domain: "example.com", Path: "/", HttpOnly: true),
            ],
        };
        var dispatcher = NewDispatcher(manager);
        var options = new CookieListOptions("https://example.com/", IncludeHttpOnly: false);

        var response = await Dispatch(dispatcher, CookiePlugin.ListCommand, options,
            TaruiJsonContext.Default.CookieListOptions, TaruiJsonContext.Default.InvokeResponse,
            CookieCapability(CookiePlugin.ListCommand));

        Assert(response is { Success: true }, "Listing cookies must succeed for an authorized window.");
        var result = response!.Payload!.Value.Deserialize(TaruiJsonContext.Default.CookieListResult);
        Assert(result!.Supported, "A host manager-backed list must be reported as supported.");
        Assert(result.Cookies.Length == 1 && result.Cookies[0].Name == "session",
            "The listed cookies must round-trip through the manager.");
        Assert(manager.LastList!.Url == options.Url, "The request must forward the URL to the manager.");
    }

    private static async Task SetsCookieThroughManagerAsync()
    {
        var manager = new FakeCookieManager { SetResult = true };
        var dispatcher = NewDispatcher(manager);
        var options = new CookieSetOptions("https://example.com/", new Tarui.Contracts.Cookie("theme", "dark", Secure: true));

        var response = await Dispatch(dispatcher, CookiePlugin.SetCommand, options,
            TaruiJsonContext.Default.CookieSetOptions, TaruiJsonContext.Default.InvokeResponse,
            CookieCapability(CookiePlugin.SetCommand));

        Assert(response is { Success: true }, "Setting a cookie must succeed for an authorized window.");
        var result = response!.Payload!.Value.Deserialize(TaruiJsonContext.Default.CookieSetResult);
        Assert(result!.Succeeded, "A successful manager set must report Succeeded.");
        Assert(manager.LastSet!.Cookie.Name == "theme", "The cookie value must reach the manager.");
        Assert(manager.LastSet.Url == options.Url, "The target URL must reach the manager.");
    }

    private static async Task RemovesCookieThroughManagerAsync()
    {
        var manager = new FakeCookieManager { RemoveResult = true };
        var dispatcher = NewDispatcher(manager);
        var options = new CookieDeleteOptions("https://example.com/", "session");

        var response = await Dispatch(dispatcher, CookiePlugin.RemoveCommand, options,
            TaruiJsonContext.Default.CookieDeleteOptions, TaruiJsonContext.Default.InvokeResponse,
            CookieCapability(CookiePlugin.RemoveCommand));

        Assert(response is { Success: true }, "Removing a cookie must succeed for an authorized window.");
        var result = response!.Payload!.Value.Deserialize(TaruiJsonContext.Default.CookieDeleteResult);
        Assert(result!.Succeeded, "A successful manager delete must report Succeeded.");
        Assert(manager.LastRemove!.Name == "session" && manager.LastRemove.Url == options.Url,
            "The delete target must reach the manager.");
    }

    private static async Task FlushDelegatesToManagerAsync()
    {
        var manager = new FakeCookieManager();
        var dispatcher = NewDispatcher(manager);

        var response = await Dispatch(dispatcher, CookiePlugin.FlushCommand, new Unit(),
            TaruiJsonContext.Default.Unit, TaruiJsonContext.Default.InvokeResponse,
            CookieCapability(CookiePlugin.FlushCommand));

        Assert(response is { Success: true }, "Flushing must succeed for an authorized window.");
        Assert(manager.FlushCount == 1, "Flush must be forwarded to the manager exactly once.");
    }

    private static void PluginRegistersFourCommands()
    {
        var builder = new CommandRouterBuilder();
        new CookiePlugin(new CookieService(NoopCookieManager.Instance)).ConfigureCommands(builder);
        var router = builder.Build();

        Assert(router.Commands.Count == 4, $"The cookie plugin must register 4 commands, got {router.Commands.Count}.");
        Assert(router.Commands.Contains(CookiePlugin.ListCommand), "The list command must be registered.");
        Assert(router.Commands.Contains(CookiePlugin.SetCommand), "The set command must be registered.");
        Assert(router.Commands.Contains(CookiePlugin.RemoveCommand), "The remove command must be registered.");
        Assert(router.Commands.Contains(CookiePlugin.FlushCommand), "The flush command must be registered.");
    }

    private static void DtoRoundTripsThroughJsonMetadata()
    {
        var cookie = new Tarui.Contracts.Cookie("sid", "v1", Domain: "example.com", Path: "/", Secure: true, HttpOnly: true,
            Expires: 1_700_000_000_000, SameSite: "lax");
        var json = JsonSerializer.Serialize(cookie, TaruiJsonContext.Default.Cookie);
        var back = JsonSerializer.Deserialize(json, TaruiJsonContext.Default.Cookie);
        Assert(back == cookie, "A Cookie must round-trip losslessly through the JSON metadata.");

        var listOptions = new CookieListOptions("https://example.com/");
        Assert(JsonSerializer.Deserialize(JsonSerializer.Serialize(listOptions, TaruiJsonContext.Default.CookieListOptions),
            TaruiJsonContext.Default.CookieListOptions) == listOptions,
            "CookieListOptions must round-trip.");

        var setOptions = new CookieSetOptions("https://example.com/", cookie);
        Assert(JsonSerializer.Deserialize(JsonSerializer.Serialize(setOptions, TaruiJsonContext.Default.CookieSetOptions),
            TaruiJsonContext.Default.CookieSetOptions) == setOptions,
            "CookieSetOptions must round-trip.");

        var deleteOptions = new CookieDeleteOptions("https://example.com/", "sid");
        Assert(JsonSerializer.Deserialize(JsonSerializer.Serialize(deleteOptions, TaruiJsonContext.Default.CookieDeleteOptions),
            TaruiJsonContext.Default.CookieDeleteOptions) == deleteOptions,
            "CookieDeleteOptions must round-trip.");
    }

    private static async Task ReportsUnsupportedWhenNoManagerAsync()
    {
        var dispatcher = NewDispatcher(NoopCookieManager.Instance);

        var response = await Dispatch(dispatcher, CookiePlugin.ListCommand,
            new CookieListOptions("https://example.com/"), TaruiJsonContext.Default.CookieListOptions,
            TaruiJsonContext.Default.InvokeResponse, CookieCapability(CookiePlugin.ListCommand));

        Assert(response is { Success: true }, "An unsupported host must still resolve the command successfully.");
        var result = response!.Payload!.Value.Deserialize(TaruiJsonContext.Default.CookieListResult);
        Assert(!result!.Supported, "A host with no cookie store must report Supported = false.");
        Assert(!string.IsNullOrEmpty(result.Error), "An unsupported host must explain why it is unsupported.");
    }

    private static async Task DeniesCommandsOutsideCapabilityAsync()
    {
        var dispatcher = NewDispatcher(NoopCookieManager.Instance);

        // No permissions at all -> every cookie command must be denied at the router.
        var response = await Dispatch(dispatcher, CookiePlugin.ListCommand,
            new CookieListOptions("https://example.com/"), TaruiJsonContext.Default.CookieListOptions,
            TaruiJsonContext.Default.InvokeResponse, CookieCapability());

        Assert(response is { Success: false, Error.Code: "PERMISSION_DENIED" },
            "A window without cookie permissions must be denied.");
    }

    // ---------- helpers ----------

    private static IpcDispatcher NewDispatcher(IWebViewCookieManager manager)
    {
        var builder = new CommandRouterBuilder();
        new CookiePlugin(new CookieService(manager)).ConfigureCommands(builder);
        return new IpcDispatcher(builder.Build());
    }

    private static async Task<InvokeResponse?> Dispatch<TArgs>(
        IpcDispatcher dispatcher,
        string command,
        TArgs arguments,
        JsonTypeInfo<TArgs> argsType,
        JsonTypeInfo<InvokeResponse> responseType,
        CapabilitySet caps)
        where TArgs : notnull
    {
        var request = new InvokeRequest(1, "cookie-" + DateTime.UtcNow.Ticks, command,
            JsonSerializer.SerializeToElement(arguments, argsType));
        var json = JsonSerializer.Serialize(request, TaruiJsonContext.Default.InvokeRequest);
        var responseText = await dispatcher.DispatchJsonAsync(json, new CommandContext("main", "main", caps));
        return JsonSerializer.Deserialize(responseText, responseType);
    }

    private static CapabilitySet CookieCapability(params string[] permissions) => new(permissions);

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    /// <summary>In-memory manager recording the last operation per command and returning canned results.</summary>
    private sealed class FakeCookieManager : IWebViewCookieManager
    {
        public CookieListOptions? LastList;
        public CookieSetOptions? LastSet;
        public CookieDeleteOptions? LastRemove;
        public int FlushCount;
        public Tarui.Contracts.Cookie[] ListResult { get; set; } = [];
        public bool SetResult { get; set; } = true;
        public bool RemoveResult { get; set; } = true;

        public ValueTask<CookieListResult> ListAsync(CookieListOptions options, CancellationToken cancellationToken)
        {
            LastList = options;
            return ValueTask.FromResult(new CookieListResult(true, ListResult));
        }

        public ValueTask<CookieSetResult> SetAsync(CookieSetOptions options, CancellationToken cancellationToken)
        {
            LastSet = options;
            return ValueTask.FromResult(new CookieSetResult(SetResult));
        }

        public ValueTask<CookieDeleteResult> RemoveAsync(CookieDeleteOptions options, CancellationToken cancellationToken)
        {
            LastRemove = options;
            return ValueTask.FromResult(new CookieDeleteResult(RemoveResult));
        }

        public ValueTask FlushAsync(CancellationToken cancellationToken)
        {
            FlushCount++;
            return ValueTask.CompletedTask;
        }
    }
}