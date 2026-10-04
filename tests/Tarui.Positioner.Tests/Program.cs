using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Tarui.Contracts;
using Tarui.Ipc;
using Tarui.Plugins.Positioner;

namespace Tarui.Positioner.Tests;

internal static class Program
{
    public static async Task<int> Main()
    {
        RegistersSetPositionCommand();
        await FallsBackToCallerWindowLabelAsync();
        await DeniesSetPositionOutsideCapabilityAsync();
        CalculatorComputesScreenAnchors();
        CalculatorAppliesMarginOffsets();
        CalculatorDegradesTrayAnchorsToBottomSlots();
        AnchorParsingIsStrict();
        Console.WriteLine("Tarui.Positioner self-tests passed.");
        return 0;
    }

    private static void RegistersSetPositionCommand()
    {
        var builder = new CommandRouterBuilder();
        new PositionerPlugin(new FakePositionerService()).ConfigureCommands(builder);
        var router = builder.Build();

        Assert(router.Commands.Count == 1, $"The positioner plugin must register 1 command, got {router.Commands.Count}.");
        Assert(router.Commands.Contains(PositionerPlugin.SetPositionCommand), "The set-position command must be registered.");
        Assert(router.RegisteredPermissions.Count == 1, "The set-position command must register exactly one permission.");
    }

    private static async Task FallsBackToCallerWindowLabelAsync()
    {
        var service = new FakePositionerService();
        var dispatcher = NewDispatcher(service);

        var response = await Dispatch(dispatcher, new PositionerOptions("TopRight"),
            PositionerCapability(), windowLabel: "main");

        Assert(response is { Success: true }, "An authorized set-position must succeed.");
        var call = AssertSingle(service);
        Assert(call.Anchor == "TopRight", "The anchor must be forwarded verbatim.");
        Assert(call.Label == "main", "An omitted label must fall back to the caller's window label.");

        await Dispatch(dispatcher, new PositionerOptions("BottomLeft", Label: "editor", X: 8, Y: 4),
            PositionerCapability(), windowLabel: "main");
        Assert(service.Calls[1].Label == "editor", "An explicit label must win over the caller's window label.");
        Assert(service.Calls[1].X == 8 && service.Calls[1].Y == 4, "The margin offsets must be forwarded.");
    }

    private static async Task DeniesSetPositionOutsideCapabilityAsync()
    {
        var service = new FakePositionerService();
        var dispatcher = NewDispatcher(service);

        var response = await Dispatch(dispatcher, new PositionerOptions("TopLeft"),
            new CapabilitySet([]), windowLabel: "main");

        Assert(response is { Success: false, Error.Code: "PERMISSION_DENIED" },
            "A window without positioner permissions must be denied.");
        Assert(service.Calls.Count == 0, "A denied command must never reach the service.");
    }

    private static void CalculatorComputesScreenAnchors()
    {
        // 工作区 (100, 50, 800, 600)，窗口 200x100，零边距。
        const double waX = 100;
        const double waY = 50;
        const double waW = 800;
        const double waH = 600;
        const double ww = 200;
        const double wh = 100;

        Assert(PositionerCalculator.Calculate(PositionerAnchorKind.TopLeft, waX, waY, waW, waH, ww, wh, 0, 0) == (100, 50),
            "TopLeft must pin the window to the work area's top-left corner.");
        Assert(PositionerCalculator.Calculate(PositionerAnchorKind.TopCenter, waX, waY, waW, waH, ww, wh, 0, 0) == (400, 50),
            "TopCenter must center the window horizontally along the top edge.");
        Assert(PositionerCalculator.Calculate(PositionerAnchorKind.TopRight, waX, waY, waW, waH, ww, wh, 0, 0) == (700, 50),
            "TopRight must pin the window to the work area's top-right corner.");
        Assert(PositionerCalculator.Calculate(PositionerAnchorKind.LeftCenter, waX, waY, waW, waH, ww, wh, 0, 0) == (100, 300),
            "LeftCenter must center the window vertically along the left edge.");
        Assert(PositionerCalculator.Calculate(PositionerAnchorKind.RightCenter, waX, waY, waW, waH, ww, wh, 0, 0) == (700, 300),
            "RightCenter must center the window vertically along the right edge.");
        Assert(PositionerCalculator.Calculate(PositionerAnchorKind.BottomLeft, waX, waY, waW, waH, ww, wh, 0, 0) == (100, 550),
            "BottomLeft must pin the window to the work area's bottom-left corner.");
        Assert(PositionerCalculator.Calculate(PositionerAnchorKind.BottomCenter, waX, waY, waW, waH, ww, wh, 0, 0) == (400, 550),
            "BottomCenter must center the window horizontally along the bottom edge.");
        Assert(PositionerCalculator.Calculate(PositionerAnchorKind.BottomRight, waX, waY, waW, waH, ww, wh, 0, 0) == (700, 550),
            "BottomRight must pin the window to the work area's bottom-right corner.");
    }

    private static void CalculatorAppliesMarginOffsets()
    {
        var anchored = PositionerCalculator.Calculate(
            PositionerAnchorKind.TopLeft, 100, 50, 800, 600, 200, 100, 10, 5);
        Assert(anchored == (110, 55), "Margin offsets must shift the anchored slot in physical pixels.");
    }

    private static void CalculatorDegradesTrayAnchorsToBottomSlots()
    {
        var bottomLeft = PositionerCalculator.Calculate(PositionerAnchorKind.BottomLeft, 0, 0, 1000, 800, 200, 100, 0, 0);
        var bottomCenter = PositionerCalculator.Calculate(PositionerAnchorKind.BottomCenter, 0, 0, 1000, 800, 200, 100, 0, 0);
        var bottomRight = PositionerCalculator.Calculate(PositionerAnchorKind.BottomRight, 0, 0, 1000, 800, 200, 100, 0, 0);

        Assert(PositionerCalculator.Calculate(PositionerAnchorKind.TrayLeft, 0, 0, 1000, 800, 200, 100, 0, 0) == bottomLeft,
            "TrayLeft must degrade to the bottom-left slot while no taskbar rect is available.");
        Assert(PositionerCalculator.Calculate(PositionerAnchorKind.TrayCenter, 0, 0, 1000, 800, 200, 100, 0, 0) == bottomCenter,
            "TrayCenter must degrade to the bottom-center slot while no taskbar rect is available.");
        Assert(PositionerCalculator.Calculate(PositionerAnchorKind.TrayRight, 0, 0, 1000, 800, 200, 100, 0, 0) == bottomRight,
            "TrayRight must degrade to the bottom-right slot while no taskbar rect is available.");
        Assert(PositionerCalculator.Calculate(PositionerAnchorKind.TrayBottomLeft, 0, 0, 1000, 800, 200, 100, 0, 0) == bottomLeft,
            "TrayBottomLeft must degrade to the bottom-left slot.");
        Assert(PositionerCalculator.Calculate(PositionerAnchorKind.TrayBottomCenter, 0, 0, 1000, 800, 200, 100, 0, 0) == bottomCenter,
            "TrayBottomCenter must degrade to the bottom-center slot.");
        Assert(PositionerCalculator.Calculate(PositionerAnchorKind.TrayBottomRight, 0, 0, 1000, 800, 200, 100, 0, 0) == bottomRight,
            "TrayBottomRight must degrade to the bottom-right slot.");
    }

    private static void AnchorParsingIsStrict()
    {
        Assert(PositionerAnchors.TryParse("TopLeft", out var parsed) && parsed == PositionerAnchorKind.TopLeft,
            "A canonical anchor name must parse.");
        Assert(PositionerAnchors.TryParse("TrayBottomCenter", out parsed) && parsed == PositionerAnchorKind.TrayBottomCenter,
            "Every tray anchor name must parse.");
        Assert(!PositionerAnchors.TryParse("topleft", out _),
            "Anchor parsing must be case-sensitive so typos surface as command failures.");
        Assert(!PositionerAnchors.TryParse("Middle", out _), "An unknown anchor must not parse.");
        Assert(!PositionerAnchors.TryParse(string.Empty, out _), "An empty anchor must not parse.");
        Assert(!PositionerAnchors.TryParse(null, out _), "A null anchor must not parse.");
    }

    // ---------- helpers ----------

    private static IpcDispatcher NewDispatcher(IPositionerService service)
    {
        var builder = new CommandRouterBuilder();
        new PositionerPlugin(service).ConfigureCommands(builder);
        return new IpcDispatcher(builder.Build());
    }

    private static async Task<InvokeResponse?> Dispatch(
        IpcDispatcher dispatcher,
        PositionerOptions options,
        CapabilitySet caps,
        string windowLabel)
    {
        var request = new InvokeRequest(1, "pos-" + DateTime.UtcNow.Ticks, PositionerPlugin.SetPositionCommand,
            JsonSerializer.SerializeToElement(options, TaruiJsonContext.Default.PositionerOptions),
            WindowLabel: windowLabel);
        var json = JsonSerializer.Serialize(request, TaruiJsonContext.Default.InvokeRequest);
        var responseText = await dispatcher.DispatchJsonAsync(json, new CommandContext(windowLabel, "main", caps));
        return JsonSerializer.Deserialize(responseText, TaruiJsonContext.Default.InvokeResponse);
    }

    private static PositionerOptions AssertSingle(FakePositionerService service)
    {
        Assert(service.Calls.Count == 1, $"The service must receive exactly one call, got {service.Calls.Count}.");
        return service.Calls[0];
    }

    private static CapabilitySet PositionerCapability() => new([PositionerPlugin.SetPositionCommand]);

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    /// <summary>In-memory service recording every position request without moving anything.</summary>
    private sealed class FakePositionerService : IPositionerService
    {
        public List<PositionerOptions> Calls { get; } = [];

        public ValueTask<Unit> SetPositionAsync(PositionerOptions options, CancellationToken cancellationToken)
        {
            Calls.Add(options);
            return ValueTask.FromResult(new Unit());
        }
    }
}
