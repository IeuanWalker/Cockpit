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
    [string]$Version = "0.0.0",
    [switch]$Clean
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$RepoRoot           = Resolve-Path "$PSScriptRoot\.."
$PublishDir         = "$RepoRoot\publish\windows"
$OutputZip          = "$RepoRoot\Cockpit-windows-x64.zip"
$InstallerOutputDir = "$RepoRoot\installer-output"
$IsccPath           = "C:\Users\$env:USERNAME\AppData\Local\Programs\Inno Setup 6\iscc.exe"
$IssScript          = "$RepoRoot\.github\installers\windows.iss"

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
        Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $InstallerOutputDir
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

# ── Installer ─────────────────────────────────────────────────────────────────
Step "Build installer -> Cockpit-windows-x64-Setup.exe" {
    if (-not (Test-Path $IsccPath)) {
        Write-Host "  Inno Setup not found at: $IsccPath" -ForegroundColor Yellow
        Write-Host "  Install it with: winget install JRSoftware.InnoSetup" -ForegroundColor Yellow
        Write-Host "  Skipping installer step." -ForegroundColor Yellow
        return
    }
    & $IsccPath `
        /DAppVersion="$Version" `
        /DSourcePath="$PublishDir" `
        /DOutputDir="$InstallerOutputDir" `
        $IssScript
}

# ── Summary ───────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "Build complete!" -ForegroundColor Green
Write-Host ""

$artifacts = @($OutputZip, "$InstallerOutputDir\Cockpit-windows-x64-Setup.exe") | Where-Object { Test-Path $_ }
foreach ($f in $artifacts) {
    $size = (Get-Item $f).Length / 1MB
    Write-Host ("  {0,-45} {1:F1} MB" -f (Resolve-Path $f -Relative), $size) -ForegroundColor White
}
