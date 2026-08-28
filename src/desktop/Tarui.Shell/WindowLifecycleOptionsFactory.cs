using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Tarui.Shell;

internal static class WindowLifecycleOptionsFactory
{
    public static WindowLifecycleOptions Build(IServiceProvider sp)
    {
        var configuration = sp.GetService<IConfiguration>();
        var resolved = WindowLifecycleOptions.DefaultCloseRequestTimeout;
        var configured = configuration?["Tarui:Window:CloseRequestTimeout"];
        if (!string.IsNullOrWhiteSpace(configured) &&
            double.TryParse(
                configured,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var seconds))
        {
            resolved = TimeSpan.FromSeconds(Math.Max(0, seconds));
        }

        return new WindowLifecycleOptions { CloseRequestTimeout = resolved };
    }
}
