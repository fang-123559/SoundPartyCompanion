[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$appProjectPath = Join-Path $projectRoot 'src\SoundPartyCompanion.App\SoundPartyCompanion.App.csproj'
$releasePath = Join-Path $projectRoot 'artifacts\publish\win-x64\SoundPartyCompanion.App.exe'
$installerDirectory = Join-Path $projectRoot 'artifacts\installer'
$compilerPath = Join-Path $projectRoot '.tools\innosetup\ISCC.exe'
$scriptPath = Join-Path $projectRoot 'installer\SoundPartyCompanion.iss'
$temporaryDirectory = Join-Path $projectRoot 'artifacts\release-prep\0.1.1\temp'
New-Item -ItemType Directory -Force -Path $installerDirectory, $temporaryDirectory | Out-Null
$env:TEMP = $temporaryDirectory
$env:TMP = $temporaryDirectory
$env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = $temporaryDirectory

[xml]$project = Get-Content -LiteralPath $appProjectPath -Raw
$version = [string]$project.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid app release version.' }
$installerPath = Join-Path $installerDirectory "SoundPartyCompanion-Setup-$version-win-x64.exe"
& (Join-Path $PSScriptRoot 'install-inno.ps1')

# Release publication cannot overwrite a running single-file executable.
# Only close this checkout's exact release path, never another installed copy.
$restartRelease = $false
try {
    $releaseProcesses = @(Get-Process -Name 'SoundPartyCompanion.App' -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and [string]::Equals($_.Path, $releasePath, [StringComparison]::OrdinalIgnoreCase) })
    foreach ($releaseProcess in $releaseProcesses) {
        $restartRelease = $true
        Stop-Process -Id $releaseProcess.Id -ErrorAction Stop
        if (-not $releaseProcess.WaitForExit(10000)) { throw 'Release process did not exit.' }
    }

    & (Join-Path $projectRoot 'build.ps1') -Task All -Configuration Release
    if (-not $?) { throw 'Build pipeline failed.' }
    $appMetadata = (Get-Item -LiteralPath $releasePath).VersionInfo
    if ($appMetadata.ProductVersion -ne $version -or $appMetadata.FileVersion -ne "$version.0") {
        throw 'Published app version does not match the package version.'
    }
    [xml]$testResults = Get-Content -LiteralPath (Join-Path $projectRoot 'artifacts\test-results\sound-party-tests.trx') -Raw
    $counters = $testResults.TestRun.ResultSummary.Counters
    if ([int]$counters.total -le 0 -or [int]$counters.passed -ne [int]$counters.total) {
        throw 'Packaging requires all automated tests to pass.'
    }

    $dotnetPath = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
    $sdkVersion = (& $dotnetPath --version).Trim()
    $runtimeConfigPath = Join-Path $projectRoot 'src\SoundPartyCompanion.App\bin\Release\net10.0-windows10.0.19041.0\win-x64\SoundPartyCompanion.App.runtimeconfig.json'
    $runtimeConfig = Get-Content -LiteralPath $runtimeConfigPath -Raw | ConvertFrom-Json
    $frameworks = @($runtimeConfig.runtimeOptions.includedFrameworks)
    $runtimeVersion = ($frameworks | Where-Object name -eq 'Microsoft.NETCore.App').version
    $desktopVersion = ($frameworks | Where-Object name -eq 'Microsoft.WindowsDesktop.App').version
    if (-not $runtimeVersion -or -not $desktopVersion) { throw 'Self-contained runtime metadata missing.' }
    $licenseDirectory = Join-Path (Split-Path -Parent $releasePath) 'licenses'
    New-Item -ItemType Directory -Force -Path $licenseDirectory | Out-Null
    foreach ($notice in @('LICENSE','THIRD-PARTY-NOTICES.md')) {
        Copy-Item -LiteralPath (Join-Path $projectRoot $notice) -Destination (Join-Path (Split-Path -Parent $releasePath) $notice)
    }
    $runtimePack = Join-Path $projectRoot ".packages\microsoft.netcore.app.runtime.win-x64\$runtimeVersion"
    foreach ($notice in @('LICENSE.TXT','THIRD-PARTY-NOTICES.TXT')) {
        Copy-Item -LiteralPath (Join-Path $runtimePack $notice) -Destination (Join-Path $licenseDirectory "dotnet-$notice")
    }
    $desktopPack = Join-Path $projectRoot ".packages\microsoft.windowsdesktop.app.runtime.win-x64\$desktopVersion"
    Copy-Item -LiteralPath (Join-Path $desktopPack 'LICENSE') -Destination (Join-Path $licenseDirectory 'windowsdesktop-LICENSE.txt')
    # Desktop runtime packs omit the notices: retain WPF and WinForms notices from the matching release tags.
    $desktopNotices = foreach ($repository in @('wpf','winforms')) {
        $url = "https://raw.githubusercontent.com/dotnet/$repository/v$desktopVersion/THIRD-PARTY-NOTICES.TXT"
        $notice = (Invoke-WebRequest -Uri $url).Content
        if ([string]::IsNullOrWhiteSpace($notice)) { throw 'Desktop runtime notice download was empty.' }
        "Third-party notices for $repository $desktopVersion`r`nSource: $url`r`n`r`n$notice"
    }
    [IO.File]::WriteAllText((Join-Path $licenseDirectory 'windowsdesktop-THIRD-PARTY-NOTICES.txt'),
        ($desktopNotices -join "`r`n`r`n"), [Text.UTF8Encoding]::new($false))
    Copy-Item -LiteralPath (Join-Path $projectRoot '.tools\innosetup\license.txt') -Destination (Join-Path $licenseDirectory 'inno-LICENSE.txt')
    $payloadNames = @('SoundPartyCompanion.App.exe','LICENSE','THIRD-PARTY-NOTICES.md',
        'licenses/dotnet-LICENSE.txt','licenses/dotnet-THIRD-PARTY-NOTICES.txt',
        'licenses/windowsdesktop-LICENSE.txt','licenses/windowsdesktop-THIRD-PARTY-NOTICES.txt','licenses/inno-LICENSE.txt')
    $payload = @(foreach ($name in $payloadNames) {
        $file = Get-Item -LiteralPath (Join-Path (Split-Path -Parent $releasePath) $name)
        @{ file=$name; bytes=$file.Length; sha256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash }
    })

    & $compilerPath "--define=MyAppVersion=$version" "--output-dir=$installerDirectory" $scriptPath
    if ($LASTEXITCODE -ne 0) { throw "Installer compilation failed: $LASTEXITCODE" }
    if (-not (Test-Path -LiteralPath $installerPath)) { throw 'Installer output is missing.' }
    $installer = Get-Item -LiteralPath $installerPath
    $installerNumericVersion = '{0}.{1}.{2}.{3}' -f $installer.VersionInfo.FileMajorPart,
        $installer.VersionInfo.FileMinorPart, $installer.VersionInfo.FileBuildPart, $installer.VersionInfo.FilePrivatePart
    # Inno pads string version resources; check numeric fields and trimmed product text.
    if ($installerNumericVersion -ne "$version.0" -or
        $installer.VersionInfo.ProductVersion.Trim() -notin @($version, "$version.0")) {
        throw 'Installer version mismatch.'
    }
    $installerHash = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash
    $appHash = (Get-FileHash -LiteralPath $releasePath -Algorithm SHA256).Hash
    $utf8 = New-Object System.Text.UTF8Encoding($false)
    [IO.File]::WriteAllText("$installerPath.sha256", "$installerHash  $($installer.Name)`r`n", $utf8)
    $report = [ordered]@{
        version = $version
        target = 'Windows 11 win-x64; self-contained; current-user'
        createdUtc = [DateTime]::UtcNow.ToString('o')
        installerFile = $installer.Name
        installerBytes = $installer.Length
        installerSha256 = $installerHash
        applicationSha256 = $appHash
        sdkVersion = $sdkVersion
        runtimeVersion = $runtimeVersion
        windowsDesktopVersion = $desktopVersion
        sourceCommit = (git -C $projectRoot rev-parse HEAD)
        sourceDirty = [bool]@(git -C $projectRoot status --porcelain).Count
        compilerVersion = (& $compilerPath --version | Out-String).Trim()
        compilerSignature = 'Valid; Pyrsys B.V. (checked by install-inno.ps1)'
        installerSignature = [string](Get-AuthenticodeSignature -LiteralPath $installerPath).Status
        testsPassed = [int]$counters.passed
        testsTotal = [int]$counters.total
        payload = $payload
        excluded = @('PDBs', 'configuration', 'device-selection cache', 'logs', 'build tools')
        installationExecuted = $false
        pending = @('See release validation report; packaging does not prove installation or hardware acceptance.')
    }
    [IO.File]::WriteAllText((Join-Path $installerDirectory "packaging-report-$version.json"),
        ($report | ConvertTo-Json -Depth 6), $utf8)
    Write-Host "Installer: $installerPath"
    Write-Host "SHA256: $installerHash"
} finally {
    if ($restartRelease -and (Test-Path -LiteralPath $releasePath)) {
        Start-Process -FilePath $releasePath -WorkingDirectory (Split-Path -Parent $releasePath) -WindowStyle Hidden | Out-Null
        Write-Host 'Release application reopened; no installer or uninstaller was executed.'
    }
}
