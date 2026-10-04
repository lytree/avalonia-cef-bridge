using Microsoft.Extensions.DependencyInjection;
using Tarui.Contracts;
using Tarui.Ipc;

namespace Tarui.Plugins.Positioner;

/// <summary>
/// The anchor slots a window can be pinned to. The eight screen anchors place the window inside the
/// current monitor's work area; the six <c>Tray*</c> anchors target the taskbar area and currently
/// degrade to their matching bottom-of-work-area slot (see <see cref="PositionerCalculator"/>).
/// </summary>
public enum PositionerAnchorKind
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
    TopCenter,
    BottomCenter,
    LeftCenter,
    RightCenter,
    TrayLeft,
    TrayRight,
    TrayCenter,
    TrayBottomLeft,
    TrayBottomRight,
    TrayBottomCenter,
}

/// <summary>Parses the wire-format anchor names used by <see cref="PositionerOptions.Anchor"/>.</summary>
public static class PositionerAnchors
{
    /// <summary>Whether <paramref name="value"/> names a supported anchor; never matches <c>null</c> or empty.</summary>
    public static bool TryParse(string? value, out PositionerAnchorKind anchor)
    {
        anchor = PositionerAnchorKind.TopLeft;
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        if (Enum.TryParse(value, ignoreCase: false, out anchor) && Enum.IsDefined(anchor))
        {
            return true;
        }

        anchor = PositionerAnchorKind.TopLeft;
        return false;
    }
}

/// <summary>
/// Positions a window inside a monitor work area (physical pixels). Tray anchors have no taskbar source
/// on this platform stack, so they degrade to the matching bottom-of-work-area slot: the window still
/// hugs the bottom edge users associate with the taskbar, but without taskbar-height compensation.
/// </summary>
public static class PositionerCalculator
{
    private const int ColumnLeft = 0;
    private const int ColumnCenter = 1;
    private const int ColumnRight = 2;

    private const int RowTop = 0;
    private const int RowMiddle = 1;
    private const int RowBottom = 2;

    /// <summary>
    /// Computes the window's top-left position in physical pixels for <paramref name="anchor"/> inside the
    /// given work area, then adds the margin offsets (<paramref name="x"/>, <paramref name="y"/>).
    /// </summary>
    public static (int X, int Y) Calculate(
        PositionerAnchorKind anchor,
        double workAreaX,
        double workAreaY,
        double workAreaWidth,
        double workAreaHeight,
        double windowWidth,
        double windowHeight,
        int x,
        int y)
    {
        // Tray 锚位回退：任务栏矩形不可用时退化为对应的工作区底部槽位。
        var (column, row) = anchor switch
        {
            PositionerAnchorKind.TopLeft => (ColumnLeft, RowTop),
            PositionerAnchorKind.TopCenter => (ColumnCenter, RowTop),
            PositionerAnchorKind.TopRight => (ColumnRight, RowTop),
            PositionerAnchorKind.LeftCenter => (ColumnLeft, RowMiddle),
            PositionerAnchorKind.RightCenter => (ColumnRight, RowMiddle),
            PositionerAnchorKind.BottomLeft => (ColumnLeft, RowBottom),
            PositionerAnchorKind.BottomCenter => (ColumnCenter, RowBottom),
            PositionerAnchorKind.BottomRight => (ColumnRight, RowBottom),
            PositionerAnchorKind.TrayLeft or PositionerAnchorKind.TrayBottomLeft => (ColumnLeft, RowBottom),
            PositionerAnchorKind.TrayCenter or PositionerAnchorKind.TrayBottomCenter => (ColumnCenter, RowBottom),
            PositionerAnchorKind.TrayRight or PositionerAnchorKind.TrayBottomRight => (ColumnRight, RowBottom),
            _ => (ColumnLeft, RowTop),
        };

        var slotX = column switch
        {
            ColumnCenter => workAreaX + ((workAreaWidth - windowWidth) / 2),
            ColumnRight => workAreaX + workAreaWidth - windowWidth,
            _ => workAreaX,
        };

        var slotY = row switch
        {
            RowMiddle => workAreaY + ((workAreaHeight - windowHeight) / 2),
            RowBottom => workAreaY + workAreaHeight - windowHeight,
            _ => workAreaY,
        };

        return ((int)Math.Round(slotX + x), (int)Math.Round(slotY + y));
    }
}

/// <summary>Moves a window to an anchored slot inside its monitor's work area.</summary>
public interface IPositionerService
{
    /// <summary>Applies the anchor to the window named by <see cref="PositionerOptions.Label"/>.</summary>
    ValueTask<Unit> SetPositionAsync(PositionerOptions options, CancellationToken cancellationToken);
}

/// <summary>
/// Registers the <c>plugin:positioner|set-position</c> command. When <see cref="PositionerOptions.Label"/> is
/// omitted the command falls back to the caller's own window label; an explicit label operates on that window.
/// The host must provide <see cref="IPositionerService"/> (the Avalonia shell registers one).
/// </summary>
public sealed class PositionerPlugin(IPositionerService service) : ITaruiPlugin
{
    public const string SetPositionCommand = "plugin:positioner|set-position";

    public void ConfigureCommands(CommandRouterBuilder commands)
    {
        commands.Add(
            SetPositionCommand,
            TaruiJsonContext.Default.PositionerOptions,
            TaruiJsonContext.Default.Unit,
            (options, context, ct) => service.SetPositionAsync(
                options.Label is null ? options with { Label = context.WindowLabel } : options,
                ct),
            SetPositionCommand);
    }
}

public static class PositionerPluginServiceCollectionExtensions
{
    public static IServiceCollection AddPositionerPlugin(this IServiceCollection services) =>
        services.AddPlugin<PositionerPlugin>();
}
