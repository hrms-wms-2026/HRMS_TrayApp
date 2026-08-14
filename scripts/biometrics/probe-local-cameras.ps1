# Local Windows camera compatibility probe (Task 0 Steps 4-5 partial)
# Enumerates video capture devices and records labels. Does not save video.
# For full WebView2 + AWS liveness, use TrayApp against staging after Task 1.

$ErrorActionPreference = "Stop"

$cameras = Get-CimInstance Win32_PnPEntity |
    Where-Object { $_.PNPClass -eq 'Camera' -or $_.Name -match 'camera|webcam|integrated' }

$os = Get-CimInstance Win32_OperatingSystem
$machine = @{
    computerName = $env:COMPUTERNAME
    osCaption = $os.Caption
    osVersion = $os.Version
    probeAtUtc = (Get-Date).ToUniversalTime().ToString('o')
}

$virtualPatterns = @('obs virtual', 'snap camera', 'manycam', 'xsplit', 'droidcam', 'epoccam')

$devices = @()
foreach ($cam in $cameras) {
    $label = [string]$cam.Name
    $lower = $label.ToLowerInvariant()
    $isVirtual = $false
    foreach ($p in $virtualPatterns) {
        if ($lower.Contains($p)) { $isVirtual = $true; break }
    }
    $devices += @{
        name = $label
        deviceId = $cam.DeviceID
        isVirtual = $isVirtual
        preferredBuiltIn = ($lower.Contains('integrated') -or $lower.Contains('built-in') -or $lower.Contains('front'))
    }
}

$result = @{
    machine = $machine
    cameraCount = $devices.Count
    devices = $devices
    note = 'Resolution/FPS requires WebView2 getUserMedia during live liveness session (Task 0 Step 5-6).'
}

$outDir = Join-Path $PSScriptRoot '..\..\docs\superpowers\plans'
$outFile = Join-Path $outDir '2026-08-13-camera-local-probe-result.json'
$result | ConvertTo-Json -Depth 6 | Set-Content -Path $outFile -Encoding UTF8

Write-Host "Camera probe complete: $($devices.Count) device(s)"
Write-Host "Written: $outFile"
$result | ConvertTo-Json -Depth 6
