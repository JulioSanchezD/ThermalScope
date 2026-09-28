param([Parameter(Mandatory)][string]$Destination)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
New-Item -ItemType Directory -Force $Destination | Out-Null
$assets = Get-Content (Join-Path $root 'obj\project.assets.json') -Raw | ConvertFrom-Json
$packageFolders = $assets.packageFolders.PSObject.Properties.Name
$packages = @($assets.libraries.PSObject.Properties | Where-Object { $_.Value.type -eq 'package' } | ForEach-Object { $_.Name })
$packages += @($assets.project.frameworks.PSObject.Properties.Value.downloadDependencies | Where-Object { $_.name -match '^Microsoft\.(NETCore|AspNetCore|WindowsDesktop)\.App\.Runtime\.win-x64$' } | ForEach-Object {
    $_.name + '/' + (($_.version -replace '[\[\]\s]', '') -split ',')[0]
})
$index = @('Third-party package inventory. Each component retains its own license.', '')
foreach ($name in ($packages | Select-Object -Unique)) {
    $path = $null
    foreach ($folder in $packageFolders) { $candidate = Join-Path $folder $name.ToLowerInvariant(); if (Test-Path $candidate) { $path = $candidate; break } }
    if (!$path) { throw "Cannot find restored dependency $name." }
    $dest = Join-Path $Destination ($name.Replace('/','-'))
    New-Item -ItemType Directory -Force $dest | Out-Null
    # Preserve full license/notice documents and upstream attribution metadata.
    Get-ChildItem $path -File -Recurse | Where-Object { $_.Name -match '^(LICENSE|NOTICE|COPYING|AUTHORS|THIRD.PARTY)' -or $_.Extension -eq '.nuspec' } | ForEach-Object {
        $relative = $_.FullName.Substring($path.Length).TrimStart('\')
        $target = Join-Path $dest $relative
        New-Item -ItemType Directory -Force (Split-Path $target -Parent) | Out-Null
        Copy-Item $_.FullName $target -Force
    }
    $index += $name
    $manifest = Get-ChildItem $path -Filter '*.nuspec' -File | Select-Object -First 1
    if ($manifest) {
        [xml]$metadata = Get-Content $manifest.FullName -Raw
        $repo = $metadata.package.metadata.repository
        if ($repo.url) { $index += ('  Source: ' + $repo.url + $(if ($repo.commit) { ' (commit ' + $repo.commit + ')' })) }
        elseif ($metadata.package.metadata.projectUrl) { $index += ('  Project: ' + $metadata.package.metadata.projectUrl) }
    }
}
# LibreHardwareMonitor's exact tagged source includes additional embedded-library licenses.
# Extract only its license documents, never executable files, into the installer payload.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archivePath = Join-Path $root 'tools\LibreHardwareMonitor-v0.9.6.zip'
if (!(Test-Path $archivePath)) {
    Invoke-WebRequest 'https://codeload.github.com/LibreHardwareMonitor/LibreHardwareMonitor/zip/refs/tags/v0.9.6' -UseBasicParsing -OutFile $archivePath
}
$archive = [IO.Compression.ZipFile]::OpenRead($archivePath)
try {
    foreach ($entry in $archive.Entries) {
        if ($entry.FullName -match '^LibreHardwareMonitor-0\.9\.6/(LICENSE|THIRD-PARTY-NOTICES\.txt|Aga\.Controls/license\.txt|LibreHardwareMonitorLib/Resources/PawnIo/(COPYING|README))$' -and $entry.Length -gt 0) {
            $relative = $entry.FullName.Substring('LibreHardwareMonitor-0.9.6/'.Length).Replace('/','\')
            $target = Join-Path $Destination ('LibreHardwareMonitor-source-notices\' + $relative)
            New-Item -ItemType Directory -Force (Split-Path $target -Parent) | Out-Null
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $true)
        }
    }
} finally { $archive.Dispose() }
$rawLicense = Join-Path $Destination 'SQLitePCLRaw-Apache-2.0.txt'
if (!(Test-Path $rawLicense)) { Invoke-WebRequest 'https://raw.githubusercontent.com/ericsink/SQLitePCL.raw/v3.0.5/LICENSE.TXT' -UseBasicParsing -OutFile $rawLicense }
$monoLicense = Join-Path $Destination 'Mono-LICENSE.txt'
if (!(Test-Path $monoLicense)) { Invoke-WebRequest 'https://raw.githubusercontent.com/mono/mono/mono-5.4.0.201/LICENSE' -UseBasicParsing -OutFile $monoLicense }
# Include the exact source release of the LGPL modules embedded by LHM.
$modulesSource = Join-Path $Destination 'PawnIO.Modules-0.1.6-source.zip'
if (!(Test-Path $modulesSource)) { Invoke-WebRequest 'https://codeload.github.com/namazso/PawnIO.Modules/zip/refs/tags/0.1.6' -UseBasicParsing -OutFile $modulesSource }
$index += '', 'LibreHardwareMonitorLib 0.9.6 is used unmodified. Corresponding source:', 'https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/tree/v0.9.6', 'https://codeload.github.com/LibreHardwareMonitor/LibreHardwareMonitor/zip/refs/tags/v0.9.6', '', 'See THIRD-PARTY-NOTICES.md and each package .nuspec for project/source references.'
$index | Set-Content (Join-Path $Destination 'INDEX.txt') -Encoding UTF8
