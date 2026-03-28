#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Replicates the GitHub Actions release pipeline locally.

.PARAMETER Version
    The version string (e.g. "1.2.3"). Defaults to "0.0.0".

.PARAMETER Clean
    Remove previous build output before building.

.EXAMPLE
    .\scripts\build-release.ps1
    .\scripts\build-release.ps1 -Version "1.2.3"
    .\scripts\build-release.ps1 -Version "1.2.3" -Clean
#>
param(
    [string]$Version,
    [switch]$Clean
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if (-not $Version) {
    $Version = Read-Host "Enter version number (e.g. 1.2.3)"
    if (-not $Version) { Write-Host "Version is required." -ForegroundColor Red; exit 1 }
}

$RepoRoot        = Resolve-Path "$PSScriptRoot\.."
$PublishDir      = "$RepoRoot\publish\windows"
$OutputZip       = "$RepoRoot\Cockpit-windows-x64.zip"
$OutputInstaller = "$RepoRoot\Cockpit-windows-x64-Setup.exe"
$NsiScript       = "$RepoRoot\.github\installers\windows.nsi"
$MakeNsis        = "C:\Program Files (x86)\NSIS\makensis.exe"

function Step([string]$name, [scriptblock]$block) {
    Write-Host ""
    Write-Host "==> $name" -ForegroundColor Cyan
    & $block
    if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) {
        Write-Host "FAILED: $name (exit code $LASTEXITCODE)" -ForegroundColor Red
        exit $LASTEXITCODE
    }
}

# ── Clean ─────────────────────────────────────────────────────────────────────
if ($Clean) {
    Step "Clean previous output" {
        Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $PublishDir
        Remove-Item -Force -ErrorAction SilentlyContinue $OutputZip
        Remove-Item -Force -ErrorAction SilentlyContinue $OutputInstaller
    }
}

# ── Restore MAUI workloads ────────────────────────────────────────────────────
Step "Restore MAUI workloads" {
    dotnet workload restore "$RepoRoot\src\Cockpit\Cockpit.csproj"
}

# ── Publish ───────────────────────────────────────────────────────────────────
Step "Publish Windows app (version: $Version)" {
    dotnet publish "$RepoRoot\src\Cockpit\Cockpit.csproj" `
        --framework net10.0-windows10.0.19041.0 `
        --configuration Release `
        -p:ApplicationDisplayVersion=$Version `
        -p:ApplicationVersion=1 `
        --output $PublishDir
}

# ── Zip ───────────────────────────────────────────────────────────────────────
Step "Zip publish output -> Cockpit-windows-x64.zip" {
    if (Test-Path $OutputZip) { Remove-Item $OutputZip -Force }
    Compress-Archive -Path "$PublishDir\*" -DestinationPath $OutputZip
}

# ── Installer (NSIS) ──────────────────────────────────────────────────────────
Step "Build installer -> Cockpit-windows-x64-Setup.exe" {
    if (-not (Test-Path $MakeNsis)) {
        Write-Host "  NSIS not found. Install with: winget install NSIS.NSIS" -ForegroundColor Yellow
        Write-Host "  Skipping installer step." -ForegroundColor Yellow
        return
    }
    & $MakeNsis `
        /DAPP_VERSION="$Version" `
        /DSOURCE_PATH="$PublishDir" `
        /DOUTPUT_PATH="$OutputInstaller" `
        $NsiScript
}

# ── Summary ───────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "Build complete!" -ForegroundColor Green
Write-Host ""

$artifacts = @($OutputZip, $OutputInstaller) | Where-Object { Test-Path $_ }
foreach ($f in $artifacts) {
    $size = (Get-Item $f).Length / 1MB
    Write-Host ("  {0,-45} {1:F1} MB" -f (Resolve-Path $f -Relative), $size) -ForegroundColor White
}
