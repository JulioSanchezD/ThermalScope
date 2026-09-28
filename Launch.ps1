$ErrorActionPreference = 'Stop'
$exe = Join-Path $PSScriptRoot 'app\desktop\ThermalScope.Desktop.exe'
Start-Process -FilePath $exe -WorkingDirectory (Join-Path $PSScriptRoot 'app\desktop')
