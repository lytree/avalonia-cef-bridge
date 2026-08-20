# CEF runtime

This directory contains native CEF runtime files for the supported target
runtime identifiers. The installer downloads CEF `150.0.11` minimal archives
from the official CEF automated-builds endpoint. It does not use NuGet and does
not commit the large archives to the repository.

Supported runtime identifiers:

| Runtime identifier | CEF automated-builds platform |
| --- | --- |
| `win-x64` | `windows64` |
| `win-arm64` | `windowsarm64` |
| `linux-x64` | `linux64` |
| `linux-arm64` | `linuxarm64` |
| `osx-x64` | `macosx64` |
| `osx-arm64` | `macosarm64` |

## Install

The installer requires PowerShell and a system `tar` executable with bzip2
support. PowerShell 7 is recommended on all platforms.

```powershell
./eng/cef/install-runtime.ps1 -RuntimeIdentifier win-x64
```

The archive is first checked with `HEAD`, then its official `.sha1` sidecar is
validated. The downloaded archive is hashed before it is extracted. Extraction
is performed into a temporary directory after archive paths and links are
checked. `Release` and `Resources` are merged into
`runtime/cef/<runtime-identifier>`.

To validate the URL, platform mapping, and sidecar without downloading the
archive:

```powershell
./eng/cef/install-runtime.ps1 -RuntimeIdentifier win-x64 -ValidateOnly
```

If the target already exists, the installer exits without contacting the
network. Use `-Force` to reinstall it. A forced install is staged beside the
target and replaces the previous directory only after download, checksum,
archive, and copy checks have succeeded; failures restore the previous target
and clean temporary files.

The resulting runtime directory is generated output. Keep it out of normal
source changes unless a release process explicitly requires bundling it.
