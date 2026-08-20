# Vendored CefGlue.Next source

This directory contains the managed source required by tarui.net, based on upstream commit `e3389315dad795374be1a1e52c42d4e49cb6fe7b` and CEF `150.0.11`. The source is part of the main repository and is not consumed through NuGet or a git submodule.

## Local changes

- Adapted the Avalonia control layer from Avalonia 11.3.17 to Avalonia 12.1.1.
- Replaced ReactiveUI/System.Reactive scheduling with Avalonia Dispatcher APIs.
- Removed reflection-based ObjectBinding, generic JavaScript evaluation, and custom deserialization.
- Added a fixed `window.invokeCSharpAction` CEF process-message bridge for Tarui IPC.
- Uses the main tarui.net executable as the CEF subprocess entry point.
- Supports windowed CEF rendering; OSR/drag-and-drop code is not compiled in this port.

The upstream MIT license is in `LICENSE`; the native CEF BSD license is in `CEF-LICENSE.txt`.
