$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$output = Join-Path $root ('tools\installer-ui-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $output | Out-Null
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes,System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class SetupWindowCapture {
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr handle, out Rect rect);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr handle);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam);
}
'@
$setup = Start-Process (Join-Path $root 'dist\ThermalScope-Setup-Test.exe') -PassThru
$wizardProcess = $null; $checks = @()
function Nodes {
    return $script:window.FindAll([Windows.Automation.TreeScope]::Descendants, [Windows.Automation.Condition]::TrueCondition)
}
function ClickButton($name) {
    $button = Nodes | Where-Object { $_.Current.Name -match ('^' + $name + '$') } | Select-Object -First 1
    if (!$button) { throw "Cannot find wizard button $name. Controls: $((Nodes | ForEach-Object { $_.Current.Name }) -join ' | ')" }
    if ($button.Current.NativeWindowHandle -ne 0) {
        $null = [SetupWindowCapture]::PostMessage([IntPtr]$button.Current.NativeWindowHandle, 0xF5, [IntPtr]::Zero, [IntPtr]::Zero)
    } else { $button.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke() }
    Start-Sleep -Milliseconds 350
}
function Screenshot($name) {
    $rect = New-Object SetupWindowCapture+Rect
    $null = [SetupWindowCapture]::SetForegroundWindow($wizardProcess.MainWindowHandle)
    Start-Sleep -Milliseconds 250
    $null = [SetupWindowCapture]::GetWindowRect($wizardProcess.MainWindowHandle, [ref]$rect)
    $bitmap = New-Object Drawing.Bitmap(($rect.Right-$rect.Left),($rect.Bottom-$rect.Top))
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try { $graphics.CopyFromScreen($rect.Left,$rect.Top,0,0,$bitmap.Size); $bitmap.Save((Join-Path $output $name), [Drawing.Imaging.ImageFormat]::Png) }
    finally { $graphics.Dispose(); $bitmap.Dispose() }
}
try {
    for ($i=0; $i -lt 80; $i++) {
        $wizardProcess = Get-Process | Where-Object { $_.ProcessName -like 'ThermalScope-Setup-Test*' -and $_.MainWindowHandle -ne [IntPtr]::Zero } | Select-Object -First 1
        if ($wizardProcess) { break }; Start-Sleep -Milliseconds 250
    }
    if (!$wizardProcess) { throw 'Setup wizard did not appear.' }
    $script:window = [Windows.Automation.AutomationElement]::FromHandle($wizardProcess.MainWindowHandle)
    Screenshot '01-welcome.png'; ClickButton 'Next'
    Screenshot '02-license.png'
    $accept = Nodes | Where-Object { $_.Current.Name -match '^I accept' } | Select-Object -First 1
    if (!$accept) { throw 'License acceptance control is missing.' }
    $null = [SetupWindowCapture]::PostMessage([IntPtr]$accept.Current.NativeWindowHandle, 0xF5, [IntPtr]::Zero, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 200
    ClickButton 'Next'; Screenshot '03-information.png'; ClickButton 'Next'
    Screenshot '04-destination.png'
    $paths = @(Nodes | Where-Object { $_.Current.ClassName -eq 'TNewPathEdit' } | ForEach-Object { $_.Current.Name })
    if ($paths -notcontains (Join-Path $env:ProgramFiles 'ThermalScope Setup Test')) { throw "Destination page did not default to Program Files: $paths" }
    $checks += 'Interactive wizard defaults to Program Files'
    ClickButton 'Next'; Screenshot '05-options.png'
    $checks += 'Welcome, MIT license, information, destination and optional-task pages are navigable'
    ClickButton 'Cancel'
    for ($i=0; $i -lt 20 -and !$wizardProcess.HasExited; $i++) {
        $condition = New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::ProcessIdProperty, $wizardProcess.Id)
        $owned = [Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Descendants, $condition)
        $yes = $owned | Where-Object { $_.Current.Name -eq 'Yes' } | Select-Object -First 1
        if ($yes) { $null = [SetupWindowCapture]::PostMessage([IntPtr]$yes.Current.NativeWindowHandle, 0xF5, [IntPtr]::Zero, [IntPtr]::Zero); break }
        Start-Sleep -Milliseconds 200
    }
    if (!$wizardProcess.WaitForExit(5000)) { throw 'Cancelling the setup wizard did not close it.' }
    $checks += 'Cancel exits the wizard before installing anything'
    @{passed=$true;checks=$checks;artifacts=$output} | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $output 'wizard-checks.json') -Encoding UTF8
    Write-Host "Wizard UI checks passed. Artifacts: $output" -ForegroundColor Green
} catch {
    Nodes | ForEach-Object { @{name=$_.Current.Name;type=$_.Current.ControlType.ProgrammaticName;class=$_.Current.ClassName;handle=$_.Current.NativeWindowHandle} } | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $output 'controls.json') -Encoding UTF8
    @{passed=$false;error=$_.ToString();details=$_.ScriptStackTrace} | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $output 'wizard-checks.json') -Encoding UTF8
    throw
} finally {
    # Never click Install: this check only inspects the wizard UI before any changes.
    if ($wizardProcess -and !$wizardProcess.HasExited) { Stop-Process -Id $wizardProcess.Id -Force }
    if (!$setup.HasExited) { $null = $setup.WaitForExit(3000) }
}
