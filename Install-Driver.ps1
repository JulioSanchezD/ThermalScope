$ErrorActionPreference = 'Stop'
$installed = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO' -ErrorAction SilentlyContinue
if ($installed) { Write-Host "PawnIO $($installed.DisplayVersion) is already installed."; exit 0 }
$toolsDir = Join-Path $PSScriptRoot 'tools'
New-Item -ItemType Directory -Force $toolsDir | Out-Null
$installer = Join-Path $toolsDir 'PawnIO_setup.exe'
# The exact installer bundled by the official Libre Hardware Monitor v0.9.6 release.
Invoke-WebRequest 'https://raw.githubusercontent.com/LibreHardwareMonitor/LibreHardwareMonitor/v0.9.6/LibreHardwareMonitor/Resources/PawnIO_setup.exe' -OutFile $installer
$signature = Get-AuthenticodeSignature $installer
if ($signature.Status -ne 'Valid') { throw "Driver installer signature did not validate: $($signature.Status). Nothing was executed." }
Write-Host "Verified signer: $($signature.SignerCertificate.Subject)"
Start-Process -FilePath $installer -ArgumentList '-install' -Verb RunAs -Wait
Write-Host 'After installation, restart ThermalScope to discover CPU and motherboard sensors.'
