# Third-Party Notices

## CefGlue.Next / CefGlue

- Repository: https://github.com/Deon-Berlin/CefGlue
- Pinned commit: `e3389315dad795374be1a1e52c42d4e49cb6fe7b`
- License: MIT
- Source is kept as a git submodule at `third_party/CefGlue`.
- Only the Avalonia control and common binding source needed by the adapter may be
  copied or linked into `Tarui.WebView.CefGlueNext`. Demo applications, build
  helpers, generated packages, and CEF redistribution binaries are excluded.

The upstream MIT license text is retained in `third_party/CefGlue/LICENSE`.

## Chromium Embedded Framework (CEF)

CEF is distributed under the BSD 3-Clause License. CEF runtime packages and
redistribution binaries are not vendored by this initialization. A future
runtime packaging change must retain the CEF notice and the corresponding
redistribution license files for every RID shipped by tarui.net.

```text
Copyright (c) 2008-2026 Marshall A. Greenblatt. Portions Copyright (c) 2006-2009 Google Inc. All rights reserved.

Redistribution and use in source and binary forms, with or without modification,
are permitted provided that the following conditions are met:

* Redistributions of source code must retain the above copyright notice, this
  list of conditions and the following disclaimer.
* Redistributions in binary form must reproduce the above copyright notice,
  this list of conditions and the following disclaimer in the documentation
  and/or other materials provided with the distribution.
* Neither the name of Google Inc. nor the name of the Chromium Embedded
  Framework nor the names of its contributors may be used to endorse or promote
  products derived from this software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```
