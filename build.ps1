[CmdletBinding()]
param(
    [ValidateSet('Restore', 'Build', 'Test', 'Publish', 'All')]
    [string]$Task = 'All',

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$dotnetPath = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
$solutionPath = Join-Path $projectRoot 'SoundPartyCompanion.sln'
$appProjectPath = Join-Path $projectRoot 'src\SoundPartyCompanion.App\SoundPartyCompanion.App.csproj'
$resultsPath = Join-Path $projectRoot 'artifacts\test-results'
$publishPath = Join-Path $projectRoot 'artifacts\publish\win-x64'

if (-not (Test-Path -LiteralPath $dotnetPath)) {
    throw 'Local .NET SDK not found. Run .\scripts\install-dotnet.ps1 first.'
}

$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.tools\dotnet-home'
$env:NUGET_PACKAGES = Join-Path $projectRoot '.packages'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'

function Invoke-DotNet {
    param([Parameter(Mandatory)][string[]]$Arguments)

    & $dotnetPath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet command failed with exit code ${LASTEXITCODE}: $($Arguments -join ' ')"
    }
}

$needsRestore = $Task -in @('Restore', 'Build', 'Test', 'Publish', 'All')
$needsBuild = $Task -in @('Build', 'Test', 'Publish', 'All')
$needsTest = $Task -in @('Test', 'All')
$needsPublish = $Task -in @('Publish', 'All')

if ($needsRestore) {
    Invoke-DotNet -Arguments @('restore', $solutionPath)
}

if ($needsBuild) {
    Invoke-DotNet -Arguments @('build', $solutionPath, '--configuration', $Configuration, '--no-restore')
}

if ($needsTest) {
    New-Item -ItemType Directory -Force -Path $resultsPath | Out-Null
    Invoke-DotNet -Arguments @(
        'test', $solutionPath,
        '--configuration', $Configuration,
        '--no-build',
        '--logger', 'trx;LogFileName=sound-party-tests.trx',
        '--results-directory', $resultsPath
    )
}

if ($needsPublish) {
    New-Item -ItemType Directory -Force -Path $publishPath | Out-Null
    Invoke-DotNet -Arguments @(
        'publish', $appProjectPath,
        '--configuration', $Configuration,
        '--runtime', 'win-x64',
        '--self-contained', 'true',
        '--no-build',
        '--output', $publishPath
    )
}

Write-Host "Task '$Task' completed successfully."
