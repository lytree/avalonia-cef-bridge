namespace Tarui.Hosting;

public static class TaruiHost
{
    public static TaruiApplicationBuilder CreateApplicationBuilder(string[]? args = null) => new(args);
}
