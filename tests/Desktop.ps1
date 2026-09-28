$ErrorActionPreference = 'Stop'
[Net.WebRequest]::DefaultWebProxy = $null
$root = Split-Path $PSScriptRoot -Parent
$gui = Join-Path $root 'app\desktop\ThermalScope.Desktop.exe'
$exe = Join-Path $root 'app\ThermalScope.exe'
$output = Join-Path $root ('tools\desktop-check-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $output | Out-Null
$desktop = $null; $server = $null; $crash = $null
$checks = @()
function WaitForServer($port) {
    for ($i = 0; $i -lt 60; $i++) {
        try { return Invoke-RestMethod "http://127.0.0.1:$port/api/info" -TimeoutSec 1 } catch { Start-Sleep -Milliseconds 250 }
    }
    throw "Server $port did not start."
}
function StopWithPipe($process, $pipeName) {
    $pipe = New-Object IO.Pipes.NamedPipeClientStream('.', $pipeName, [IO.Pipes.PipeDirection]::Out)
    try { $pipe.Connect(2000); $writer = New-Object IO.StreamWriter($pipe); $writer.WriteLine('stop'); $writer.Flush(); $writer.Dispose() } finally { $pipe.Dispose() }
    if (!$process.WaitForExit(15000)) { throw 'Server did not shut down gracefully.' }
}
try {
    $desktop = Start-Process $gui -ArgumentList @('--demo','--port','18099','--data-dir',('"' + $output + '"'),'--self-test',('"' + $output + '"')) -PassThru
    if (!$desktop.WaitForExit(60000)) { throw 'Desktop self-check timed out.' }
    $report = Get-Content (Join-Path $output 'launcher-checks.json') -Raw | ConvertFrom-Json
    if (!$report.passed) { throw $report.error }
    $checks += $report.checks
    $pipeName = 'ThermalScope.desktop-check.' + [Guid]::NewGuid().ToString('N')
    $server = Start-Process $exe -ArgumentList @('--demo','--port','18099','--data-dir',('"' + $output + '"'),'--control-pipe',$pipeName) -WindowStyle Hidden -PassThru
    $null = WaitForServer 18099
    $sessions = Invoke-RestMethod http://127.0.0.1:18099/api/sessions
    $closed = $sessions | Where-Object { $_.name -eq 'Window-close check' }
    if (!$closed -or $closed.status -ne 'stopped' -or $closed.samples -lt 2) { throw 'Closing the window did not save its active session gracefully.' }
    $checks += 'Actual window close saves the active recording before exit'
    StopWithPipe $server $pipeName; $server = $null
    # Verify the OS ownership guard even if the GUI is forcibly terminated.
    $crashData = Join-Path $output 'crash'
    $crash = Start-Process $gui -ArgumentList @('--demo','--port','18100','--data-dir',('"' + $crashData + '"')) -PassThru
    $info = WaitForServer 18100
    $ownedProcessId = $info.processId
    $duplicate = Start-Process $gui -ArgumentList @('--demo','--port','18100') -PassThru
    if (!$duplicate.WaitForExit(5000)) { throw 'Launching a second instance should return to the existing window.' }
    $checks += 'Duplicate launcher exits without creating another server'
    Stop-Process -Id $crash.Id -Force; $crash.WaitForExit(); $crash = $null
    Start-Sleep -Milliseconds 700
    if (Get-Process -Id $ownedProcessId -ErrorAction SilentlyContinue) { throw 'A server was left behind when the GUI crashed.' }
    $checks += 'Forcing the launcher to exit also terminates its owned server'
    $checks | ForEach-Object { Write-Host "PASS: $_" }
    [IO.File]::WriteAllText((Join-Path $output 'complete-checks.json'), (@{passed=$true;checks=$checks} | ConvertTo-Json -Depth 5))
    Write-Host "Desktop checks passed: $($checks.Count). Artifacts: $output" -ForegroundColor Green
} finally {
    foreach ($process in @($desktop,$server,$crash)) { if ($process -and !$process.HasExited) { Stop-Process -Id $process.Id -Force } }
}
