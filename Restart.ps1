$ErrorActionPreference = 'Stop'
$exe = Join-Path $PSScriptRoot 'app\ThermalScope.exe'
$gui = Join-Path $PSScriptRoot 'app\desktop\ThermalScope.Desktop.exe'
if (Get-Process ThermalScope.Desktop -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $gui }) {
    throw 'Close the ThermalScope control window before rebuilding. It will save recordings and stop its server.'
}
$listeners = @(Get-NetTCPConnection -LocalPort 8088 -State Listen -ErrorAction SilentlyContinue | Select-Object -ExpandProperty OwningProcess -Unique)
foreach ($processId in $listeners) {
    $process = Get-Process -Id $processId
    if ($process.Path -ne $exe) { throw 'Port 8088 belongs to a different application; it was not stopped.' }
    $live = Invoke-RestMethod 'http://localhost:8088/api/live' -TimeoutSec 5
    if ($live.active) { throw 'A session is recording. Stop and save it before rebuilding.' }
    Stop-Process -Id $processId -Force
    $process.WaitForExit()
}
& (Join-Path $PSScriptRoot 'Build.ps1')
$started = Start-Process $gui -WorkingDirectory (Join-Path $PSScriptRoot 'app\desktop') -PassThru
Write-Host "ThermalScope restarted. PID: $($started.Id)"
