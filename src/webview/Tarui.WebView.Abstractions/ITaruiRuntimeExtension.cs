using System.Diagnostics.CodeAnalysis;
using Tarui.Contracts;

namespace Tarui.WebView.Abstractions;

/// <summary>
/// Marker for a runtime-specific surface. A web view runtime advertises the extras only it can offer
/// (CDP control, engine-specific settings, …) as extensions implementing this interface, so the
/// abstraction layer and the front end never see engine types. Mirrors Tauri v3's runtime extension
/// traits (<c>WebviewCefExt</c>, <c>AppHandleWryExt</c>): the interface is defined next to the runtime,
/// host code downcasts to it, and a runtime that does not implement it reports a type mismatch
/// instead of pretending to support it.
/// </summary>
public interface ITaruiRuntimeExtension
{
    /// <summary>The runtime that owns this extension, for example <c>cef</c>.</summary>
    string RuntimeName { get; }
}

/// <summary>Accessors that turn a failed extension probe into the stable runtime-mismatch error.</summary>
public static class TaruiRuntimeExtensionAccess
{
    /// <summary>
    /// Resolves <typeparamref name="TExtension"/> or throws
    /// <see cref="TaruiRuntimeTypeMismatchException"/> naming the selected runtime. Use it when the
    /// caller cannot proceed without the runtime-specific behaviour; use
    /// <see cref="ITaruiWebRuntime.TryGetExtension{TExtension}"/> when it is optional.
    /// </summary>
    public static TExtension GetRequiredExtension<TExtension>(this ITaruiWebRuntime runtime)
        where TExtension : class, ITaruiRuntimeExtension
    {
        ArgumentNullException.ThrowIfNull(runtime);

        return runtime.TryGetExtension<TExtension>(out var extension)
            ? extension
            : throw new TaruiRuntimeTypeMismatchException(runtime.Name, typeof(TExtension).Name);
    }
}
