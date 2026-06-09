<#
.SYNOPSIS
  Publish ZTools and produce a Windows installer via Inno Setup.

.DESCRIPTION
  1. Cleans previous publish/dist output.
  2. Runs `dotnet publish` (self-contained, single-file, compressed).
  3. Invokes ISCC.exe to build dist\ztools-setup-<version>.exe.

.PARAMETER Version
  Optional override for the installer version (defaults to the value in ztools.csproj).

.EXAMPLE
  pwsh scripts\build-installer.ps1
  pwsh scripts\build-installer.ps1 -Version 1.2.3
#>

[CmdletBinding()]
param(
    [string]$Version
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    if (-not $Version) {
        $csproj = Join-Path $root 'ztools.csproj'
        $xml = [xml](Get-Content $csproj)
        $Version = ($xml.Project.PropertyGroup.Version | Where-Object { $_ }) -as [string]
        if (-not $Version) { $Version = '1.0.0' }
    }
    Write-Host "==> Building installer for ZTools v$Version" -ForegroundColor Cyan

    $publishDir = Join-Path $root 'publish'
    $distDir    = Join-Path $root 'dist'

    if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }
    New-Item -ItemType Directory -Path $publishDir | Out-Null
    if (-not (Test-Path $distDir)) { New-Item -ItemType Directory -Path $distDir | Out-Null }

    Write-Host "==> dotnet publish (self-contained, single-file)" -ForegroundColor Cyan
    & dotnet publish ztools.csproj `
        -c Release `
        -r win-x64 `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:EnableCompressionInSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -o $publishDir
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

    # Drop .pdb / .xml that slip past DebugType=None.
    Get-ChildItem -Path $publishDir -Include *.pdb,*.xml -Recurse -ErrorAction SilentlyContinue |
        Remove-Item -Force -ErrorAction SilentlyContinue

    $iscc = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1

    if (-not $iscc) { throw "ISCC.exe not found. Install Inno Setup 6 (winget install JRSoftware.InnoSetup)." }

    Write-Host "==> Running ISCC: $iscc" -ForegroundColor Cyan
    & $iscc "/DMyAppVersion=$Version" (Join-Path $root 'installer\ztools.iss')
    if ($LASTEXITCODE -ne 0) { throw "ISCC failed ($LASTEXITCODE)" }

    $output = Join-Path $distDir "ztools-setup-$Version.exe"
    if (Test-Path $output) {
        $size = [Math]::Round((Get-Item $output).Length / 1MB, 2)
        Write-Host "==> Installer ready: $output ($size MB)" -ForegroundColor Green
    } else {
        Write-Warning "ISCC reported success but $output is missing."
    }
} finally {
    Pop-Location
}
