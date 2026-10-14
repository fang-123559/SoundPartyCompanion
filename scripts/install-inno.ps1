[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$compilerDirectory = Join-Path $projectRoot '.tools\innosetup'
$compilerPath = Join-Path $compilerDirectory 'ISCC.exe'
$cacheDirectory = Join-Path $projectRoot '.tools\cache'
$downloadPath = Join-Path $cacheDirectory 'innosetup-7.1.0-x64.exe'
$temporaryDirectory = Join-Path $projectRoot 'artifacts\release-prep\0.1.1\temp'

function Assert-OfficialSignature([string]$Path) {
    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    if ($signature.Status -ne 'Valid' -or
        $signature.SignerCertificate.Subject -notmatch '(?:^|,\s*)CN=Pyrsys B\.V\.(?:,|$)') {
        throw "Official compiler signature verification failed: $($signature.Status)"
    }
}

if (Test-Path -LiteralPath $compilerPath) {
    Assert-OfficialSignature $compilerPath
    $compilerVersion = (& $compilerPath --version | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $compilerVersion -notmatch '\b7\.1\.0\b') {
        throw 'Unexpected compiler version; expected 7.1.0.'
    }
    Write-Host "Compiler ready: $compilerPath"
    return
}

New-Item -ItemType Directory -Force -Path $compilerDirectory, $cacheDirectory, $temporaryDirectory | Out-Null
$env:TEMP = $temporaryDirectory
$env:TMP = $temporaryDirectory
if (-not (Test-Path -LiteralPath $downloadPath)) {
    Invoke-WebRequest -UseBasicParsing -Uri 'https://github.com/jrsoftware/issrc/releases/download/is-7_1_0/innosetup-7.1.0-x64.exe' -OutFile $downloadPath
}
Assert-OfficialSignature $downloadPath
$arguments = '/PORTABLE=1 /CURRENTUSER /VERYSILENT /SUPPRESSMSGBOXES /SP- /NORESTART /NOICONS /DIR="' + $compilerDirectory + '"'
$process = Start-Process -FilePath $downloadPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
if (-not $process.WaitForExit(60000)) {
    throw 'Compiler provisioning is still running. Check its completion before retrying.'
}
if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $compilerPath)) {
    throw "Compiler provisioning failed: $($process.ExitCode)"
}
Assert-OfficialSignature $compilerPath
$compilerVersion = (& $compilerPath --version | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or $compilerVersion -notmatch '\b7\.1\.0\b') {
    throw 'Unexpected compiler engine version; expected 7.1.0.'
}
Write-Host "Compiler ready: $compilerPath"
Write-Host "Download SHA256: $((Get-FileHash -LiteralPath $downloadPath -Algorithm SHA256).Hash)"
