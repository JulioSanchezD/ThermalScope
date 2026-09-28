param([switch]$NonInteractive)
$ErrorActionPreference = 'Stop'
$name = 'ThermalScope-Private-LAN'
$exe = Join-Path $PSScriptRoot 'app\ThermalScope.exe'
if (!(Test-Path $exe)) { throw 'Build ThermalScope first.' }
if (Get-NetFirewallRule -Name $name -ErrorAction SilentlyContinue) {
    Set-NetFirewallRule -Name $name -Enabled True -Profile Private -Action Allow
    Get-NetFirewallRule -Name $name | Get-NetFirewallApplicationFilter | Set-NetFirewallApplicationFilter -Program $exe
} else {
    New-NetFirewallRule -Name $name -DisplayName 'ThermalScope (home LAN only)' -Direction Inbound -Action Allow -Protocol TCP -LocalPort 8088 -Program $exe -Profile Private -RemoteAddress LocalSubnet | Out-Null
}
Write-Host 'ThermalScope is allowed on the Private network profile, local subnet, TCP 8088.' -ForegroundColor Green
if (!$NonInteractive) { Read-Host 'Press Enter to close' }
