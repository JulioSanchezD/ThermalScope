$ErrorActionPreference = 'Stop'
$output = Join-Path $PSScriptRoot 'tools\discovery.json'
New-Item -ItemType Directory -Force (Split-Path $output -Parent) | Out-Null
$json = & (Join-Path $PSScriptRoot 'app\ThermalScope.exe') --discover
if ($LASTEXITCODE -ne 0) { throw 'Sensor discovery failed.' }
[IO.File]::WriteAllText($output, ($json -join "`n"))
Write-Host "Sensor report saved to $output"
