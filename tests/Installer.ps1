$ErrorActionPreference = 'Stop'
[Net.WebRequest]::DefaultWebProxy = $null
$root = Split-Path $PSScriptRoot -Parent
$installer = Join-Path $root 'dist\ThermalScope-Setup-Test.exe'
$installDir = Join-Path $env:ProgramFiles 'ThermalScope Setup Test'
$output = Join-Path $root ('tools\installer-check-' + [Guid]::NewGuid().ToString('N'))
$data = Join-Path $output 'data'
$registry = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{BC053406-02C1-493B-BB04-8BC59677D3DB}_is1'
$shortcut = Join-Path ([Environment]::GetFolderPath('CommonPrograms')) 'ThermalScope Setup Test\ThermalScope Setup Test.lnk'
$desktopLink = Join-Path ([Environment]::GetFolderPath('CommonDesktopDirectory')) 'ThermalScope Setup Test.lnk'
$checks = @(); $desktop = $null; $server = $null; $installed = $false
if (!(Test-Path $installer)) { throw 'First run Build-Installer.ps1 -TestMode.' }
if ((Test-Path $installDir) -or (Test-Path $registry)) { throw 'An existing test installation was found. Uninstall that test product before running these checks.' }
New-Item -ItemType Directory $output | Out-Null
function Check($condition, $message) { if (!$condition) { throw $message }; $script:checks += $message; Write-Host "PASS: $message" }
function RunSetup($label) {
    $process = Start-Process $installer -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/TASKS=lan,desktopicon',('/LOG="' + (Join-Path $output ($label + '.log')) + '"')) -PassThru
    if (!$process.WaitForExit(150000)) { throw 'Setup did not finish in time.' }
    return $process.ExitCode
}
function WaitServer {
    for ($i=0; $i -lt 60; $i++) {
        try { $live = Invoke-RestMethod http://127.0.0.1:18101/api/live -TimeoutSec 1; if ($live.frame) { return $live } } catch { }
        Start-Sleep -Milliseconds 250
    }
    throw 'Installed server did not start.'
}
function StopServer($process, $pipeName) {
    $pipe = New-Object IO.Pipes.NamedPipeClientStream('.', $pipeName, [IO.Pipes.PipeDirection]::Out)
    try { $pipe.Connect(3000); $writer = New-Object IO.StreamWriter($pipe); $writer.WriteLine('stop'); $writer.Flush(); $writer.Dispose() } finally { $pipe.Dispose() }
    if (!$process.WaitForExit(15000)) { throw 'Installed server did not stop gracefully.' }
}
try {
    Check ((RunSetup 'install') -eq 0) 'Setup completes without optional-task errors'
    $installed = $true
    Check (Test-Path (Join-Path $installDir 'app\desktop\ThermalScope.Desktop.exe')) 'Default destination is Program Files, not Documents'
    Check ((Get-ItemProperty $registry).DisplayVersion -eq '0.1.0') 'Windows Installed apps registration includes the version'
    Check ((Get-Content (Join-Path $installDir 'LICENSE') -Raw) -eq (Get-Content (Join-Path $root 'LICENSE') -Raw)) 'Installer includes the MIT license'
    Check ((Test-Path (Join-Path $installDir 'licenses\LibreHardwareMonitor-source-notices\LICENSE')) -and (Test-Path (Join-Path $installDir 'licenses\LibreHardwareMonitor-source-notices\THIRD-PARTY-NOTICES.txt')) -and (Test-Path (Join-Path $installDir 'licenses\PawnIO.Modules-0.1.6-source.zip')) -and (Test-Path (Join-Path $installDir 'licenses\INDEX.txt'))) 'Dependency licenses and corresponding-source references are included'
    $link = (New-Object -ComObject WScript.Shell).CreateShortcut($shortcut)
    Check ($link.TargetPath -eq (Join-Path $installDir 'app\desktop\ThermalScope.Desktop.exe') -and (Test-Path $desktopLink)) 'Start menu and optional desktop shortcuts target the installed launcher'
    $exe = Join-Path $installDir 'app\ThermalScope.exe'
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $hash = [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($exe.ToLowerInvariant()))).Replace('-','').Substring(0,16) } finally { $sha.Dispose() }
    $ruleName = 'ThermalScope-Setup-' + $hash
    $rule = Get-NetFirewallRule -Name $ruleName
    $ports = $rule | Get-NetFirewallPortFilter; $addresses = $rule | Get-NetFirewallAddressFilter
    Check ($rule.Profile -eq 'Private' -and $ports.LocalPort -eq '8088' -and $ports.Protocol -eq 'TCP' -and $addresses.RemoteAddress -eq 'LocalSubnet' -and ($rule | Get-NetFirewallApplicationFilter).Program -eq $exe) 'Firewall is scoped to the installed executable, Private LAN and TCP 8088'
    $gui = Join-Path $installDir 'app\desktop\ThermalScope.Desktop.exe'
    $selftest = Join-Path $output 'desktop'
    $desktop = Start-Process $gui -ArgumentList @('--demo','--port','18101','--data-dir',('"' + $data + '"'),'--self-test',('"' + $selftest + '"')) -PassThru
    if (!$desktop.WaitForExit(60000)) { throw 'Installed launcher self-check timed out.' }
    $report = Get-Content (Join-Path $selftest 'launcher-checks.json') -Raw | ConvertFrom-Json
    Check $report.passed 'Installed launcher starts, stops, restarts and saves active recordings on close'
    # Verify the running-app guard without modifying the real product or real sessions.
    $desktop = Start-Process $gui -ArgumentList @('--demo','--port','18101','--data-dir',('"' + $data + '"')) -PassThru
    $null = WaitServer
    Check ((RunSetup 'blocked-upgrade') -ne 0 -and !$desktop.HasExited) 'Setup refuses to replace a running launcher'
    if (!$desktop.CloseMainWindow() -or !$desktop.WaitForExit(20000)) { throw 'Could not close the test launcher gracefully.' }
    $desktop = $null
    $db = Join-Path $data 'sessions.sqlite'
    $recordingHash = (Get-FileHash $db).Hash
    Write-Host 'Building an isolated 0.1.1 upgrade...'
    & (Join-Path $root 'Build-Installer.ps1') -TestMode -ReusePayload -Version '0.1.1'
    Check ((RunSetup 'upgrade') -eq 0 -and (Get-ItemProperty $registry).DisplayVersion -eq '0.1.1') 'Upgrade reuses the installation folder and updates Installed apps version'
    Check ((Get-FileHash $db).Hash -eq $recordingHash) 'Upgrade leaves existing recordings unchanged'
    $pipeName = 'ThermalScope.installer-check.' + [Guid]::NewGuid().ToString('N')
    $server = Start-Process $exe -ArgumentList @('--demo','--port','18101','--data-dir',('"' + $data + '"'),'--control-pipe',$pipeName) -WindowStyle Hidden -PassThru
    $null = WaitServer
    $sessions = Invoke-RestMethod http://127.0.0.1:18101/api/sessions
    Check (@($sessions).Count -eq 2 -and @($sessions | Where-Object { $_.status -eq 'stopped' -and $_.samples -ge 2 }).Count -eq 2) 'Previously recorded sessions remain readable after upgrade'
    StopServer $server $pipeName; $server = $null
    $recordingHash = (Get-FileHash $db).Hash
    $driverBefore = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO' -ErrorAction SilentlyContinue).DisplayVersion
    $uninstaller = Start-Process (Join-Path $installDir 'unins000.exe') -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG="' + (Join-Path $output 'uninstall.log') + '"')) -PassThru
    if (!$uninstaller.WaitForExit(60000)) { throw 'Uninstall timed out.' }
    Check ($uninstaller.ExitCode -eq 0 -and !(Test-Path $exe) -and !(Test-Path $registry)) 'Uninstall removes the app and its Installed apps registration'
    $installed = $false
    Check (!(Test-Path $shortcut) -and !(Test-Path $desktopLink)) 'Uninstall removes its shortcuts'
    Check (!(Get-NetFirewallRule -Name $ruleName -ErrorAction SilentlyContinue)) 'Uninstall removes only its own firewall rule'
    Check ((Get-FileHash $db).Hash -eq $recordingHash) 'Uninstall preserves recorded sessions'
    $driverAfter = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO' -ErrorAction SilentlyContinue).DisplayVersion
    Check ($driverBefore -eq $driverAfter) 'Uninstall preserves the shared sensor driver'
    @{passed=$true;checks=$checks;artifacts=$output} | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $output 'installer-checks.json') -Encoding UTF8
    Write-Host "Installer checks passed: $($checks.Count). Artifacts: $output" -ForegroundColor Green
} finally {
    foreach ($process in @($desktop,$server)) { if ($process -and !$process.HasExited) { Stop-Process -Id $process.Id -Force } }
    if ($installed -and (Test-Path $registry) -and (Get-ItemProperty $registry).DisplayName -like 'ThermalScope Setup Test*' -and (Test-Path (Join-Path $installDir 'unins000.exe'))) {
        $cleanup = Start-Process (Join-Path $installDir 'unins000.exe') -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART' -PassThru
        $null = $cleanup.WaitForExit(60000)
    }
}
