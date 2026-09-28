$ErrorActionPreference = 'Stop'
$exe = Join-Path $PSScriptRoot 'app\desktop\ThermalScope.Desktop.exe'
if (!(Test-Path $exe)) { throw 'Build the desktop application first.' }
$programs = [Environment]::GetFolderPath('Programs')
$shortcutPath = Join-Path $programs 'ThermalScope.lnk'
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $exe
$shortcut.WorkingDirectory = Split-Path $exe -Parent
$shortcut.Description = 'ThermalScope — local gaming temperature monitor'
$shortcut.IconLocation = "$exe,0"
$shortcut.Save()
Write-Host "Start menu entry installed: $shortcutPath"
