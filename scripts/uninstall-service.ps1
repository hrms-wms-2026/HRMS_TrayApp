# uninstall-service.ps1 — stops and removes the ONEVO Agent Service registered
# by install-service.ps1. Must be run as Administrator.
#
# Usage (from repo root, elevated PowerShell):
#   powershell -ExecutionPolicy Bypass -File .\scripts\uninstall-service.ps1

param(
    [string]$ServiceName = "ONEVO Agent Service"
)

$ErrorActionPreference = "Stop"

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "Run this script as Administrator (right-click PowerShell -> Run as administrator)." -ForegroundColor Red
    exit 1
}

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if (-not $existing) {
    Write-Host "Service '$ServiceName' is not installed." -ForegroundColor Yellow
    exit 0
}

Write-Host "Stopping '$ServiceName'..." -ForegroundColor Yellow
Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue

sc.exe delete "$ServiceName" | Out-Null
Write-Host "Uninstalled." -ForegroundColor Green
