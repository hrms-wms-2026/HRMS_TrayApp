# check-coverage.ps1 - fails when an assembly's coverage drops below its floor (AUD-025).
#
# Expects one results folder per test project, as CI produces with:
#   dotnet test tests/<Project>/<Project>.csproj --collect:"XPlat Code Coverage" --results-directory <ResultsRoot>/<Project>
# Floors live in coverage-thresholds.json at the repo root.

param(
    [Parameter(Mandatory)] [string]$ResultsRoot,
    [string]$ThresholdsFile
)

$ErrorActionPreference = "Stop"
# Resolved here, not as a param default: Windows PowerShell 5.1 leaves $PSScriptRoot empty there.
if (-not $ThresholdsFile) { $ThresholdsFile = Join-Path $PSScriptRoot "..\coverage-thresholds.json" }
$thresholds = Get-Content $ThresholdsFile -Raw | ConvertFrom-Json
$failed = $false
$rows = @()

foreach ($assembly in ($thresholds.PSObject.Properties | Where-Object { $_.Name -notlike "_*" })) {
    $floor = $assembly.Value
    $report = Get-ChildItem (Join-Path $ResultsRoot $floor.testProject) -Recurse -Filter coverage.cobertura.xml -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $report) {
        Write-Host "::error::No coverage report for $($floor.testProject) under $ResultsRoot"
        $failed = $true
        continue
    }

    [xml]$xml = Get-Content $report.FullName
    $package = $xml.coverage.packages.package | Where-Object { $_.name -eq $assembly.Name }
    if (-not $package) {
        Write-Host "::error::$($assembly.Name) not found in $($report.FullName)"
        $failed = $true
        continue
    }

    $line = [math]::Round([double]$package.'line-rate' * 100, 1)
    $branch = [math]::Round([double]$package.'branch-rate' * 100, 1)
    $ok = $line -ge $floor.line -and $branch -ge $floor.branch
    if (-not $ok) {
        Write-Host "::error::$($assembly.Name) coverage $line% line / $branch% branch is below the floor $($floor.line)% / $($floor.branch)%"
        $failed = $true
    }
    $rows += [pscustomobject]@{
        Assembly = $assembly.Name
        Line     = "$line% (floor $($floor.line)%)"
        Branch   = "$branch% (floor $($floor.branch)%)"
        Result   = if ($ok) { "ok" } else { "BELOW FLOOR" }
    }
}

$rows | Format-Table -AutoSize | Out-String | Write-Host

if ($env:GITHUB_STEP_SUMMARY) {
    $summary = @("## Coverage", "", "| Assembly | Line | Branch | Result |", "|---|---|---|---|")
    $summary += $rows | ForEach-Object { "| $($_.Assembly) | $($_.Line) | $($_.Branch) | $($_.Result) |" }
    $summary -join "`n" | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Append -Encoding utf8
}

if ($failed) { exit 1 }
