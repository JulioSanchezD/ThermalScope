param(
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = '0.1.0',
    [string]$CompilerPath,
    [switch]$InstallCompiler,
    [switch]$TestMode,
    [switch]$ReusePayload
)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$tools = Join-Path $root 'tools'
$payload = Join-Path $tools 'installer-payload'
if (!$CompilerPath) {
    $candidates = @((Join-Path $tools 'InnoSetup\ISCC.exe'), "${env:ProgramFiles}\Inno Setup 7\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe")
    $CompilerPath = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (!$CompilerPath) { $found = Get-Command ISCC.exe -ErrorAction SilentlyContinue; if ($found) { $CompilerPath = $found.Source } }
}
if (!$CompilerPath -and $InstallCompiler) {
    New-Item -ItemType Directory -Force $tools | Out-Null
    $download = Join-Path $tools 'innosetup-7.1.0-x64.exe'
    Invoke-WebRequest 'https://github.com/jrsoftware/issrc/releases/download/is-7_1_0/innosetup-7.1.0-x64.exe' -UseBasicParsing -OutFile $download
    $signature = Get-AuthenticodeSignature $download
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'Pyrsys B\.V\.') { throw 'Inno Setup publisher/signature validation failed. Nothing was executed.' }
    $compilerFolder = Join-Path $tools 'InnoSetup'
    $process = Start-Process $download -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/CURRENTUSER','/NOICONS',('/DIR="' + $compilerFolder + '"')) -PassThru
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) { throw "Inno Setup tool installation failed: $($process.ExitCode)." }
    $CompilerPath = Join-Path $compilerFolder 'ISCC.exe'
}
if (!$CompilerPath -or !(Test-Path $CompilerPath)) { throw 'Install Inno Setup 7 (or 6.7+) and supply -CompilerPath, or run with -InstallCompiler to download the signed build tool from its official release.' }
if (!$ReusePayload) {
    & (Join-Path $root 'Build.ps1') -OutputDirectory (Join-Path $payload 'app')
    & (Join-Path $root 'setup\Collect-Licenses.ps1') -Destination (Join-Path $payload 'licenses')
}
if (!(Test-Path (Join-Path $payload 'app\desktop\ThermalScope.Desktop.exe'))) { throw 'Build payload is missing.' }
$arguments = @("/DSourceRoot=$root", "/DPayloadRoot=$payload", "/DAppVersion=$Version")
if ($TestMode) { $arguments += '/DTestMode' }
& $CompilerPath /Q @arguments (Join-Path $root 'setup\ThermalScope.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
$name = if ($TestMode) { 'ThermalScope-Setup-Test.exe' } else { "ThermalScope-Setup-$Version-win-x64.exe" }
$artifact = Join-Path $root ('dist\' + $name)
(Get-FileHash $artifact -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $name | Set-Content (Join-Path $root ('dist\' + $name + '.sha256')) -Encoding ascii
Write-Host "Installer ready: $artifact" -ForegroundColor Green
