using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tarui.Contracts;
using Tarui.Ipc;

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


    /// <summary>
    /// Registers the application's identity (used by single-instance endpoints, OS-scoped file
    /// paths and any other place that derives an OS identifier from the host). Replaces any
    /// previously-registered identity so an app can override the defaults during bootstrap.
    /// </summary>
    public TaruiApplicationBuilder UseApplicationIdentity(TaruiApplicationIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ApplicationIdentity = identity;

        // Surface the sanitized identifier through an environment variable so the CEF bootstrap,
        // which lives in a separate webview assembly and cannot reference Hosting directly,
        // can route its on-disk cache to the same per-application root used by every other
        // OS-scoped resource.
        Environment.SetEnvironmentVariable("TARUI_APP_ID", identity.SanitizedIdentifier);
        return this;
    }

    /// <summary>The application identity that the host should use for endpoints, paths and IPC.</summary>
    public TaruiApplicationIdentity ApplicationIdentity { get; private set; } = TaruiApplicationIdentity.Default;

    public TaruiApplication Build()
    {
        _inner.Services.AddSingleton<TaruiLifetimeBridge>();
        _inner.Services.AddHostedService<HostShutdownWatcher>();
        _inner.Services.AddSingleton<IAppShutdown, HostAppShutdown>();
        var shutdownMode = ReadShutdownMode() ?? AppShutdownMode.OnMainWindowClose;
        _inner.Services.AddSingleton<IAppShutdownCoordinator>(sp => new AppShutdownCoordinator(
            sp.GetRequiredService<IAppShutdown>(),
            shutdownMode));
        _inner.Services.AddSingleton(_ => ApplicationIdentity);
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
        var resizable = ReadBool("Tarui:Window:Resizable") ?? true;
        var decorations = ReadBool("Tarui:Window:Decorations") ?? true;
        var alwaysOnTop = ReadBool("Tarui:Window:AlwaysOnTop") ?? false;
        var visible = ReadBool("Tarui:Window:Visible") ?? true;

        return new WindowOptions("main")
        {
            Title = Window.Title ?? title,
            Url = Window.Url ?? url,
            Width = Window.Width ?? width,
            Height = Window.Height ?? height,
            MinWidth = Window.MinWidth ?? minWidth,
            MinHeight = Window.MinHeight ?? minHeight,
            MaxWidth = Window.MaxWidth ?? ReadDouble("Tarui:Window:MaxWidth"),
            MaxHeight = Window.MaxHeight ?? ReadDouble("Tarui:Window:MaxHeight"),
            X = Window.X ?? ReadDouble("Tarui:Window:X"),
            Y = Window.Y ?? ReadDouble("Tarui:Window:Y"),
            Center = Window.Center ?? center,
            Resizable = Window.Resizable ?? resizable,
            Decorations = Window.Decorations ?? decorations,
            AlwaysOnTop = Window.AlwaysOnTop ?? alwaysOnTop,
            Visible = Window.Visible ?? visible,
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

    private AppShutdownMode? ReadShutdownMode()
    {
        var value = Configuration["Tarui:Application:ShutdownMode"];
        if (value is null)
        {
            return null;
        }

        if (Enum.TryParse<AppShutdownMode>(value, ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        throw new InvalidOperationException(
            $"Configuration key 'Tarui:Application:ShutdownMode' must be one of "
            + $"{string.Join(", ", Enum.GetNames<AppShutdownMode>())}, but '{value}' was found.");
    }
}

