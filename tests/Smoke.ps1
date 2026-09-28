$ErrorActionPreference = 'Stop'
[Net.WebRequest]::DefaultWebProxy = $null
$root = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $root 'app\ThermalScope.exe'
$testRoot = Join-Path $root ('tools\smoke-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $testRoot | Out-Null
$base = 'http://127.0.0.1:18099'
$script:server = $null
$checks = 0
function Assert($condition, $message) { if (!$condition) { throw $message }; $script:checks++ }
function Request($path, $body = $null) {
    if ($null -eq $body) { return Invoke-RestMethod ($base + $path) -TimeoutSec 10 }
    return Invoke-RestMethod ($base + $path) -Method Post -ContentType 'application/json' -Body ($body | ConvertTo-Json -Depth 10 -Compress) -TimeoutSec 10
}
function ExpectStatus($path, $body, $status, $origin = $null) {
    try {
        $headers = @{}; if ($origin) { $headers.Origin = $origin }
        Invoke-RestMethod ($base + $path) -Method Post -Headers $headers -ContentType 'application/json' -Body ($body | ConvertTo-Json -Depth 10 -Compress) -TimeoutSec 10 | Out-Null
        throw "Expected HTTP $status for $path"
    } catch {
        if (!$_.Exception.Response) { throw }
        Assert ([int]$_.Exception.Response.StatusCode -eq $status) "Wrong status for $path"
    }
}
function StartServer($label) {
    $script:server = Start-Process $exe -ArgumentList @('--demo','--port','18099','--data-dir',('"' + $testRoot + '"')) -RedirectStandardOutput (Join-Path $testRoot ($label + '.log')) -RedirectStandardError (Join-Path $testRoot ($label + '.err')) -PassThru
    for ($i = 0; $i -lt 30; $i++) {
        Start-Sleep -Milliseconds 500
        if ($script:server.HasExited) { throw 'Demo server exited unexpectedly.' }
        try { $live = Request '/api/live'; if ($live.frame) { return } } catch { }
    }
    throw 'Server startup timed out.'
}
try {
    StartServer 'first'
    $live = Request '/api/live'
    Assert ($live.frame.demo -and $live.frame.metrics.cpuTemp -gt 0 -and $live.frame.metrics.cpuFan -gt 0) 'Demo readings/automatic mapping missing.'
    Assert ((Request '/api/history').Count -ge 1) 'History unavailable.'
    $null = Request '/api/settings' @{mappings=@{cpuFan='demo/cpu/fan'}}
    ExpectStatus '/api/settings' @{mappings=@{cpuFan='demo/cpu/temp'}} 400
    ExpectStatus '/api/settings' @{mappings=@{cpuFan='demo/cpu/fan'}} 403 'http://unrelated.example'
    ExpectStatus '/api/sessions/start' @{name='bad';cooler='Wraith Prism';game='';notes='';minutes=0} 400
    $session = Request '/api/sessions/start' @{name='Automatic stop test';cooler='Wraith Prism';game='Smoke check';notes='isolated test';ambient=24;minutes=1}
    ExpectStatus '/api/sessions/start' @{name='duplicate';cooler='Wraith Prism';game='';notes='';minutes=1} 409
    ExpectStatus '/api/settings' @{mappings=@{cpuFan='demo/cpu/fan'}} 409
    Write-Host 'Checking one-minute automatic recording, without an attached dashboard...'
    Start-Sleep -Seconds 63
    $live = Request '/api/live'
    Assert ($null -eq $live.active) 'Session failed to stop automatically.'
    $detail = Request ('/api/sessions/' + $session.id)
    Assert ($detail.session.status -eq 'completed' -and $detail.frames.Count -ge 55) 'Recorded session incomplete.'
    Assert ($detail.summary.cpuTemp.count -eq $detail.frames.Count -and $detail.summary.cpuTemp.peak -ge $detail.summary.cpuTemp.average) 'Summary is inconsistent.'
    $csv = Invoke-WebRequest ($base + '/api/sessions/' + $session.id + '/csv') -UseBasicParsing
    Assert ($csv.Content.StartsWith('timestamp_utc,elapsed_seconds,cpuTemp_C')) 'CSV header invalid.'
    Assert (($csv.Content -split "`n").Count -ge 56) 'CSV samples missing.'
    $interrupted = Request '/api/sessions/start' @{name='Recovery test';cooler='ARCTIC Liquid Freezer III Pro 240';game='';notes='';minutes=60}
    Start-Sleep -Seconds 3
    Stop-Process -Id $server.Id -Force; $server.WaitForExit(); $script:server = $null
    StartServer 'restart'
    $recovered = Request ('/api/sessions/' + $interrupted.id)
    Assert ($recovered.session.status -eq 'interrupted' -and $recovered.frames.Count -ge 2) 'Crash recovery failed.'
    Assert ((Request '/api/settings').mappings.cpuFan -eq 'demo/cpu/fan') 'Mappings did not persist.'
    $manual = Request '/api/sessions/start' @{name='Manual stop';cooler='Wraith Prism';game='';notes='';minutes=60}
    Start-Sleep -Seconds 2
    $stopped = Request '/api/sessions/stop' @{}
    Assert ($stopped.status -eq 'stopped' -and $stopped.id -eq $manual.id) 'Manual stop failed.'
    $custom = Request '/api/sessions/start' @{name='My arbitrary workload / fan curve';cooler='Any user-supplied cooler';game='Benchmark';notes='';minutes=47}
    Assert ($custom.name -eq 'My arbitrary workload / fan curve' -and $custom.cooler -eq 'Any user-supplied cooler' -and $custom.minutes -eq 47) 'Custom session metadata not preserved.'
    $null = Request '/api/sessions/stop' @{}
    $unnamed = Request '/api/sessions/start' @{name='  ';cooler='';game='';notes='';minutes=1}
    Assert ($unnamed.name -match '^Session \d{4}-\d{2}-\d{2} ' -and $unnamed.cooler -eq '') 'Blank optional labels need a nonempty automatic name.'
    $null = Request '/api/sessions/stop' @{}
    Write-Host "Integration checks passed: $checks. Test artifacts: $testRoot" -ForegroundColor Green
} finally {
    if ($script:server -and !$script:server.HasExited) { Stop-Process -Id $script:server.Id -Force }
}
