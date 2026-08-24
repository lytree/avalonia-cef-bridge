<#
.SYNOPSIS
    自动枚举并顺序执行仓库内所有 Tarui.*.Tests 控制台式自测试项目。

.DESCRIPTION
    - 发现 tests/<name>.Tests/<name>.Tests.csproj，按字母顺序逐个 dotnet run。
    - 每个项目目录可选放置 .requires-env.txt（每行一个 ENV 名称）；
      若任一 ENV 未设置，则视为该测试受宿主环境约束，计为 skipped。
    - 任一项目运行失败立即停止（$ErrorActionPreference = 'Stop'）。
    - 通过数 < -BaselineCount 时抛出错误，捕获"意外删除测试"的回归。

.PARAMETER TestsRoot
    测试根目录，默认 'tests'（相对仓库根）。

.PARAMETER Configuration
    dotnet run 配置，默认 'Release'。

.PARAMETER BaselineCount
    最少通过项目数，默认 21。低于该值即报错。

.EXAMPLE
    ./eng/test-all.ps1
    ./eng/test-all.ps1 -BaselineCount 21
    ./eng/test-all.ps1 -TestsRoot ../tests -Configuration Debug
#>
[CmdletBinding()]
param(
    [string]$TestsRoot = 'tests',
    [string]$Configuration = 'Release',
    [int]$BaselineCount = 21
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $TestsRoot)) {
    throw "TestsRoot '$TestsRoot' not found."
}

$projects = Get-ChildItem -LiteralPath $TestsRoot -Filter '*.Tests.csproj' -Recurse -File |
    Where-Object { $_.DirectoryName -like '*\*.Tests' } |
    Sort-Object FullName

$projects = @($projects)
$discovered = $projects.Count
Write-Host "Discovered $discovered self-test project(s) under '$TestsRoot'."

if ($discovered -eq 0) {
    throw "No self-test projects found under '$TestsRoot'."
}

$passed = New-Object System.Collections.Generic.List[string]
$skipped = New-Object System.Collections.Generic.List[object]
$failed = New-Object System.Collections.Generic.List[string]

foreach ($csproj in $projects) {
    $projectName = $csproj.BaseName
    $dir = $csproj.DirectoryName

    $requiresFile = Join-Path $dir '.requires-env.txt'
    if (Test-Path -LiteralPath $requiresFile) {
        $missing = @()
        foreach ($line in Get-Content -LiteralPath $requiresFile) {
            $name = $line.Trim()
            if ([string]::IsNullOrWhiteSpace($name) -or $name.StartsWith('#')) { continue }
            if (-not (Test-Path -Path ("Env:$name"))) { $missing += $name }
        }
        if ($missing.Count -gt 0) {
            Write-Host "[skip] $projectName (missing env: $($missing -join ', '))"
            $skipped.Add([pscustomobject]@{ Project = $projectName; Reason = "missing env: $($missing -join ', ')" })
            continue
        }
    }

    Write-Host "[run ] $projectName"
    & dotnet run --project $csproj.FullName -c $Configuration --no-build
    if ($LASTEXITCODE -ne 0) {
        Write-Host "[FAIL] $projectName (exit $LASTEXITCODE)" -ForegroundColor Red
        $failed.Add($projectName)
        throw "Self-test project '$projectName' failed with exit code $LASTEXITCODE."
    }
    Write-Host "[ ok ] $projectName"
    $passed.Add($projectName)
}

Write-Host ""
Write-Host "================ test-all summary ================"
Write-Host ("Discovered : {0}" -f $discovered)
Write-Host ("Skipped    : {0}" -f $skipped.Count)
foreach ($entry in $skipped) {
    Write-Host ("  - {0}  ({1})" -f $entry.Project, $entry.Reason)
}
Write-Host ("Passed     : {0}" -f $passed.Count)
Write-Host ("Failed     : {0}" -f $failed.Count)
foreach ($name in $failed) {
    Write-Host ("  - {0}" -f $name)
}
Write-Host "==================================================="

if ($passed.Count -lt $BaselineCount) {
    throw "Passed count $($passed.Count) is below baseline $BaselineCount. Refusing to continue."
}

if ($failed.Count -gt 0) {
    throw "Self-test failures: $($failed -join ', ')"
}

Write-Host "All self-test projects passed ($($passed.Count)/$BaselineCount)."