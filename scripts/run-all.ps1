# Launch Backend API + Agent Service + TrayApp together for local end-to-end testing.
# Always kills old processes, rebuilds, then starts fresh (never launches a stale binary).
#
# Usage (from repo root C:\HR or C:\HR\tray_app_maui):
#   powershell -ExecutionPolicy Bypass -File .\tray_app_maui\scripts\run-all.ps1

$ErrorActionPreference = "Continue"

$BackendRoot  = "C:\HR\HRMS-Backend-v1"
$BackendProj  = "$BackendRoot\src\ONEVO.Api\ONEVO.Api.csproj"
$BackendExe   = "$BackendRoot\src\ONEVO.Api\bin\Debug\net10.0\ONEVO.Api.exe"
$TrayRoot     = "C:\HR\tray_app_maui"
$ServiceProj  = "$TrayRoot\ONEVO.Agent.Service\ONEVO.Agent.Service.csproj"
$TrayProj     = "$TrayRoot\ONEVO.Agent.TrayApp\ONEVO.Agent.TrayApp.csproj"
$BackendUrl   = "https://localhost:7229"

$BackendOut = "$BackendRoot\backend-run-out.log"
$BackendErr = "$BackendRoot\backend-run-err.log"
$ServiceOut = "$TrayRoot\service-debug-out.log"
$ServiceErr = "$TrayRoot\service-debug-err.log"

function Write-Step($msg) { Write-Host "`n=== $msg ===" -ForegroundColor Cyan }
function Write-Ok($msg)   { Write-Host "[OK] $msg" -ForegroundColor Green }
function Write-Fail($msg) { Write-Host "[FAIL] $msg" -ForegroundColor Red }

$result = @{ Backend = $false; Service = $false; Tray = $false }

# ---------------------------------------------------------------------------
Write-Step "Stopping old processes"

Stop-Process -Name "ONEVO.Agent.Service" -Force -ErrorAction SilentlyContinue
Stop-Process -Name "ONEVO.Agent.TrayApp" -Force -ErrorAction SilentlyContinue
Stop-Process -Name "ONEVO.Api" -Force -ErrorAction SilentlyContinue
taskkill /F /IM ONEVO.Agent.Service.exe 2>$null | Out-Null
taskkill /F /IM ONEVO.Agent.TrayApp.exe 2>$null | Out-Null
taskkill /F /IM ONEVO.Api.exe 2>$null | Out-Null

# Verify port 7229 is actually free before moving on - Stop-Process/taskkill return
# before the OS always finishes releasing the socket, and starting a new backend while
# the old one is still mid-shutdown throws "address already in use".
$portFreeDeadline = (Get-Date).AddSeconds(15)
do {
    $stillBound = Get-NetTCPConnection -LocalPort 7229 -State Listen -ErrorAction SilentlyContinue
    if ($stillBound) {
        foreach ($c in $stillBound) {
            $p = Get-Process -Id $c.OwningProcess -ErrorAction SilentlyContinue
            if ($p) {
                Write-Host "Killing stray process on port 7229: $($p.ProcessName) (PID $($p.Id))" -ForegroundColor Yellow
                Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
            }
        }
        Start-Sleep -Seconds 1
    }
} while ($stillBound -and (Get-Date) -lt $portFreeDeadline)

Start-Sleep -Seconds 2

# ---------------------------------------------------------------------------
Write-Step "Building Backend API"
dotnet build $BackendProj -c Debug --nologo
if ($LASTEXITCODE -ne 0) { Write-Fail "Backend build failed"; exit 1 }
Write-Ok "Backend build succeeded"

Write-Step "Building Agent Service"
dotnet build $ServiceProj -c Debug --nologo
if ($LASTEXITCODE -ne 0) { Write-Fail "Agent Service build failed"; exit 1 }
Write-Ok "Agent Service build succeeded"

Write-Step "Building TrayApp"
dotnet build $TrayProj -c Debug --nologo
if ($LASTEXITCODE -ne 0) { Write-Fail "TrayApp build failed"; exit 1 }
Write-Ok "TrayApp build succeeded"

# ---------------------------------------------------------------------------
Write-Step "Starting Backend API"
# Launch the built apphost exe directly rather than "dotnet run" - running through the
# dotnet muxer here was observed spawning a second bind attempt that raced the real one
# and logged a spurious "address already in use" crash even though the actual backend
# came up fine a moment later. Running the exe directly is a single, predictable process.
# launchSettings.json's "https" profile is only auto-applied by `dotnet run`/the IDE, not
# by running the exe directly, so its settings are replicated here via env vars.
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:ASPNETCORE_URLS = $BackendUrl
$backendProc = Start-Process -FilePath $BackendExe `
    -WorkingDirectory "$BackendRoot\src\ONEVO.Api" `
    -RedirectStandardOutput $BackendOut `
    -RedirectStandardError $BackendErr `
    -WindowStyle Hidden `
    -PassThru

# -SkipCertificateCheck is a PowerShell 7+ only parameter for Invoke-WebRequest.
# Windows PowerShell 5.1 (the shell this script actually runs under here) doesn't have
# it, and passing it throws a parameter-binding error on every call - which silently
# looks identical to "backend not up yet" if wrapped in try/catch. Use the
# ServicePointManager override instead, which works on 5.1.
[System.Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }

# Cold start (EF Core DI for 252 tables) has been observed taking well over 150s under
# load, which previously made this loop give up and report DOWN while the process was
# still genuinely starting. That false "DOWN" then led to a second run-all.ps1 launching
# a competing instance, which crashed with AddressInUseException once the slow original
# finally finished binding. Waiting longer here (and bailing immediately if the process
# has actually died, instead of polling out the full budget) avoids both failure modes.
Write-Host "Waiting for backend at $BackendUrl (cold start can take a while: EF Core + DI container for 252 tables) ..."
$backendUp = $false
$backendCrashed = $false
for ($i = 0; $i -lt 240; $i++) {
    if ($backendProc.HasExited) { $backendCrashed = $true; break }
    try {
        $resp = Invoke-WebRequest -Uri "$BackendUrl/swagger" -UseBasicParsing -TimeoutSec 2 -ErrorAction Stop
        if ($resp.StatusCode -lt 500) { $backendUp = $true; break }
    } catch {}
    Start-Sleep -Seconds 1
}
if ($backendUp) {
    Write-Ok "Backend responding at $BackendUrl (took $($i+1)s)"
    $result.Backend = $true
} elseif ($backendCrashed) {
    Write-Fail "Backend process exited after ${i}s (PID $($backendProc.Id)) - check $BackendErr"
} else {
    Write-Fail "Backend did not respond after 240s - check $BackendErr"
}

# ---------------------------------------------------------------------------
Write-Step "Starting Agent Service"
# Same reasoning as Backend above: launch the built apphost exe directly instead of
# "dotnet run", which was observed producing an extra transient process alongside the
# real one.
$ServiceExe = "$TrayRoot\ONEVO.Agent.Service\bin\Debug\net10.0-windows\ONEVO.Agent.Service.exe"
$env:DOTNET_ENVIRONMENT = "Development"
Start-Process -FilePath $ServiceExe `
    -WorkingDirectory "$TrayRoot\ONEVO.Agent.Service" `
    -RedirectStandardOutput $ServiceOut `
    -RedirectStandardError $ServiceErr `
    -WindowStyle Hidden

Start-Sleep -Seconds 4
$svc = Get-Process -Name "ONEVO.Agent.Service" -ErrorAction SilentlyContinue
if ($svc) {
    $svcId = ($svc | Select-Object -First 1).Id
    Write-Ok "Agent Service running (PID $svcId)"
    $result.Service = $true
} else {
    Write-Fail "Agent Service did not stay running - check $ServiceErr"
}

# ---------------------------------------------------------------------------
Write-Step "Starting TrayApp"
# Both ASPNETCORE_ENVIRONMENT (set above for Backend) and DOTNET_ENVIRONMENT (set above for Agent
# Service) are still present in this shell and would otherwise leak into TrayApp's process
# environment. MAUI's CreateBuilder() strips the "ASPNETCORE_"/"DOTNET_" prefix from each and
# collapses both to the same "ENVIRONMENT" config key, throwing ArgumentException on the second
# insert - crashing TrayApp before any collector starts. TrayApp doesn't need either variable.
Remove-Item Env:\ASPNETCORE_ENVIRONMENT -ErrorAction SilentlyContinue
Remove-Item Env:\DOTNET_ENVIRONMENT -ErrorAction SilentlyContinue

$trayExe = Get-ChildItem "$TrayRoot\ONEVO.Agent.TrayApp\bin\Debug" -Recurse -Filter "ONEVO.Agent.TrayApp.exe" -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1

if ($trayExe) {
    Start-Process -FilePath $trayExe.FullName
    Start-Sleep -Seconds 2
    $tray = Get-Process -Name "ONEVO.Agent.TrayApp" -ErrorAction SilentlyContinue
    if ($tray) {
        $trayId = ($tray | Select-Object -First 1).Id
        Write-Ok "TrayApp running (PID $trayId)"
        $result.Tray = $true
    } else {
        Write-Fail "TrayApp process exited immediately after launch"
    }
} else {
    Write-Fail "Could not find built ONEVO.Agent.TrayApp.exe"
}

# ---------------------------------------------------------------------------
Write-Step "Summary"
Write-Host ("Backend API : {0}" -f $(if ($result.Backend) { "UP  ($BackendUrl/swagger)" } else { "DOWN - see $BackendErr" }))
Write-Host ("Agent Service : {0}" -f $(if ($result.Service) { "UP" } else { "DOWN - see $ServiceErr" }))
Write-Host ("TrayApp : {0}" -f $(if ($result.Tray) { "UP" } else { "DOWN" }))

if (-not ($result.Backend -and $result.Service -and $result.Tray)) {
    exit 1
}
