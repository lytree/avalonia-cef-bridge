# CefGlue.Next.Avalonia

`CefGlue.Next.Avalonia` is the standalone Avalonia component for the repository's CefGlue port. It owns the browser control, native CefGlue handlers, runtime bootstrap, subprocess dispatch, resource scheme hooks, drag/drop translation and browser close completion.

The package contains the managed CefGlue assemblies it builds against:

- `CefGlue.Next.Avalonia.dll`
- `Xilium.CefGlue.dll`
- `Xilium.CefGlue.Common.dll`
- `Xilium.CefGlue.Common.Shared.dll`
- `Xilium.CefGlue.BrowserProcess.Core.dll`
- `Xilium.CefGlue.Avalonia.dll`

Avalonia remains a normal NuGet dependency. The package nuspec must not expose any `Xilium.*` dependency; those assemblies are embedded as package lib assets. Native CEF files are application/runtime assets and are not included in this managed package.

## Direct Avalonia usage

```xml
<PackageReference Include="CefGlue.Next.Avalonia" Version="0.1.0" />
```

Dispatch CEF subprocess arguments before creating the host or Avalonia application:

```csharp
using System.IO;
using CefGlue.Next.Avalonia;

if (CefGlueNextAvaloniaRuntime.RunSubProcess(args))
{
    return;
}

try
{
    CefGlueNextAvaloniaRuntime.Initialize(new CefGlueNextAvaloniaRuntimeOptions
    {
        RuntimeDirectory = Path.Combine(AppContext.BaseDirectory, "CEF", "win-x64")
    });

    var webView = new CefGlueNextAvaloniaWebView(new Uri("https://example.com"));
    // Add webView to the Avalonia visual tree and await its close during window shutdown.
}
finally
{
    CefGlueNextAvaloniaRuntime.Shutdown();
}
```

The control can be inserted directly into an Avalonia visual tree. Navigation, download, external-navigation, file-drop, draggable-region and message events are exposed by the component. A host decides policy by handling the corresponding event arguments; the component does not depend on Tarui or `Process.Start`.

## Lifecycle

Use one runtime configuration per process. The expected order is:

```text
RunSubProcess(args)
  -> Initialize(options)
  -> create CefGlueNextAvaloniaWebView controls
  -> await webView.CloseAsync() for every control
  -> Avalonia loop exits
  -> Host StopAsync and Dispose complete
  -> Program finally calls CefGlueNextAvaloniaRuntime.Shutdown()
```

`CloseAsync` waits for the native browser close callback up to its shutdown timeout and records completion so later `CloseAsync` or `Dispose` calls are immediately idempotent. The host must not call runtime shutdown until all controls have completed their close operation and the Avalonia loop and Host have stopped and been disposed. Initialization failures do not poison a later initialization attempt, while shutdown is terminal for the current process.
