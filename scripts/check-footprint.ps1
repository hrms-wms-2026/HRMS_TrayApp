# check-footprint.ps1 - measures the agent's idle CPU and memory against footprint-budgets.json (T10).
#
# Starts the built Service (console) and Tray, waits, samples both every few seconds, prints a
# table and exits 1 if any budget is exceeded. Monitoring stays OFF (ForceMonitoringActive and
# AllowLocalLifecycleWithoutFullGates are forced false), so no collectors run and nothing is
# written to the activity buffer. Run it on a quiet machine with no other agent running:
#
#   dotnet build ONEVO.Agent.Service/ONEVO.Agent.Service.csproj -c Release
#   dotnet build ONEVO.Agent.TrayApp/ONEVO.Agent.TrayApp.csproj -c Release
#   .\scripts\check-footprint.ps1
#
# A Tray window opens while it runs. Uses Development only so a non-installed Tray may connect
# (AllowIpcClientFromAnyPath).

param(
    [string]$Configuration = "Release",
    [int]$WarmupSeconds = 60,
    [int]$DurationSeconds = 300,
    [int]$IntervalSeconds = 5,
    [string]$BudgetsFile,
    [string]$OutCsv
)

$ErrorActionPreference = "Stop"
$repo = Split-Path $PSScriptRoot -Parent
if (-not $BudgetsFile) { $BudgetsFile = Join-Path $repo "footprint-budgets.json" }
$budgets = Get-Content $BudgetsFile -Raw | ConvertFrom-Json

$serviceExe = Get-ChildItem "$repo\ONEVO.Agent.Service\bin\$Configuration" -Recurse -Filter ONEVO.Agent.Service.exe -ErrorAction SilentlyContinue | Select-Object -First 1
$trayExe = Get-ChildItem "$repo\ONEVO.Agent.TrayApp\bin\$Configuration" -Recurse -Filter ONEVO.Agent.TrayApp.exe -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $serviceExe -or -not $trayExe) { throw "Build Service and TrayApp in $Configuration first (see the header of this script)." }
if (Get-Process ONEVO.Agent.Service, ONEVO.Agent.TrayApp -ErrorAction SilentlyContinue) {
    throw "An ONEVO agent process is already running; stop it so it is not measured or disturbed."
}

$cores = (Get-CimInstance Win32_Processor | Measure-Object NumberOfLogicalProcessors -Sum).Sum
$env:DOTNET_ENVIRONMENT = "Development"
$env:Logging__LogLevel__Default = "Information"
$env:Logging__LogLevel__ONEVO = "Information"
$env:Agent__ForceMonitoringActive = "false"
$env:Agent__AllowLocalLifecycleWithoutFullGates = "false"

$logDir = Join-Path ([IO.Path]::GetTempPath()) "onevo-footprint-$(Get-Date -Format yyyyMMddHHmmss)"
New-Item -ItemType Directory $logDir | Out-Null
$procs = [ordered]@{}
try {
    $procs.Service = Start-Process $serviceExe.FullName -WorkingDirectory $serviceExe.DirectoryName -PassThru -WindowStyle Hidden `
        -RedirectStandardOutput "$logDir\service-out.log" -RedirectStandardError "$logDir\service-err.log"
    Start-Sleep 20
    $procs.Tray = Start-Process $trayExe.FullName -WorkingDirectory $trayExe.DirectoryName -PassThru
    Write-Host "Warming up $WarmupSeconds s, then sampling for $DurationSeconds s..."
    Start-Sleep $WarmupSeconds

    $prevCpu = @{}
    foreach ($name in $procs.Keys) { $procs[$name].Refresh(); $prevCpu[$name] = $procs[$name].TotalProcessorTime.TotalMilliseconds }
    $prevTime = Get-Date
    $samples = @()
    $end = (Get-Date).AddSeconds($DurationSeconds)
    while ((Get-Date) -lt $end) {
        Start-Sleep $IntervalSeconds
        $now = Get-Date
        $elapsed = ($now - $prevTime).TotalMilliseconds
        foreach ($name in $procs.Keys) {
            $p = $procs[$name]
            if ($p.HasExited) { throw "$name exited during measurement (exit code $($p.ExitCode)); logs in $logDir" }
            $p.Refresh()
            $cpu = $p.TotalProcessorTime.TotalMilliseconds
            $samples += [pscustomobject]@{
                Time = $now.ToString("HH:mm:ss"); Process = $name
                CpuPercent = ($cpu - $prevCpu[$name]) / $elapsed / $cores * 100
                WorkingSetMB = $p.WorkingSet64 / 1MB
                PrivateMB = $p.PrivateMemorySize64 / 1MB
                Handles = $p.HandleCount
            }
            $prevCpu[$name] = $cpu
        }
        $prevTime = $now
    }
}
finally {
    foreach ($p in $procs.Values) { if ($p -and -not $p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue } }
}

if ($OutCsv) { $samples | Export-Csv $OutCsv -NoTypeInformation }

$failed = $false
$rows = foreach ($name in $procs.Keys) {
    $s = $samples | Where-Object Process -eq $name
    $budget = $budgets.processes.$name
    # Median working set/private: robust to one-off spikes; the max is reported alongside.
    $ws = ($s.WorkingSetMB | Sort-Object)[[int][math]::Floor($s.Count / 2)]
    $pm = ($s.PrivateMB | Sort-Object)[[int][math]::Floor($s.Count / 2)]
    $okWs = $ws -le $budget.workingSetMB
    $okPm = $pm -le $budget.privateMB
    if (-not ($okWs -and $okPm)) { $failed = $true }
    [pscustomobject]@{
        Process      = $name
        CpuAvg       = "{0:N3} %" -f ($s | Measure-Object CpuPercent -Average).Average
        WorkingSetMB = "{0:N1} (max {1:N1}) / {2}" -f $ws, ($s | Measure-Object WorkingSetMB -Maximum).Maximum, $budget.workingSetMB
        PrivateMB    = "{0:N1} (max {1:N1}) / {2}" -f $pm, ($s | Measure-Object PrivateMB -Maximum).Maximum, $budget.privateMB
        Handles      = "{0}-{1}" -f ($s | Measure-Object Handles -Minimum).Minimum, ($s | Measure-Object Handles -Maximum).Maximum
        Result       = if ($okWs -and $okPm) { "ok" } else { "OVER BUDGET" }
    }
}

# Total CPU: sum of both processes per sample time, averaged.
$totalCpu = ($samples | Group-Object Time | ForEach-Object { ($_.Group | Measure-Object CpuPercent -Sum).Sum } | Measure-Object -Average).Average
$okCpu = $totalCpu -le $budgets.totalCpuAvgPercent
if (-not $okCpu) { $failed = $true }

$rows | Format-Table -AutoSize | Out-String | Write-Host
Write-Host ("Total CPU avg {0:N3} % / budget {1} %  {2}" -f $totalCpu, $budgets.totalCpuAvgPercent, $(if ($okCpu) { "ok" } else { "OVER BUDGET" }))
Write-Host "Scenario: $($budgets.scenario). Samples: $($samples.Count / 2) per process. Logs: $logDir"
if ($failed) { Write-Host "Footprint check FAILED"; exit 1 }
Write-Host "Footprint check passed"
