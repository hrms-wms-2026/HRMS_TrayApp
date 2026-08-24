# install-service.ps1 — publishes ONEVO.Agent.Service (Release) and registers it
# as a real Windows Service (auto-start on boot, restarts on crash).
#
# Must be run as Administrator. Re-running is safe (stops/replaces any prior install).
#
# Usage (from repo root, elevated PowerShell):
#   powershell -ExecutionPolicy Bypass -File .\scripts\install-service.ps1

param(
    [string]$InstallDir  = "$env:ProgramFiles\ONEVO\AgentService",
    [string]$ServiceName = "ONEVO Agent Service"
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "Run this script as Administrator (right-click PowerShell -> Run as administrator)." -ForegroundColor Red
    exit 1
}

# ── 1. Stop/remove any prior install, and kill any dev-mode process holding the pipe/files ──
$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "Stopping existing service..." -ForegroundColor Yellow
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    sc.exe delete "$ServiceName" | Out-Null
    Start-Sleep -Seconds 2
}
Stop-Process -Name "ONEVO.Agent.Service" -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 1

# ── 2. Publish Release build (framework-dependent — target machine needs the .NET 10 Desktop Runtime) ──
Write-Host "Publishing Release build to $InstallDir ..." -ForegroundColor Cyan
dotnet publish "$Root\ONEVO.Agent.Service\ONEVO.Agent.Service.csproj" `
    -c Release `
    -r win-x64 `
    --self-contained false `
    -o $InstallDir
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$exePath = Join-Path $InstallDir "ONEVO.Agent.Service.exe"
if (-not (Test-Path $exePath)) {
    Write-Host "Publish did not produce $exePath" -ForegroundColor Red
    exit 1
}

# ── 3. Register with the Service Control Manager ──
Write-Host "Registering Windows Service '$ServiceName'..." -ForegroundColor Cyan
sc.exe create "$ServiceName" binPath= "$exePath" start= auto DisplayName= "$ServiceName" | Out-Null
sc.exe description "$ServiceName" "ONEVO WorkPulse Agent background service - policy sync, activity queue, IPC server for the TrayApp." | Out-Null
# Auto-restart on crash: 5s, then 15s, then 60s; counters reset after 1 day with no failures.
sc.exe failure "$ServiceName" reset= 86400 actions= restart/5000/restart/15000/restart/60000 | Out-Null

# ── 4. Start it ──
Write-Host "Starting service..." -ForegroundColor Green
Start-Service -Name $ServiceName

Get-Service -Name $ServiceName | Format-Table Name, Status, StartType

Write-Host ""
Write-Host "Installed to: $InstallDir" -ForegroundColor Green
Write-Host "Config read from: $InstallDir\appsettings.json (ApiBaseUrl etc.)" -ForegroundColor Gray
Write-Host "Uninstall with: .\scripts\uninstall-service.ps1" -ForegroundColor Gray
