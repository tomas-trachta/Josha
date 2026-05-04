#requires -Version 7.0
<#
.SYNOPSIS
    Publishes Josha as a self-contained win-x64 build into ./publish/.
.PARAMETER Configuration
    Build configuration. Defaults to Release.
.PARAMETER Runtime
    Target runtime identifier. Defaults to win-x64.
.PARAMETER OutputDir
    Output directory (relative to repo root). Defaults to ./publish.
#>
[CmdletBinding()]
param(
    [string] $Configuration = 'Release',
    [string] $Runtime = 'win-x64',
    [string] $OutputDir = 'publish'
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$absOutput = Join-Path $repoRoot $OutputDir

if (Test-Path $absOutput) {
    Remove-Item -Recurse -Force $absOutput
}

Write-Host "Publishing Josha ($Configuration / $Runtime, self-contained) -> $absOutput" -ForegroundColor Cyan

& dotnet publish (Join-Path $repoRoot 'Josha.csproj') `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishReadyToRun=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $absOutput

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

Write-Host "Publish complete." -ForegroundColor Green
