# Registers the custom activation protocol used by the tenant portal.
# The protocol passes only the portal_origin query value to the tray app.
param(
    [Parameter(Mandatory = $true)]
    [string]$TrayExePath,
    [string]$Protocol = 'onexso-workspace'
)

$ErrorActionPreference = 'Stop'

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) { throw 'Run this script as Administrator.' }

$resolvedExe = (Resolve-Path -LiteralPath $TrayExePath -ErrorAction Stop).Path
$root = "Registry::HKEY_LOCAL_MACHINE\Software\Classes\$Protocol"
New-Item -Path $root -Force | Out-Null
New-ItemProperty -Path $root -Name '(Default)' -Value 'URL:ONEVO Workspace Protocol' -PropertyType String -Force | Out-Null
New-ItemProperty -Path $root -Name 'URL Protocol' -Value '' -PropertyType String -Force | Out-Null

$command = Join-Path $root 'shell\open\command'
New-Item -Path $command -Force | Out-Null
$quotedExe = '"' + $resolvedExe.Replace('"', '""') + '"'
New-ItemProperty -Path $command -Name '(Default)' -Value "$quotedExe \"%1\"" -PropertyType String -Force | Out-Null

Write-Host "Registered $Protocol:// to $resolvedExe" -ForegroundColor Green
