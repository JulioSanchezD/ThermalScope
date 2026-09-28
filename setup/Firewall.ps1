param([Parameter(Mandatory)][ValidateSet('Install','Uninstall')][string]$Action)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$exe = [IO.Path]::GetFullPath((Join-Path $root 'app\ThermalScope.exe'))
$sha = [Security.Cryptography.SHA256]::Create()
try { $hash = [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($exe.ToLowerInvariant()))).Replace('-','').Substring(0,16) }
finally { $sha.Dispose() }
$name = 'ThermalScope-Setup-' + $hash
if ($Action -eq 'Uninstall') {
    $rule = Get-NetFirewallRule -Name $name -ErrorAction SilentlyContinue
    if ($rule) {
        $program = ($rule | Get-NetFirewallApplicationFilter).Program
        if ($program -ne $exe) { throw 'Refusing to remove a firewall rule belonging to a different application path.' }
        $rule | Remove-NetFirewallRule
    }
    exit 0
}
if (!(Test-Path $exe)) { throw 'The installed server executable is missing.' }
# Recreate only this installation's rule so every constraint is explicit.
Get-NetFirewallRule -Name $name -ErrorAction SilentlyContinue | Remove-NetFirewallRule
New-NetFirewallRule -Name $name -DisplayName 'ThermalScope (home LAN only)' -Direction Inbound -Action Allow -Protocol TCP -LocalPort 8088 -Program $exe -Profile Private -RemoteAddress LocalSubnet | Out-Null
