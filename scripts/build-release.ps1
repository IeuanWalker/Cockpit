#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Builds the Windows ZIP, legacy NSIS installer, and signed MSIX installer locally.

.PARAMETER Version
    The version string (for example "1.2.3").

.PARAMETER PackageCertificateKeyFile
    Path to a PFX whose subject matches the publisher in Package.appxmanifest.

.PARAMETER PackageCertificatePassword
    Password for PackageCertificateKeyFile.

.PARAMETER SkipMsix
    Skip MSIX generation when only the legacy artifacts are required.

.EXAMPLE
    .\scripts\build-release.ps1 -Version "1.2.3" -PackageCertificateKeyFile ".\Cockpit.pfx" -PackageCertificatePassword "password" -Clean

.EXAMPLE
    .\scripts\build-release.ps1 -Version "1.2.3" -SkipMsix
#>
param(
    [string]$Version,
    [switch]$Clean,
    [string]$PackageCertificateKeyFile,
    [string]$PackageCertificatePassword,
    [switch]$SkipMsix
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

if (-not $SkipMsix) {
    if (-not $PackageCertificateKeyFile) {
        throw "PackageCertificateKeyFile is required for a signed MSIX. Pass -SkipMsix to build only the legacy artifacts."
    }
    $PackageCertificateKeyFile = (Resolve-Path $PackageCertificateKeyFile).Path
}

$RepoRoot       = (Resolve-Path "$PSScriptRoot\..").Path
$ProjectPath    = "$RepoRoot\src\Cockpit\Cockpit.csproj"
$PublishDir     = "$RepoRoot\publish\windows"
$MsixBuildDir   = "$RepoRoot\publish\msix"
$OutputZip      = "$RepoRoot\Cockpit-windows-x64.zip"
$OutputExe      = "$RepoRoot\Cockpit-windows-x64-Setup.exe"
$OutputMsix     = "$RepoRoot\Cockpit-windows-x64-$Version-Installer.msix"
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
        Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $PublishDir, $MsixBuildDir
        Remove-Item -Force -ErrorAction SilentlyContinue $OutputZip, $OutputExe, $OutputMsix
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

Step "Create ZIP -> Cockpit-windows-x64.zip" {
    if (Test-Path $OutputZip) { Remove-Item $OutputZip -Force }
    Compress-Archive -Path "$PublishDir\*" -DestinationPath $OutputZip
}

Step "Build legacy NSIS installer -> Cockpit-windows-x64-Setup.exe" {
    if (-not (Test-Path $MakeNsis)) {
        throw "NSIS was not found. Install it with: winget install NSIS.NSIS"
    }
    & $MakeNsis `
        /DAPP_VERSION="$Version" `
        /DSOURCE_PATH="$PublishDir" `
        /DOUTPUT_PATH="$OutputExe" `
        $NsiScript
}

if (-not $SkipMsix) {
    Step "Build signed MSIX -> Cockpit-windows-x64-$Version-Installer.msix" {
        Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $MsixBuildDir
        dotnet publish $ProjectPath `
            --framework net10.0-windows10.0.19041.0 `
            --configuration Release `
            -p:Platform=x64 `
            -p:RuntimeIdentifierOverride=win-x64 `
            -p:WindowsPackageType=MSIX `
            -p:GenerateAppxPackageOnBuild=true `
            -p:AppxPackageSigningEnabled=true `
            -p:AppxBundle=Never `
            -p:UapAppxPackageBuildMode=SideloadOnly `
            -p:PackageCertificateKeyFile="$PackageCertificateKeyFile" `
            -p:PackageCertificatePassword="$PackageCertificatePassword" `
            -p:ApplicationDisplayVersion=$Version `
            -p:ApplicationVersion=1 `
            -p:AppxPackageDir="$MsixBuildDir\"

        $packages = @(Get-ChildItem -Path $MsixBuildDir -Filter *.msix -File -Recurse)
        if ($packages.Count -ne 1) {
            throw "Expected exactly one MSIX package, but found: $($packages.FullName -join ', ')"
        }
        Copy-Item $packages[0].FullName $OutputMsix -Force
    }
}

Write-Host ""
Write-Host "Build complete!" -ForegroundColor Green
Write-Host ""

$artifacts = @($OutputZip, $OutputExe, $OutputMsix) | Where-Object { Test-Path $_ }
foreach ($file in $artifacts) {
    $size = (Get-Item $file).Length / 1MB
    Write-Host ("  {0,-55} {1:F1} MB" -f (Resolve-Path $file -Relative), $size)
}
