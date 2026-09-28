@echo off
cd /d "%~dp0"
if not exist "app\desktop\ThermalScope.Desktop.exe" (
  echo Please run Build.ps1 first.
  pause
  exit /b 1
)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Launch.ps1"
if errorlevel 1 pause
