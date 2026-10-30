# check-test-isolation.ps1 - fails when a test run changed the real agent data folders (UAT-20).
#
# Tests must redirect every ONEVO data path (AUD-017, AUD-034, AUD-036), but each redirect covers
# one path; a path added later is not covered automatically. CI records the real folders before
# the tests and compares them afterwards:
#   ./scripts/check-test-isolation.ps1 -Snapshot <file>
#   ./scripts/check-test-isolation.ps1 -Compare <file>
# -Roots replaces the folders that are checked (the CI self-check uses a temp folder).
# -Plain prints findings without GitHub error annotations (for that self-check).

param(
    [string]$Snapshot,
    [string]$Compare,
    [string[]]$Roots,
    [switch]$Plain
)

$ErrorActionPreference = "Stop"
if ([bool]$Snapshot -eq [bool]$Compare) { throw "Pass exactly one of -Snapshot or -Compare." }
# Resolved here, not as a param default, so the environment is read when the script runs.
if (-not $Roots) {
    $Roots = @(
        (Join-Path $env:ProgramData "ONEVO"),
        (Join-Path $env:LOCALAPPDATA "ONEVO")
    )
}

# One line per folder and per file (size + last write), sorted, so two states compare line by line.
function Get-State([string[]]$roots) {
    $entries = foreach ($root in $roots) {
        if (Test-Path -LiteralPath $root) {
            "D|$root"
            Get-ChildItem -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue | ForEach-Object {
                if ($_.PSIsContainer) { "D|$($_.FullName)" }
                else { "F|$($_.FullName)|$($_.Length)|$($_.LastWriteTimeUtc.Ticks)" }
            }
        }
    }
    $entries | Sort-Object
}

if ($Snapshot) {
    # @() and WriteAllLines, not Set-Content: a function's empty or one-line output is not an array.
    $state = [string[]]@(Get-State $Roots)
    [System.IO.File]::WriteAllLines($Snapshot, $state)
    Write-Host "Recorded $($state.Count) entries under: $($Roots -join ', ')"
    exit 0
}

$before = [string[]]@([System.IO.File]::ReadAllLines($Compare) | Where-Object { $_ })
$after = [string[]]@(Get-State $Roots)
$beforeSet = [System.Collections.Generic.HashSet[string]]::new($before)
$afterSet = [System.Collections.Generic.HashSet[string]]::new($after)

$findings = @($after | Where-Object { -not $beforeSet.Contains($_) } | ForEach-Object { "new or changed: $_" }) +
    @($before | Where-Object { -not $afterSet.Contains($_) } | ForEach-Object { "removed or changed: $_" })

if ($findings.Count -gt 0) {
    $prefix = if ($Plain) { "" } else { "::error::" }
    foreach ($finding in $findings) { Write-Host "${prefix}Test run touched real agent data - $finding" }
    exit 1
}

Write-Host "Real agent data folders unchanged: $($Roots -join ', ')"
