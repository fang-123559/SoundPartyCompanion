[CmdletBinding()]
param(
    [string]$Version = '10.0.402'
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$projectRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$toolsRoot = Join-Path $projectRoot '.tools'
$cacheRoot = Join-Path $toolsRoot 'cache'
$installRoot = Join-Path $toolsRoot 'dotnet'
$dotnetPath = Join-Path $installRoot 'dotnet.exe'
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.tools/dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:TEMP = Join-Path $projectRoot 'artifacts/release-prep/0.1.1/temp'
$env:TMP = $env:TEMP

New-Item -ItemType Directory -Force -Path $cacheRoot | Out-Null
New-Item -ItemType Directory -Force -Path $installRoot | Out-Null
New-Item -ItemType Directory -Force -Path $env:TEMP | Out-Null

$pinnedVersion = (Get-Content (Join-Path $projectRoot 'global.json') -Raw | ConvertFrom-Json).sdk.version
if ($Version -ne $pinnedVersion) { throw 'SDK version must match global.json.' }
$metadata = Invoke-RestMethod -Uri 'https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json'
$release = $metadata.releases | Where-Object { $_.sdks.version -contains $Version } | Select-Object -First 1
$sdk = $release.sdks | Where-Object version -eq $Version | Select-Object -First 1
$archive = $sdk.files | Where-Object { $_.rid -eq 'win-x64' -and $_.name -like '*.zip' } | Select-Object -First 1
if (-not $archive -or $Version -match '-') { throw 'Stable pinned SDK archive not found.' }
$expectedHash = 'ae4fd528cb7e9f6af6aaf7de36715a69f761f183744f1bb3025d19f5a92447090ec561410e46266c1d10a63781ed6f3de771760bc76c876d87daabc367b898cd'
if ($archive.hash -ne $expectedHash) { throw 'Official SDK metadata does not match the pinned SHA512.' }
$downloadUri = [Uri]$archive.url
if ($downloadUri.Scheme -ne 'https' -or $downloadUri.Host -ne 'builds.dotnet.microsoft.com') {
    throw 'Unexpected SDK download origin.'
}
$zipPath = Join-Path $cacheRoot "dotnet-sdk-$Version-win-x64.zip"
if (-not (Test-Path -LiteralPath $zipPath) -or
    (Get-FileHash -LiteralPath $zipPath -Algorithm SHA512).Hash -ne $expectedHash) {
    try {
        Invoke-WebRequest -Uri $archive.url -OutFile $zipPath -TimeoutSec 180
    } catch {
        # Microsoft storage mirror; the same pinned hash is required regardless of transport.
        $mirror = "https://dotnetcli.blob.core.windows.net/dotnet/Sdk/$Version/dotnet-sdk-$Version-win-x64.zip"
        Invoke-WebRequest -Uri $mirror -OutFile $zipPath -TimeoutSec 900
    }
}
$actualHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA512).Hash
if ($actualHash -ne $expectedHash) { throw 'SDK SHA512 verification failed; archive was not extracted.' }
if (-not (Test-Path -LiteralPath (Join-Path $installRoot "sdk/$Version/dotnet.dll"))) {
    Expand-Archive -LiteralPath $zipPath -DestinationPath $installRoot -Force
}

if (-not (Test-Path -LiteralPath $dotnetPath)) {
    throw 'The local dotnet executable was not created.'
}

$installedVersion = (& $dotnetPath --version).Trim()
if ($installedVersion -ne $Version) {
    throw "Expected .NET SDK $Version, but found $installedVersion."
}

Write-Host ".NET SDK $installedVersion installed at $installRoot"
[ordered]@{ sdk=$Version; runtime=$release.runtime.version; windowsDesktop=$release.windowsdesktop.version;
    url=$archive.url; sha512=$actualHash; verifiedUtc=[DateTime]::UtcNow.ToString('o') } |
    ConvertTo-Json | Set-Content (Join-Path $cacheRoot "dotnet-sdk-$Version-verification.json") -Encoding utf8

