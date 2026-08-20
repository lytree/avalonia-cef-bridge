using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tarui.Contracts;

namespace Tarui.Hosting;

public sealed class TaruiApplicationBuilder(string[]? args)
{
    private readonly HostApplicationBuilder _inner = new(new HostApplicationBuilderSettings
    {
        Args = args,
        ContentRootPath = AppContext.BaseDirectory,
    });

    private readonly string[] _args = args ?? [];

    public ConfigurationManager Configuration => _inner.Configuration;

    public IServiceCollection Services => _inner.Services;

    public ILoggingBuilder Logging => _inner.Logging;

    public TaruiWindowBuilder Window { get; } = new();

    public TaruiApplication Build()
    {
        _inner.Services.AddSingleton<TaruiLifetimeBridge>();
        _inner.Services.AddHostedService<HostShutdownWatcher>();
        var mainWindowOptions = MaterializeMainWindowOptions();
        _inner.Services.AddSingleton(mainWindowOptions);
        return new TaruiApplication(_inner.Build(), _args);
    }

    private WindowOptions MaterializeMainWindowOptions()
    {
        var title = ReadString("Tarui:Window:Title") ?? "tarui.net";
        var url = ReadString("Tarui:Window:Url");
        var width = ReadDouble("Tarui:Window:Width") ?? 1280;
        var height = ReadDouble("Tarui:Window:Height") ?? 820;
        var minWidth = ReadDouble("Tarui:Window:MinWidth") ?? 900;
        var minHeight = ReadDouble("Tarui:Window:MinHeight") ?? 600;
        var center = ReadBool("Tarui:Window:Center") ?? true;

        return new WindowOptions("main")
        {
            Title = Window.Title ?? title,
            Url = Window.Url ?? url,
            Width = Window.Width ?? width,
            Height = Window.Height ?? height,
            MinWidth = Window.MinWidth ?? minWidth,
            MinHeight = Window.MinHeight ?? minHeight,
            Center = Window.Center ?? center,
        };
    }

    private string? ReadString(string key)
    {
        var value = Configuration[key];
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private double? ReadDouble(string key)
    {
        var value = Configuration[key];
        if (value is null)
        {
            return null;
        }

        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            throw new InvalidOperationException(
                $"Configuration key '{key}' must be a double value, but '{value}' was found.");
        }

        return parsed;
    }

    private bool? ReadBool(string key)
    {
        var value = Configuration[key];
        if (value is null)
        {
            return null;
        }

        if (!bool.TryParse(value, out var parsed))
        {
            throw new InvalidOperationException(
                $"Configuration key '{key}' must be a boolean value, but '{value}' was found.");
        }

        return parsed;
    }
}
