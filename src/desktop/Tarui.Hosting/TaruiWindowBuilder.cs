namespace Tarui.Hosting;

/// <summary>
/// Fluent configuration for the main window. It mirrors the full <c>WindowOptions</c> surface that the
/// shell window exposes; values set here take precedence over <c>Tarui:Window:*</c> configuration.
/// </summary>
public sealed class TaruiWindowBuilder
{
    public string? Title { get; set; }

    public string? Url { get; set; }

    public double? Width { get; set; }

    public double? Height { get; set; }

    public double? MinWidth { get; set; }

    public double? MinHeight { get; set; }

    public double? MaxWidth { get; set; }

    public double? MaxHeight { get; set; }

    public double? X { get; set; }

    public double? Y { get; set; }

    public bool? Center { get; set; }

    public bool? Resizable { get; set; }

    public bool? Decorations { get; set; }

    public bool? AlwaysOnTop { get; set; }

    public bool? Visible { get; set; }

    public void Configure(Action<TaruiWindowBuilder> configure) => configure(this);
}