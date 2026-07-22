#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Replicates the GitHub Actions release pipeline locally.

.PARAMETER Version
    The version string (e.g. "1.2.3").

.PARAMETER Clean
    Remove previous build output before building.

.EXAMPLE
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

$RepoRoot   = Resolve-Path "$PSScriptRoot\.."
$PublishDir = "$RepoRoot\publish\windows"
$OutputExe  = "$RepoRoot\Cockpit-windows-x64-Setup.exe"
$OutputZip  = "$RepoRoot\Cockpit-windows-x64.zip"

function Step([string]$name, [scriptblock]$block) {
    Write-Host ""
    Write-Host "==> $name" -ForegroundColor Cyan
    & $block
    if (-not $?) {
        $exitCode = if (Test-Path variable:global:LASTEXITCODE) { $global:LASTEXITCODE } else { 1 }
        Write-Host "FAILED: $name (exit code $exitCode)" -ForegroundColor Red
        exit $exitCode
    }
}

if ($Clean) {
    Step "Clean previous output" {
        Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $PublishDir
        Remove-Item -Force -ErrorAction SilentlyContinue $OutputExe
        Remove-Item -Force -ErrorAction SilentlyContinue $OutputZip
    }
}

Step "Restore MAUI workloads" {
    dotnet workload restore "$RepoRoot\src\Cockpit\Cockpit.csproj"
}

Step "Publish Windows app (version: $Version)" {
    # Avoid retaining bundled CLI content from an earlier publish.
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $PublishDir

    dotnet publish "$RepoRoot\src\Cockpit\Cockpit.csproj" `
        --framework net10.0-windows10.0.19041.0 `
        --configuration Release `
        -p:RuntimeIdentifierOverride=win-x64 `
        -p:ApplicationDisplayVersion=$Version `
        -p:ApplicationVersion=1 `
        --output $PublishDir
}

Step "Create single-executable release artifacts" {
    $publishedFiles = @(Get-ChildItem -Path $PublishDir -File -Recurse)
    if ($publishedFiles.Count -ne 1 -or $publishedFiles[0].Name -ne "Cockpit.exe") {
        throw "Expected a single self-contained Cockpit.exe, but found: $($publishedFiles.FullName -join ', ')"
    }

    Copy-Item $publishedFiles[0].FullName $OutputExe -Force
    if (Test-Path $OutputZip) { Remove-Item $OutputZip -Force }
    Compress-Archive -Path $OutputExe -DestinationPath $OutputZip
}

Write-Host ""
Write-Host "Build complete!" -ForegroundColor Green
Write-Host ""

$artifacts = @($OutputExe, $OutputZip) | Where-Object { Test-Path $_ }
foreach ($f in $artifacts) {
    $size = (Get-Item $f).Length / 1MB
    Write-Host ("  {0,-45} {1:F1} MB" -f (Resolve-Path $f -Relative), $size) -ForegroundColor White
}
