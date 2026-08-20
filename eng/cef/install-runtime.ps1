[CmdletBinding()]
param(
    [ValidateSet("win-x64", "win-arm64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64")]
    [string]$RuntimeIdentifier = "win-x64",
    [switch]$Force,
    [switch]$ValidateOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$cefBuild = "150.0.11+gb887805+chromium-150.0.7871.115"
$platforms = @{
    "win-x64" = "windows64"
    "win-arm64" = "windowsarm64"
    "linux-x64" = "linux64"
    "linux-arm64" = "linuxarm64"
    "osx-x64" = "macosx64"
    "osx-arm64" = "macosarm64"
}

function Invoke-CefWebRequest {
    param(
        [Parameter(Mandatory)]
        [string]$Uri,
        [ValidateSet("Get", "Head")]
        [string]$Method = "Get",
        [string]$OutFile
    )

    $parameters = @{
        Uri                = $Uri
        Method             = $Method
        MaximumRedirection = 5
        ErrorAction        = "Stop"
    }

    # Windows PowerShell needs this when Internet Explorer is not configured;
    # PowerShell 7 also exposes the parameter, so use it when available.
    $webRequestCommand = Get-Command Invoke-WebRequest -CommandType Cmdlet
    if ($webRequestCommand.Parameters.ContainsKey("UseBasicParsing")) {
        $parameters.UseBasicParsing = $true
    }

    if ($PSBoundParameters.ContainsKey("OutFile")) {
        $parameters.OutFile = $OutFile
    }

    return Invoke-WebRequest @parameters
}

function Get-ResponseText {
    param(
        [Parameter(Mandatory)]
        $Response
    )

    $content = $Response.Content
    if ($content -is [byte[]]) {
        return [Text.Encoding]::UTF8.GetString($content)
    }

    return [string]$content
}

function Get-ExpectedSha1 {
    param(
        [Parameter(Mandatory)]
        $Response,
        [Parameter(Mandatory)]
        [string]$ArchiveName
    )

    $text = (Get-ResponseText -Response $Response).Trim()
    if ([string]::IsNullOrWhiteSpace($text)) {
        throw "CEF SHA-1 sidecar is empty."
    }

    $tokens = $text -split "\s+"
    $expected = $tokens[0].ToLowerInvariant()
    if ($expected -notmatch "^[0-9a-f]{40}$") {
        throw "CEF SHA-1 sidecar does not contain a valid SHA-1 digest: '$text'."
    }

    if ($tokens.Count -ge 2) {
        $sidecarName = $tokens[1].TrimStart("*")
        if ([IO.Path]::GetFileName($sidecarName) -ne $ArchiveName) {
            throw "CEF SHA-1 sidecar names '$sidecarName' instead of '$ArchiveName'."
        }
    }

    return $expected
}

function Get-TarExecutable {
    $command = Get-Command tar -CommandType Application -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($null -eq $command) {
        throw "A system tar executable with bzip2 support is required to install CEF."
    }

    if (-not [string]::IsNullOrWhiteSpace($command.Source)) {
        return $command.Source
    }

    return $command.Path
}

function Invoke-Tar {
    param(
        [Parameter(Mandatory)]
        [string]$TarExecutable,
        [Parameter(Mandatory)]
        [string[]]$Arguments,
        [switch]$CaptureOutput
    )

    if ($CaptureOutput) {
        $output = & $TarExecutable @Arguments 2>&1
        $exitCode = $LASTEXITCODE
        if ($exitCode -ne 0) {
            throw "tar failed with exit code ${exitCode}: $($output -join [Environment]::NewLine)"
        }

        return @($output | ForEach-Object { [string]$_ })
    }

    & $TarExecutable @Arguments
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        throw "tar failed with exit code $exitCode."
    }
}

function Assert-SafeChildDirectory {
    param(
        [Parameter(Mandatory)]
        [string]$Root,
        [Parameter(Mandatory)]
        [string]$Child
    )

    $rootFullPath = [IO.Path]::GetFullPath($Root).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)
    $childFullPath = [IO.Path]::GetFullPath($Child)
    $rootPrefix = $rootFullPath + [IO.Path]::DirectorySeparatorChar

    if (-not $childFullPath.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "CEF path escapes its runtime root: $childFullPath"
    }

    if (Test-Path -LiteralPath $rootFullPath) {
        $rootItem = Get-Item -LiteralPath $rootFullPath -Force
        if (($rootItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "CEF runtime root must not be a symbolic link or reparse point: $rootFullPath"
        }
    }

    if (Test-Path -LiteralPath $childFullPath) {
        $childItem = Get-Item -LiteralPath $childFullPath -Force
        if (($childItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "CEF target must not be a symbolic link or reparse point: $childFullPath"
        }
    }
}

function Assert-SafeArchiveEntries {
    param(
        [Parameter(Mandatory)]
        [string[]]$Entries
    )

    $nonEmptyEntries = @($Entries | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($nonEmptyEntries.Count -eq 0) {
        throw "CEF archive contains no entries."
    }

    foreach ($entry in $nonEmptyEntries) {
        $name = $entry.Trim().Replace("\", "/")
        if ($name.StartsWith("/") -or $name -match "^[A-Za-z]:/") {
            throw "CEF archive contains an absolute path: $entry"
        }

        $segments = $name.Split("/", [StringSplitOptions]::RemoveEmptyEntries)
        if ($segments -contains "..") {
            throw "CEF archive contains a path traversal entry: $entry"
        }
    }
}

function Copy-DirectoryContents {
    param(
        [Parameter(Mandatory)]
        [string]$Source,
        [Parameter(Mandatory)]
        [string]$Destination
    )

    foreach ($entry in Get-ChildItem -LiteralPath $Source -Force) {
        $destinationPath = Join-Path $Destination $entry.Name
        Copy-Item -LiteralPath $entry.FullName -Destination $destinationPath -Recurse -Force
    }
}

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "../.."))
$runtimeRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot "runtime/cef"))
$targetRoot = [IO.Path]::GetFullPath((Join-Path $runtimeRoot $RuntimeIdentifier))
Assert-SafeChildDirectory -Root $repositoryRoot -Child $runtimeRoot
Assert-SafeChildDirectory -Root $runtimeRoot -Child $targetRoot

if (Test-Path -LiteralPath $targetRoot) {
    $targetItem = Get-Item -LiteralPath $targetRoot -Force
    if (-not $targetItem.PSIsContainer) {
        throw "CEF target exists but is not a directory: $targetRoot"
    }

    if (-not $Force -and -not $ValidateOnly) {
        Write-Host "CEF runtime already exists at $targetRoot. Use -Force to reinstall."
        return
    }
}

$platform = $platforms[$RuntimeIdentifier]
$archiveName = "cef_binary_$($cefBuild)_$($platform)_minimal.tar.bz2"
$encodedName = [Uri]::EscapeDataString($archiveName)
$baseUrl = "https://cef-builds.spotifycdn.com/$encodedName"
$shaUrl = "$baseUrl.sha1"

$headResponse = Invoke-CefWebRequest -Uri $baseUrl -Method Head
$shaResponse = Invoke-CefWebRequest -Uri $shaUrl -Method Get
$expectedSha = Get-ExpectedSha1 -Response $shaResponse -ArchiveName $archiveName
Write-Host "Validated CEF $cefBuild for $RuntimeIdentifier (SHA-1 $expectedSha)."

if ($ValidateOnly) {
    return
}

$tempRoot = Join-Path ([IO.Path]::GetTempPath()) "tarui-cef-$([Guid]::NewGuid().ToString('N'))"
$archivePath = Join-Path $tempRoot $archiveName
$extractRoot = Join-Path $tempRoot "extracted"
$stagingRoot = Join-Path $runtimeRoot ".tarui-cef-staging-$([Guid]::NewGuid().ToString('N'))"
$backupRoot = Join-Path $runtimeRoot ".tarui-cef-backup-$([Guid]::NewGuid().ToString('N'))"
$tarExecutable = Get-TarExecutable
$targetMovedToBackup = $false
$installationCompleted = $false

try {
    New-Item -ItemType Directory -Force -Path $tempRoot, $extractRoot, $runtimeRoot, $stagingRoot | Out-Null
    Assert-SafeChildDirectory -Root $runtimeRoot -Child $stagingRoot
    Assert-SafeChildDirectory -Root $runtimeRoot -Child $backupRoot

    Write-Host "Downloading $baseUrl"
    Invoke-CefWebRequest -Uri $baseUrl -Method Get -OutFile $archivePath | Out-Null
    if (-not (Test-Path -LiteralPath $archivePath)) {
        throw "CEF archive was not downloaded."
    }

    $archiveItem = Get-Item -LiteralPath $archivePath -Force
    if ($archiveItem.Length -le 0) {
        throw "CEF archive is empty."
    }

    $actualSha = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA1).Hash.ToLowerInvariant()
    if ($actualSha -ne $expectedSha) {
        throw "CEF archive checksum mismatch. Expected $expectedSha, got $actualSha."
    }

    $listArguments = @("-t", "-j", "-f", $archivePath)
    $entries = Invoke-Tar -TarExecutable $tarExecutable -Arguments $listArguments -CaptureOutput
    Assert-SafeArchiveEntries -Entries $entries

    # The official CEF archive should not contain links. Reject them before extraction
    # so an archive entry cannot redirect a later copy outside the temporary tree.
    $verboseEntries = Invoke-Tar -TarExecutable $tarExecutable -Arguments @("-t", "-j", "-v", "-f", $archivePath) -CaptureOutput
    foreach ($entry in $verboseEntries) {
        if (-not [string]::IsNullOrWhiteSpace($entry) -and $entry.TrimStart()[0] -in @("l", "h")) {
            throw "CEF archive contains a symbolic or hard link: $entry"
        }
    }

    Invoke-Tar -TarExecutable $tarExecutable -Arguments @("-x", "-j", "-f", $archivePath, "-C", $extractRoot)

    $distributionRoots = @(Get-ChildItem -LiteralPath $extractRoot -Directory -Force)
    if ($distributionRoots.Count -ne 1) {
        throw "CEF distribution root is ambiguous; expected exactly one top-level directory."
    }

    $distributionRoot = $distributionRoots[0].FullName
    $releaseRoot = Join-Path $distributionRoot "Release"
    $resourcesRoot = Join-Path $distributionRoot "Resources"
    foreach ($requiredRoot in @($releaseRoot, $resourcesRoot)) {
        if (-not (Test-Path -LiteralPath $requiredRoot -PathType Container)) {
            throw "CEF archive is missing the required directory: $requiredRoot"
        }
    }

    Copy-DirectoryContents -Source $releaseRoot -Destination $stagingRoot
    Copy-DirectoryContents -Source $resourcesRoot -Destination $stagingRoot

    $licenseSource = Join-Path $repositoryRoot "src/webview/cefglue/CEF-LICENSE.txt"
    if (-not (Test-Path -LiteralPath $licenseSource -PathType Leaf)) {
        throw "CEF license file is missing: $licenseSource"
    }
    Copy-Item -LiteralPath $licenseSource -Destination (Join-Path $stagingRoot "LICENSE.txt") -Force

    if (Test-Path -LiteralPath $targetRoot) {
        Move-Item -LiteralPath $targetRoot -Destination $backupRoot
        $targetMovedToBackup = $true
    }

    Move-Item -LiteralPath $stagingRoot -Destination $targetRoot
    $installationCompleted = $true
    Write-Host "CEF $cefBuild installed at $targetRoot"
}
catch {
    if ($targetMovedToBackup -and -not (Test-Path -LiteralPath $targetRoot) -and (Test-Path -LiteralPath $backupRoot)) {
        try {
            Move-Item -LiteralPath $backupRoot -Destination $targetRoot
        }
        catch {
            Write-Warning "Failed to restore the previous CEF runtime at ${targetRoot}: $($_.Exception.Message)"
        }
    }
    throw
}
finally {
    if ($installationCompleted -and (Test-Path -LiteralPath $backupRoot)) {
        try {
            Remove-Item -LiteralPath $backupRoot -Recurse -Force
        }
        catch {
            Write-Warning "Failed to remove the CEF backup directory ${backupRoot}: $($_.Exception.Message)"
        }
    }

    foreach ($cleanupPath in @($stagingRoot, $backupRoot, $tempRoot)) {
        if ($null -ne $cleanupPath -and (Test-Path -LiteralPath $cleanupPath)) {
            try {
                Remove-Item -LiteralPath $cleanupPath -Recurse -Force
            }
            catch {
                Write-Warning "Failed to clean temporary CEF path ${cleanupPath}: $($_.Exception.Message)"
            }
        }
    }
}