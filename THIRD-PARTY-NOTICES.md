# Third-Party Notices

## CefGlue.Next / CefGlue

- Repository: https://github.com/Deon-Berlin/CefGlue
- Source baseline: commit `e3389315dad795374be1a1e52c42d4e49cb6fe7b`
- License: MIT
- Vendored source: `src/webview/cefglue`

The upstream MIT license is retained at `src/webview/cefglue/LICENSE`. The vendored copy contains local Avalonia 12 and reflection-removal changes documented in `src/webview/cefglue/README.md`.

## Chromium Embedded Framework

CEF `150.0.11` native distributions are downloaded from the official CEF automated build endpoint by `eng/cef/install-runtime.ps1`. The CEF BSD license is retained at `src/webview/cefglue/CEF-LICENSE.txt` and copied alongside installed runtime files. Native runtime binaries are not restored from NuGet.
