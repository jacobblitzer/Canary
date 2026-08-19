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
    [switch]   $NoLaunch,
    [string]   $CanaryExe,
    [string]   $WorkloadsRoot
)

# DELIBERATELY 'Continue', not 'Stop'.
#
# This script's whole job is to run commands that are EXPECTED to fail on a machine that is
# not set up, and to record how they failed. Under 'Stop', `& $exe ... 2>&1` turns any line a
# native command writes to stderr into a terminating NativeCommandError - so the first
# workload whose doctor complained killed the run and the remaining workloads were never
# captured. A survey that aborts on the first problem is useless on the machine with
# problems. Every native call below is judged by $LASTEXITCODE, which is the only reliable
# signal for an exe, and never by scraping its text.
$ErrorActionPreference = 'Continue'

$repo = Split-Path -Parent $PSScriptRoot

# Find canary. The first version of this script looked ONLY under
# src\Canary.Harness\bin - a DEV TREE path - which meant it could not run on the one kind of
# machine it was written for: a QC box installed the way a stranger would, with no repo and
# no SDK. Search the dev tree, then the Drive payload, then give up with instructions rather
# than a bare "not found".
$canaryCandidates = @()
if ($CanaryExe) { $canaryCandidates += $CanaryExe }
$canaryCandidates += @(
    (Join-Path $repo 'src\Canary.Harness\bin\Debug\net8.0-windows\canary.exe'),
    (Join-Path $repo 'src\Canary.Harness\bin\Release\net8.0-windows\canary.exe'),
    (Join-Path $PSScriptRoot 'canary.exe'),      # payload layout: script sits beside the exe
    'G:\My Drive\Builds\Canary\canary.exe',
    'G:\My Drive\Builds\canary\canary.exe'
)
$canary = $canaryCandidates | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $canary) {
    # The one place a hard stop IS right: with no canary there is nothing to capture.
    throw @"
canary.exe not found. Looked in:
$($canaryCandidates | ForEach-Object { "  $_" } | Out-String)
Get one of:
  - build it here:      dotnet build Canary.sln    (needs the repo + .NET 8 SDK)
  - or point at a copy: -CanaryExe <path to canary.exe>
Run scripts\machine-survey.ps1 first if you are not sure what this machine has.
"@
}

# The workloads root travels with the exe on a payload and sits beside the repo in a dev
# tree, so do not assume the dev layout here either.
if (-not $WorkloadsRoot) {
    $wlCandidates = @(
        (Join-Path $repo 'workloads'),
        (Join-Path (Split-Path -Parent $canary) 'workloads'),
        (Join-Path $PSScriptRoot 'workloads')
    )
    $WorkloadsRoot = $wlCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $WorkloadsRoot) { throw "no workloads directory found; pass -WorkloadsRoot" }
$workloadsRoot = $WorkloadsRoot
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

# 0. Survey the machine BEFORE touching any application. This is the half that still works
#    when nothing else does, and it is what a later setup/reinstall pass reads to decide
#    what to do. Never fatal: a machine too broken to survey is exactly the one worth
#    capturing whatever can be captured from.
$surveyScript = Join-Path $PSScriptRoot 'machine-survey.ps1'
if (Test-Path $surveyScript) {
    try {
        & powershell -NoProfile -ExecutionPolicy Bypass -File $surveyScript `
            -OutFile (Join-Path $OutDir 'machine-survey.json') | Out-Null
        Write-Host "  survey   : machine-survey.json" -ForegroundColor Green
    } catch {
        Write-Host "  survey   : FAILED - $($_.Exception.Message)" -ForegroundColor Yellow
    }
} else {
    Write-Host "  survey   : machine-survey.ps1 not beside this script - skipped" -ForegroundColor Yellow
}
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
