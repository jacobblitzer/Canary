<#
.SYNOPSIS
  Capture everything needed to judge whether THIS machine is correctly set up, as one bundle.

.DESCRIPTION
  Deployment campaign, Stage A1. The QC step is run by someone standing at an unfamiliar
  machine, and until now it meant remembering four commands in the right order and knowing
  which output mattered. A verification procedure that is a memory test is one that gets
  done differently every time, or skipped.

  This runs, per workload:
    1. canary doctor      - can this machine run the content it has? (launches nothing)
    2. canary env         - what does the application ACTUALLY have loaded? (launches the app)
  and copies each environment.json into one dated bundle with a summary.

  It NEVER repairs anything. It reports and stops. The one time this campaign touched an
  install decision, it was the operator who fixed an unregistered plug-in in Developer
  Settings; a script that "helpfully" registered it somewhere else would have hidden the
  question rather than answered it.

  Take the bundle to a known-good machine and run:
      canary env --workload <w> --diff <bundle>\<w>.environment.json

.PARAMETER Workloads
  Workloads to capture. Defaults to every workload that has a workload.json.

.PARAMETER OutDir
  Where to write the bundle. Defaults to .\qc-<machine>-<timestamp>\ beside the repo.

.PARAMETER NoLaunch
  Skip the `canary env` probe: doctor only, no application is started. Use on a machine
  where the target app cannot run yet - you still get the offline half.

.EXAMPLE
  powershell -File scripts\qc-capture.ps1
  powershell -File scripts\qc-capture.ps1 -Workloads rhino -NoLaunch
#>
[CmdletBinding()]
param(
    [string[]] $Workloads,
    [string]   $OutDir,
    [switch]   $NoLaunch
)

$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
$canary = Join-Path $repo 'src\Canary.Harness\bin\Debug\net8.0-windows\canary.exe'
if (-not (Test-Path $canary)) {
    $canary = Join-Path $repo 'src\Canary.Harness\bin\Release\net8.0-windows\canary.exe'
}
if (-not (Test-Path $canary)) {
    throw "canary.exe not found under $repo\src\Canary.Harness\bin. Build first: dotnet build Canary.sln"
}

$workloadsRoot = Join-Path $repo 'workloads'
if (-not $Workloads -or $Workloads.Count -eq 0) {
    $Workloads = Get-ChildItem $workloadsRoot -Directory -ErrorAction SilentlyContinue |
        Where-Object { Test-Path (Join-Path $_.FullName 'workload.json') } |
        Select-Object -ExpandProperty Name
}
if (-not $Workloads -or $Workloads.Count -eq 0) { throw "no workloads found under $workloadsRoot" }

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
if (-not $OutDir) { $OutDir = Join-Path $repo ("qc-{0}-{1}" -f $env:COMPUTERNAME, $stamp) }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$summary = [System.Collections.Generic.List[object]]::new()

Write-Host ""
Write-Host "QC capture on $env:COMPUTERNAME" -ForegroundColor Cyan
Write-Host "  canary   : $canary"
Write-Host "  workloads: $($Workloads -join ', ')"
Write-Host "  bundle   : $OutDir"
Write-Host ""

foreach ($w in $Workloads) {
    Write-Host "--- $w" -ForegroundColor Cyan

    # 1. doctor. Judge by EXIT CODE, never by scraping the text - a native call's stderr
    #    is noise here, and this campaign has already been bitten by reading words instead
    #    of codes.
    $docOut = & $canary doctor --workload $w 2>&1 | Out-String
    $docExit = $LASTEXITCODE
    $docOut | Set-Content -Path (Join-Path $OutDir "$w.doctor.txt") -Encoding utf8
    $verdict = if ($docExit -eq 0) { 'OK' } else { "FAILED (exit $docExit)" }
    $color = if ($docExit -eq 0) { 'Green' } else { 'Red' }
    Write-Host "    doctor : $verdict" -ForegroundColor $color

    # 2. env. Launches the app, asks one question, closes it. Runs even when doctor failed:
    #    a machine that cannot run the content is exactly the one whose environment you
    #    most want recorded.
    $envExit = $null
    if (-not $NoLaunch) {
        $envOut = & $canary env --workload $w 2>&1 | Out-String
        $envExit = $LASTEXITCODE
        $envOut | Set-Content -Path (Join-Path $OutDir "$w.env.txt") -Encoding utf8

        $src = Join-Path $workloadsRoot "$w\results\environment.json"
        if (Test-Path $src) {
            Copy-Item $src (Join-Path $OutDir "$w.environment.json") -Force
            Write-Host "    env    : captured" -ForegroundColor Green
        } else {
            Write-Host "    env    : NO CAPTURE WRITTEN (exit $envExit)" -ForegroundColor Yellow
        }
    } else {
        Write-Host "    env    : skipped (-NoLaunch)" -ForegroundColor DarkGray
    }

    $summary.Add([pscustomobject]@{
        workload    = $w
        doctorExit  = $docExit
        envExit     = $envExit
        captured    = (Test-Path (Join-Path $OutDir "$w.environment.json"))
    })
}

$meta = [pscustomobject]@{
    machine     = $env:COMPUTERNAME
    user        = $env:USERNAME
    capturedUtc = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    os          = [System.Environment]::OSVersion.VersionString
    canary      = $canary
    noLaunch    = [bool]$NoLaunch
    workloads   = $summary
}
$meta | ConvertTo-Json -Depth 5 | Set-Content -Path (Join-Path $OutDir 'qc-summary.json') -Encoding utf8

Write-Host ""
$failed = @($summary | Where-Object { $_.doctorExit -ne 0 })
if ($failed.Count -gt 0) {
    Write-Host "$($failed.Count) workload(s) FAILED doctor: $($failed.workload -join ', ')" -ForegroundColor Red
} else {
    Write-Host "doctor passed on every workload." -ForegroundColor Green
}
Write-Host "Bundle written to $OutDir"
Write-Host ""
Write-Host "Next, on a known-good machine:" -ForegroundColor Cyan
foreach ($s in $summary | Where-Object { $_.captured }) {
    Write-Host "  canary env --workload $($s.workload) --diff `"$OutDir\$($s.workload).environment.json`""
}

# Exit non-zero if any workload failed doctor, so this is usable as a gate.
if ($failed.Count -gt 0) { exit 1 }
exit 0
