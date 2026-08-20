# Third-Party Source

`CefGlue` is a git submodule pinned to:

```text
e3389315dad795374be1a1e52c42d4e49cb6fe7b
```

The submodule is intentionally kept separate from the main solution. It is the
source of the future Avalonia 12 port; it is not added as a project reference.

The upstream `CefGlue.Avalonia` project targets the Avalonia 11.3.14 API and
.NET 10 in this pinned source. `Tarui.WebView.CefGlueNext` therefore exposes a
small adapter boundary and currently compiles a placeholder implementation.
The port must use an explicit, reviewed file list. Do not use wildcard source
inclusion. In particular, upstream ObjectBinding and serializer files use
reflection and must not be compiled into tarui.net. Tarui IPC continues to use
its own static source-generated command and JSON binding chain. The port should
link or copy only reviewed control/common source files, with the upstream
license retained, and should not include demo projects, build scripts,
generated packages, or CEF runtime redists.

To update the submodule deliberately:

```powershell
git -C third_party/CefGlue fetch origin
git -C third_party/CefGlue checkout <reviewed-commit>
git add .gitmodules third_party/CefGlue
```

Any commit change must be reviewed against the upstream license, CEF version,
Avalonia API assumptions, and runtime redistribution requirements.
