# Stop old Agent Service / Tray (fixes DLL lock MSB3027), then rebuild + start Service.
# Usage (from repo root):
#   powershell -ExecutionPolicy Bypass -File .\scripts\restart-agent.ps1
#   powershell -ExecutionPolicy Bypass -File .\scripts\restart-agent.ps1 -WithTray

param(
    [switch]$WithTray
)

$ErrorActionPreference = "Continue"
$Root = Split-Path -Parent $PSScriptRoot
Set-Location $Root

# Service reads appsettings.Development.json (ApiBaseUrl etc.) only under this environment;
# without it, Program.cs's OptionsValidation throws "ApiBaseUrl must be set" on startup.
$env:DOTNET_ENVIRONMENT = "Development"

Write-Host "Stopping ONEVO.Agent.Service / ONEVO.Agent.TrayApp ..." -ForegroundColor Yellow
Stop-Process -Name "ONEVO.Agent.Service" -Force -ErrorAction SilentlyContinue
Stop-Process -Name "ONEVO.Agent.TrayApp" -Force -ErrorAction SilentlyContinue
taskkill /F /IM ONEVO.Agent.Service.exe 2>$null | Out-Null
taskkill /F /IM ONEVO.Agent.TrayApp.exe 2>$null | Out-Null
Start-Sleep -Seconds 2

$left = Get-Process -Name "ONEVO.Agent.Service","ONEVO.Agent.TrayApp" -ErrorAction SilentlyContinue
if ($left) {
    Write-Host "Still running:" -ForegroundColor Red
    $left | Format-Table Id, ProcessName
    exit 1
}

Write-Host "Build Service..." -ForegroundColor Cyan
dotnet build "$Root\ONEVO.Agent.Service\ONEVO.Agent.Service.csproj" -c Debug --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Start Service (new window)..." -ForegroundColor Green
Start-Process -FilePath "dotnet" `
    -ArgumentList @("run","--project","$Root\ONEVO.Agent.Service\ONEVO.Agent.Service.csproj","-c","Debug","--no-build") `
    -WorkingDirectory $Root `
    -WindowStyle Normal

if ($WithTray) {
    Start-Sleep -Seconds 3
    Write-Host "Build + start Tray..." -ForegroundColor Green
    dotnet build "$Root\ONEVO.Agent.TrayApp\ONEVO.Agent.TrayApp.csproj" -c Debug --nologo
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    $exe = Get-ChildItem "$Root\ONEVO.Agent.TrayApp\bin\Debug" -Recurse -Filter "ONEVO.Agent.TrayApp.exe" |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($exe) { Start-Process -FilePath $exe.FullName }
}

Write-Host "Done." -ForegroundColor Green
