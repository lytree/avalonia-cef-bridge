namespace Tarui.Hosting.Blazor;

/// <summary>
/// Carries the user's Blazor root component type so <c>TaruiBlazorAppShell</c> can mount it via
/// <c>DynamicComponent</c> without needing an explicit endpoint parameter. The type is supplied by
/// the host via <see cref="TaruiBlazorOptions.RootComponent"/> at <c>AddTaruiBlazor</c> time.
/// </summary>
public sealed class TaruiBlazorRootComponent
{
    public Type Value { get; }

    public TaruiBlazorRootComponent(Type value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Value = value;
    }
}