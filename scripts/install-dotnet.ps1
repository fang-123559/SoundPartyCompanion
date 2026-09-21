[CmdletBinding()]
param(
    [string]$Version = '8.0.425'
)

$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$toolsRoot = Join-Path $projectRoot '.tools'
$cacheRoot = Join-Path $toolsRoot 'cache'
$installRoot = Join-Path $toolsRoot 'dotnet'
$installerPath = Join-Path $cacheRoot 'dotnet-install.ps1'
$dotnetPath = Join-Path $installRoot 'dotnet.exe'

New-Item -ItemType Directory -Force -Path $cacheRoot | Out-Null
New-Item -ItemType Directory -Force -Path $installRoot | Out-Null

if (-not (Test-Path -LiteralPath $installerPath)) {
    Invoke-WebRequest -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installerPath
}

& $installerPath -Version $Version -Architecture 'x64' -InstallDir $installRoot -NoPath
if ($LASTEXITCODE -ne 0) {
    throw "dotnet-install.ps1 failed with exit code $LASTEXITCODE."
}

if (-not (Test-Path -LiteralPath $dotnetPath)) {
    throw 'The local dotnet executable was not created.'
}

$installedVersion = (& $dotnetPath --version).Trim()
if ($installedVersion -ne $Version) {
    throw "Expected .NET SDK $Version, but found $installedVersion."
}

Write-Host ".NET SDK $installedVersion installed at $installRoot"

