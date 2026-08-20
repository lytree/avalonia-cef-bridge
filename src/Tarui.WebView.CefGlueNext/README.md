# Tarui.WebView.CefGlueNext

This project is the only place allowed to know about CefGlue.Next/Avalonia implementation details. The public surface is `Tarui.WebView.Abstractions`.
`Tarui.Shell` references only the abstractions project; `Tarui.App` performs the explicit composition-root backend selection.

The current adapter is intentionally a compiling placeholder because the pinned upstream source targets Avalonia 11.3.14 while tarui.net targets Avalonia 12.1.1. The source is available under `third_party/CefGlue` and is wired through an explicit MSBuild switch so the port can be reviewed in this project without loading assemblies dynamically.

## Port switch

`EnableCefGlueNextSourcePort` is an explicit MSBuild switch. The default is `false`, so the solution remains buildable while the port is reviewed. When enabled, `CefGlueNextReviewedSourceFiles` must contain an explicit semicolon-separated list of reviewed source files. This project intentionally has no wildcard `Compile` include.

The first port should isolate only the required files from:

```text
third_party/CefGlue/CefGlue.Avalonia
third_party/CefGlue/CefGlue.Common
```

Those files need an Avalonia 12 compatibility pass and must remain in this adapter project or a clearly owned source-linked area. Upstream ObjectBinding and serializer files use reflection and must not be selected. Tarui IPC keeps its own static generated command and JSON binding chain. Do not add the upstream demo, build, tools, generated package, or CEF runtime redist directories to the tarui.net solution.
