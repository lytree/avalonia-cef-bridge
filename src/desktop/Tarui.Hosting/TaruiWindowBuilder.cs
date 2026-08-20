namespace Tarui.Hosting;

public sealed class TaruiWindowBuilder
{
    public string? Title { get; set; }

    public double? Width { get; set; }

    public double? Height { get; set; }

    public double? MinWidth { get; set; }

    public double? MinHeight { get; set; }

    public bool? Center { get; set; }

    public string? Url { get; set; }

    public void Configure(Action<TaruiWindowBuilder> configure) => configure(this);
}
