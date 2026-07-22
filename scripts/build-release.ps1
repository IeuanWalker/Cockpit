#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Builds the portable executable and NSIS installer locally.

.PARAMETER Version
    The version string (for example "1.2.3").

.PARAMETER Clean
    Removes previous publish output and release artifacts before building.

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

if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Version must contain three numeric components, for example 1.2.3."
}

$RepoRoot       = (Resolve-Path "$PSScriptRoot\..").Path
$ProjectPath    = "$RepoRoot\src\Cockpit\Cockpit.csproj"
$PublishDir     = "$RepoRoot\publish\windows"
$PortableDir    = "$RepoRoot\publish\portable"
$OutputPortable = "$RepoRoot\Cockpit-windows-x64-$Version-Portable.exe"
$OutputExe      = "$RepoRoot\Cockpit-windows-x64-$Version-Setup.exe"
$NsiScript      = "$RepoRoot\.github\installers\windows.nsi"
$MakeNsis       = "C:\Program Files (x86)\NSIS\makensis.exe"

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
        Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $PublishDir, $PortableDir
        Remove-Item -Force -ErrorAction SilentlyContinue $OutputPortable, $OutputExe
    }
}

Step "Restore MAUI workloads" {
    dotnet workload restore $ProjectPath
}

Step "Publish unpackaged Windows app (version: $Version)" {
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $PublishDir
    dotnet publish $ProjectPath `
        --framework net10.0-windows10.0.19041.0 `
        --configuration Release `
        -p:WindowsPackageType=None `
        -p:ApplicationDisplayVersion=$Version `
        -p:ApplicationVersion=1 `
        --output $PublishDir
}

Step "Build portable executable -> Cockpit-windows-x64-$Version-Portable.exe" {
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $PortableDir
    dotnet publish $ProjectPath `
        --framework net10.0-windows10.0.19041.0 `
        --configuration Release `
        --runtime win-x64 `
        --self-contained true `
        -p:WindowsPackageType=None `
        -p:WindowsAppSDKSelfContained=true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:IncludeAllContentForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:DebugType=None `
        -p:ApplicationDisplayVersion=$Version `
        -p:ApplicationVersion=1 `
        --output $PortableDir
    Copy-Item "$PortableDir\Cockpit.exe" $OutputPortable -Force
}

Step "Build NSIS installer -> Cockpit-windows-x64-$Version-Setup.exe" {
    if (-not (Test-Path $MakeNsis)) {
        throw "NSIS was not found. Install it with: winget install NSIS.NSIS"
    }
    & $MakeNsis `
        /DAPP_VERSION="$Version" `
        /DSOURCE_PATH="$PublishDir" `
        /DOUTPUT_PATH="$OutputExe" `
        $NsiScript
}

Write-Host ""
Write-Host "Build complete!" -ForegroundColor Green
Write-Host ""

$artifacts = @($OutputPortable, $OutputExe) | Where-Object { Test-Path $_ }
foreach ($file in $artifacts) {
    $size = (Get-Item $file).Length / 1MB
    Write-Host ("  {0,-55} {1:F1} MB" -f (Resolve-Path $file -Relative), $size)
}
