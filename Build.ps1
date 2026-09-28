param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'app'))
$ErrorActionPreference = 'Stop'
$dotnet = Join-Path $env:LOCALAPPDATA 'ThermalScope\dotnet\dotnet.exe'
if (!(Test-Path $dotnet)) {
    $globalSdk = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($globalSdk) { $dotnet = $globalSdk.Source }
    else { throw 'A .NET 10 SDK is needed to build. The published app does not need an SDK.' }
}
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
& $dotnet publish (Join-Path $PSScriptRoot 'ThermalScope.csproj') -c Release -o $OutputDirectory --self-contained true
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
& $dotnet publish (Join-Path $PSScriptRoot 'desktop\ThermalScope.Desktop.csproj') -c Release -o (Join-Path $OutputDirectory 'desktop') --self-contained true
if ($LASTEXITCODE -ne 0) { throw 'Desktop build failed.' }
Write-Host 'Ready. Double-click Start ThermalScope.cmd.' -ForegroundColor Green
