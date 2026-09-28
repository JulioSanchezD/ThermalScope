$ErrorActionPreference = 'Stop'
try {
    $installed = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO' -ErrorAction SilentlyContinue
    if ($installed) { Write-Host "PawnIO $($installed.DisplayVersion) is already installed."; exit 0 }
    $downloadDir = Join-Path ([IO.Path]::GetTempPath()) ('ThermalScope-driver-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory $downloadDir | Out-Null
    $installer = Join-Path $downloadDir 'PawnIO_setup.exe'
    Invoke-WebRequest 'https://raw.githubusercontent.com/LibreHardwareMonitor/LibreHardwareMonitor/v0.9.6/LibreHardwareMonitor/Resources/PawnIO_setup.exe' -UseBasicParsing -OutFile $installer
    $signature = Get-AuthenticodeSignature $installer
    if ($signature.Status -ne 'Valid') { throw "Driver signature did not validate: $($signature.Status). Nothing was executed." }
    Write-Host "Verified signer: $($signature.SignerCertificate.Subject)"
    $process = Start-Process -FilePath $installer -ArgumentList '-install' -PassThru
    $process.WaitForExit()
    if ($process.ExitCode -notin @(0,3010)) { throw "Driver setup returned $($process.ExitCode)." }
    if (!(Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO' -ErrorAction SilentlyContinue)) { throw 'PawnIO was not installed. Driver setup may have been cancelled.' }
    exit $process.ExitCode
} catch {
    Write-Host "Sensor driver setup failed: $_" -ForegroundColor Red
    exit 1
}
