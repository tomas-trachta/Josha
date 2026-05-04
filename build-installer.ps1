#requires -Version 7.0
<#
.SYNOPSIS
    Publishes Josha self-contained, then compiles the Inno Setup installer.
.DESCRIPTION
    Runs publish.ps1 -> ./publish, then invokes Inno Setup's ISCC.exe to
    produce ./installer/Output/Josha-Setup-*.exe. Skips the publish step if
    -SkipPublish is supplied (useful when iterating on the .iss script).
.PARAMETER IsccPath
    Optional path to ISCC.exe. Auto-discovered from common install locations
    if omitted.
#>
[CmdletBinding()]
param(
    [switch] $SkipPublish,
    [string] $IsccPath
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path

if (-not $SkipPublish) {
    & (Join-Path $repoRoot 'publish.ps1')
    if ($LASTEXITCODE -ne 0) { throw "publish.ps1 failed ($LASTEXITCODE)" }
}

if (-not $IsccPath) {
    $candidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    )
    $IsccPath = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $IsccPath) {
        $cmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
        if ($cmd) { $IsccPath = $cmd.Source }
    }
}

if (-not $IsccPath -or -not (Test-Path $IsccPath)) {
    throw "ISCC.exe not found. Install Inno Setup 6.3+ from https://jrsoftware.org/isdl.php or pass -IsccPath."
}

$iss = Join-Path $repoRoot 'installer\Josha.iss'
Write-Host "Compiling installer: $iss" -ForegroundColor Cyan
& $IsccPath $iss
if ($LASTEXITCODE -ne 0) { throw "ISCC.exe failed ($LASTEXITCODE)" }

$outDir = Join-Path $repoRoot 'installer\Output'
Write-Host "Installer built in: $outDir" -ForegroundColor Green
Get-ChildItem $outDir -Filter *.exe | ForEach-Object { Write-Host "  $($_.Name) ($([math]::Round($_.Length / 1MB, 1)) MB)" }
