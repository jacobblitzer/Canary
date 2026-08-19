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

  IT ALSO NEVER RUNS THE PAYLOAD WHERE IT SITS. Canary writes its results beside the content
  it ran - under the workload's own results folder - so aiming it straight at the payload on
  Google Drive made every commission and every env probe create directories inside the
  delivered payload. Drive is delivery; local disk is runtime, and this phase is meant to be
  read-only on the payload. So the workloads tree is copied to a scratch folder under
  %LOCALAPPDATA% first, and every canary call is told to use the copy with --workloads-dir.
  That flag is not optional: canary resolves a root from the current directory BEFORE it
  looks anywhere else, so without it canary could write into one tree while this script read
  another - which is exactly how a bundle came out with no commissioning report in it and
  nothing saying why.

  Take the bundle to a known-good machine and run:
      canary env --workload <w> --diff <bundle>\<w>.environment.json

  The bundle is the deliverable, so it delivers itself: when the claude-share folder on the
  Drive is present the finished bundle is copied there, which is where the dev machine looks
  for it. The copy happens once, at the end, from a bundle that is already complete.

.PARAMETER Workloads
  Workloads to capture. Defaults to every workload that has a workload.json.

.PARAMETER OutDir
  Where to write the bundle. Defaults to %LOCALAPPDATA%\Canary\qc-<machine>-<timestamp>\.
  Deliberately NOT on the Drive: the bundle is written to during the run, and a
  half-written folder appearing on a shared Drive is indistinguishable from a finished one.

.PARAMETER NoLaunch
  Skip the `canary env` probe: doctor only, no application is started. Use on a machine
  where the target app cannot run yet - you still get the offline half.

.PARAMETER Publish
  Copy the finished bundle to G:\My Drive\claude-share\qc-<machine>-<date>\. Defaults to ON
  when that folder exists and OFF (saying so, with the path) when it does not. Pass -Publish
  to force the copy on a machine where the check said otherwise.

.PARAMETER NoPublish
  Keep the bundle on this machine. This exists as its own switch because "-Publish:$false"
  cannot be passed through `powershell -File` on Windows PowerShell 5.1 - -File hands every
  argument over as a string, and a switch refuses the string. Every invocation in this
  campaign is a -File invocation, so the negation had to be a switch of its own.

.EXAMPLE
  powershell -File scripts\qc-capture.ps1
  powershell -File scripts\qc-capture.ps1 -Workloads rhino -NoLaunch
  powershell -File scripts\qc-capture.ps1 -NoPublish
#>
[CmdletBinding()]
param(
    [string[]] $Workloads,
    [string]   $OutDir,
    [switch]   $NoLaunch,
    [string]   $CanaryExe,
    [string]   $WorkloadsRoot,
    [string]   $CommissionWith,
    [switch]   $Publish,
    [switch]   $NoPublish
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
# A hand-passed root is checked here rather than at first use: under 'Continue', Resolve-Path
# on a path that is not there writes an error and hands back nothing, and the run would
# stagger on to fail somewhere further down with a message about a directory nobody named.
if (-not (Test-Path $WorkloadsRoot)) { throw "-WorkloadsRoot does not exist: $WorkloadsRoot" }
$sourceWorkloadsRoot = (Resolve-Path $WorkloadsRoot).Path

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'

# RUN AGAINST A COPY, NEVER AGAINST THE PAYLOAD ITSELF.
#
# Canary puts a workload's results inside that workload's own folder, so pointing it at the
# payload on the Drive meant commission and env manufactured directories inside the thing we
# had just delivered - on a folder that syncs, to every machine, while the phase that runs
# this script is supposed to be read-only. Drive is delivery. Local disk is runtime. Copying
# first costs a few seconds and makes that rule true rather than merely stated.
#
# %LOCALAPPDATA% and not %TEMP%: a cleaner emptying TEMP mid-run would delete the tree a
# launched application still has files open in. TEMP is only the fallback for a profile that
# somehow has no LOCALAPPDATA.
$scratchBase = $env:LOCALAPPDATA
if (-not $scratchBase) { $scratchBase = $env:TEMP }
$canaryLocal   = Join-Path $scratchBase 'Canary'
$scratchRoot   = Join-Path $canaryLocal (Join-Path 'qc-scratch' $stamp)
$workloadsRoot = Join-Path $scratchRoot 'workloads'
New-Item -ItemType Directory -Force -Path $workloadsRoot | Out-Null
# Copy the CONTENTS, not the folder. Copy-Item -Recurse of a directory onto a directory that
# already exists nests it one level deeper instead of merging, and a workloads root one level
# down is a root canary will not find.
Copy-Item -Path (Join-Path $sourceWorkloadsRoot '*') -Destination $workloadsRoot -Recurse -Force

# A payload can arrive carrying results from the machine that built it, and
# commissioning-report.json is the one file in there this script lifts into the bundle.
# Delete the copy's inherited one before running, so a report made somewhere else can never
# be read as this machine's answer. Nothing else under a results folder is touched: baselines
# live there, and removing those would silently turn every comparison into a first-run New,
# which is not a failure and so prints as a pass.
$staleReport = Join-Path (Join-Path $workloadsRoot 'commissioning') (Join-Path 'results' 'commissioning-report.json')
if (Test-Path $staleReport) { Remove-Item $staleReport -Force }

if (-not $Workloads -or $Workloads.Count -eq 0) {
    # 'commissioning' is excluded deliberately: it carries no tests and launches no
    # application, so doctoring and env-probing it would add two guaranteed failures to every
    # bundle. It is the subject of step 0.5 below, not one of the workloads under test.
    $Workloads = Get-ChildItem $workloadsRoot -Directory -ErrorAction SilentlyContinue |
        Where-Object { Test-Path (Join-Path $_.FullName 'workload.json') } |
        Select-Object -ExpandProperty Name |
        Where-Object { $_ -ne 'commissioning' }
}
if (-not $Workloads -or $Workloads.Count -eq 0) { throw "no workloads found under $workloadsRoot" }

# The bundle is written to during the run, so it is built on local disk and copied to the
# Drive once, finished. The old default put it beside the repo, which on a QC machine
# resolved onto the payload's own folder on the Drive - a half-written bundle syncing to
# everyone, and unreadable while it grew.
if (-not $OutDir) { $OutDir = Join-Path $canaryLocal ("qc-{0}-{1}" -f $env:COMPUTERNAME, $stamp) }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

# Where a finished bundle goes, and how this run decided. Default ON when the Drive folder is
# actually here, because a return path that lived only as prose in a prompt someone was meant
# to remember is why bundles stayed on the machines that produced them.
#
# Two switches rather than -Publish and -Publish:$false, and that is not a style choice:
# under `powershell -File` on 5.1 every argument arrives as a STRING, so -Publish:$false is
# rejected outright ("Cannot convert value System.String to type SwitchParameter") - measured,
# in all three spellings, $false / false / 0. Every documented invocation here is a -File
# invocation, so the negation has to be a switch of its own or it does not exist.
if ($Publish -and $NoPublish) { throw "-Publish and -NoPublish contradict each other; pass one or neither." }
$publishRoot = Join-Path 'G:\My Drive' 'claude-share'
$publishDest = Join-Path $publishRoot ("qc-{0}-{1}" -f $env:COMPUTERNAME, (Get-Date -Format 'yyyyMMdd'))
$driveThere  = Test-Path $publishRoot
if     ($NoPublish) { $doPublish = $false }
elseif ($Publish)   { $doPublish = $true }
else                { $doPublish = $driveThere }

# The learnings folder ships EMPTY but explains itself. The dev machine's importer reads it,
# and an empty folder with a README says "nobody wrote anything down here" - a missing folder
# says "nobody was ever asked to", which is a different and much worse failure.
$learningsDir = Join-Path $OutDir 'learnings'
New-Item -ItemType Directory -Force -Path $learningsDir | Out-Null
$learningsReadme = @'
# Learnings from this QC run

Anything this machine taught you that the next machine should not have to relearn goes in
here, one file per learning, named:

    YYYY-MM-DD-NNN-slug.md

    YYYY-MM-DD   the day you learned it
    NNN          three digits, 001 upwards within that day
    slug         a few words, lower case, hyphenated

The dev machine's importer reads this folder, so a note left here reaches the next payload.
A note left in your head does not.

Write what was actually observed and what it cost: the command you ran, what it printed,
what you expected instead. "doctor failed" is not importable. "doctor exited 1 with 13
precondition errors, all of them an unexpanded token, because the payload ships no
tokens.json" is.

This folder is created empty on purpose. Empty means nobody wrote anything down. Missing
would have meant nobody was asked to.
'@
Set-Content -Path (Join-Path $learningsDir 'README.md') -Value $learningsReadme -Encoding utf8

$summary = [System.Collections.Generic.List[object]]::new()

Write-Host ""
Write-Host "QC capture on $env:COMPUTERNAME" -ForegroundColor Cyan
Write-Host "  canary   : $canary"
Write-Host "  workloads: $($Workloads -join ', ')"
Write-Host "  source   : $sourceWorkloadsRoot" -ForegroundColor DarkGray
Write-Host "  runtime  : $workloadsRoot" -ForegroundColor DarkGray
Write-Host "             (a copy - canary writes results next to the content it runs, and the" -ForegroundColor DarkGray
Write-Host "              payload is delivery, not a place to run in)" -ForegroundColor DarkGray
Write-Host "  bundle   : $OutDir"
if ($doPublish) {
    Write-Host "  publish  : $publishDest"
} elseif ($driveThere) {
    Write-Host "  publish  : off (-NoPublish) - the bundle stays on this machine" -ForegroundColor Yellow
} else {
    Write-Host "  publish  : off - $publishRoot is not on this machine, so nothing can be" -ForegroundColor Yellow
    Write-Host "             copied there. Carry $OutDir across by hand, or re-run with -Publish" -ForegroundColor Yellow
    Write-Host "             once the Drive is mounted." -ForegroundColor Yellow
}
Write-Host ""

# A file MISSING from a bundle and a file NEVER ASKED FOR look identical from the outside:
# both are an absence. So the absence gets written down, with where it should have been and
# what could have caused it. Reading a bundle is not the moment to start guessing.
function Write-MissingNote {
    param([string] $Path, [string] $Expected, [string] $Why)
    @(
        "MISSING from this bundle.",
        "",
        "expected at : $Expected",
        "why         : $Why",
        "machine     : $env:COMPUTERNAME",
        "recorded    : $((Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ'))",
        "",
        "This note exists so that the file being absent is a recorded fact rather than",
        "something a reader has to notice."
    ) | Set-Content -Path $Path -Encoding utf8
}

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

# 0.5. COMMISSION FIRST. Ruling 7A: this answers "can this machine test at all", and it
#      GATES everything below rather than merely preceding it. If layer 2 fails, no pixel
#      result in this bundle is readable, and knowing that before reading them is the whole
#      point. Layer 1 needs no application, so this produces an answer even on a machine
#      where nothing else runs.
$commissionWorkload = @($Workloads | Where-Object { $_ -eq 'rhino' })[0]
if (-not $commissionWorkload) { $commissionWorkload = @($Workloads)[0] }
if ($CommissionWith) { $commissionWorkload = $CommissionWith }

# --workloads-dir on this and every canary call below, without exception. Canary resolves a
# workloads root from the current directory FIRST, so a call without the flag can run one
# tree while this script reads another - and the only symptom is a bundle quietly short of a
# file, which reads as "the machine did not produce one".
$commissionScope = 'layers 1-3'
$commissionScopeReason = $null
if ($NoLaunch -or -not $commissionWorkload) {
    $commissionScope = 'layer 1 only'
    $commissionScopeReason = if ($NoLaunch) { '-NoLaunch was requested' }
                             else { 'no workload here can supply an application' }
    Write-Host "  commission: layer 1 only - $commissionScopeReason, so no app is started" -ForegroundColor DarkGray
    & $canary commission --workloads-dir $workloadsRoot 2>&1 |
        Out-String | Set-Content -Path (Join-Path $OutDir 'commissioning.txt') -Encoding utf8
} else {
    & $canary commission --workload $commissionWorkload --workloads-dir $workloadsRoot 2>&1 |
        Out-String | Set-Content -Path (Join-Path $OutDir 'commissioning.txt') -Encoding utf8
}
$commissionExit = $LASTEXITCODE
# Built from Join-Path segments rather than one quoted literal. A backslash inside a
# generated string has been silently eaten several times in this campaign - the CR
# escape in particular turns a folder separator into a line break, which is exactly
# what happened when this very line was first written.
$src = Join-Path (Join-Path $workloadsRoot 'commissioning') (Join-Path 'results' 'commissioning-report.json')
$commissionReportCaptured = (Test-Path $src)
if ($commissionReportCaptured) {
    Copy-Item $src (Join-Path $OutDir 'commissioning-report.json') -Force
} else {
    Write-MissingNote -Path (Join-Path $OutDir 'commissioning-report.MISSING.txt') `
        -Expected $src `
        -Why "commission exited $commissionExit without writing a report - it stopped before the report step, or it ran against a different workloads root than this script read"
    Write-Host "  commission: NO REPORT WRITTEN - recorded as commissioning-report.MISSING.txt" -ForegroundColor Yellow
}

if ($commissionExit -eq 0) {
    Write-Host "  commission: harness PROVEN on this machine" -ForegroundColor Green
} elseif ($commissionScope -eq 'layer 1 only') {
    # A layer-1-only commission CANNOT come back green, and that is by design rather than a
    # fault: with no workload there is no app, so layer 2 is recorded NotRun with Fatal true
    # and the exit is 4. Left unsaid, this red is the same red a genuinely broken harness
    # produces, and the campaign depends on those three signals staying apart. So say which
    # one this is - and say the other half too, because NotRun is never a pass.
    Write-Host "  commission: layer 2 is NotRun BECAUSE $commissionScopeReason (exit $commissionExit)." -ForegroundColor Yellow
    Write-Host "              That is NOT evidence of a harness fault - nothing was measured." -ForegroundColor Yellow
    Write-Host "              It is NOT a pass either: capture repeatability is UNKNOWN on this" -ForegroundColor Yellow
    Write-Host "              machine until commission runs here with an application." -ForegroundColor Yellow
} else {
    Write-Host "  commission: HARNESS NOT PROVEN (exit $commissionExit)" -ForegroundColor Red
    Write-Host "              Every result below is unreadable until this passes." -ForegroundColor Red
}
Write-Host ""

foreach ($w in $Workloads) {
    Write-Host "--- $w" -ForegroundColor Cyan

    # 1. doctor. Judge by EXIT CODE, never by scraping the text - a native call's stderr
    #    is noise here, and this campaign has already been bitten by reading words instead
    #    of codes.
    $docOut = & $canary doctor --workload $w --workloads-dir $workloadsRoot 2>&1 | Out-String
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
        $envOut = & $canary env --workload $w --workloads-dir $workloadsRoot 2>&1 | Out-String
        $envExit = $LASTEXITCODE
        $envOut | Set-Content -Path (Join-Path $OutDir "$w.env.txt") -Encoding utf8

        # Segment by segment, for the same reason as the commissioning path above.
        $src = Join-Path (Join-Path $workloadsRoot $w) (Join-Path 'results' 'environment.json')
        if (Test-Path $src) {
            Copy-Item $src (Join-Path $OutDir "$w.environment.json") -Force
            Write-Host "    env    : captured" -ForegroundColor Green
        } else {
            Write-MissingNote -Path (Join-Path $OutDir "$w.environment.MISSING.txt") `
                -Expected $src `
                -Why "canary env exited $envExit without writing a capture - the application did not start, or it started and the probe never got an answer out of it"
            Write-Host "    env    : NO CAPTURE WRITTEN (exit $envExit) - recorded as $w.environment.MISSING.txt" -ForegroundColor Yellow
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

# Publish BEFORE the summary is written, so the summary can state where the bundle actually
# went rather than where it was going to go. The published folder then gets that one file
# copied over on top - one small file, not a second recursive pass.
$publishedTo  = $null
$publishError = $null
if ($doPublish) {
    try {
        New-Item -ItemType Directory -Force -Path $publishDest | Out-Null
        Copy-Item -Path (Join-Path $OutDir '*') -Destination $publishDest -Recurse -Force
        $publishedTo = $publishDest
    } catch {
        # Not fatal. The bundle exists on local disk either way, and telling the operator
        # exactly what to copy where is more use than aborting after the work is done.
        $publishError = $_.Exception.Message
    }
}

$meta = [pscustomobject]@{
    machine     = $env:COMPUTERNAME
    commissionExit = $commissionExit
    harnessProven  = ($commissionExit -eq 0)
    # What the commission actually covered. Under -NoLaunch the exit is 4 because layer 2 is
    # NotRun with Fatal true, which is the harness answering honestly about a question nobody
    # asked it - not a measured fault. Anyone reading harnessProven=false needs this field in
    # the same breath.
    commissionScope = $commissionScope
    commissionScopeReason = $commissionScopeReason
    commissionReportCaptured = $commissionReportCaptured
    # Both roots, because they are different directories on purpose: canary ran the copy,
    # this script read the copy, and the payload was left alone.
    workloadsRootSource = $sourceWorkloadsRoot
    workloadsRootUsed   = $workloadsRoot
    user        = $env:USERNAME
    capturedUtc = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    os          = [System.Environment]::OSVersion.VersionString
    canary      = $canary
    noLaunch    = [bool]$NoLaunch
    publishRequested = [bool]$doPublish
    publishedTo      = $publishedTo
    publishError     = $publishError
    workloads   = $summary
}
$meta | ConvertTo-Json -Depth 5 | Set-Content -Path (Join-Path $OutDir 'qc-summary.json') -Encoding utf8
if ($publishedTo) {
    Copy-Item (Join-Path $OutDir 'qc-summary.json') (Join-Path $publishedTo 'qc-summary.json') -Force
}

Write-Host ""
# Split rather than lumped. Exit 5 is doctor's NOT PROVEN tier: a machine where a check
# could not run, which is neither a passing install nor a failing one. Counting it as a
# failure here would have the bundle report an install problem on a box whose only fault is
# that nobody has commissioned it yet - and the operator would go looking for a package.
$notProven = @($summary | Where-Object { $_.doctorExit -eq 5 })
$failed = @($summary | Where-Object { $_.doctorExit -ne 0 -and $_.doctorExit -ne 5 })
if ($failed.Count -gt 0) {
    Write-Host "$($failed.Count) workload(s) FAILED doctor: $($failed.workload -join ', ')" -ForegroundColor Red
}
if ($notProven.Count -gt 0) {
    Write-Host "$($notProven.Count) workload(s) NOT PROVEN (doctor exit 5): $($notProven.workload -join ', ')" -ForegroundColor Yellow
    Write-Host "  Checks could not run there. Not a failure, and NOT a pass." -ForegroundColor Yellow
}
if ($failed.Count -eq 0 -and $notProven.Count -eq 0) {
    Write-Host "doctor passed on every workload." -ForegroundColor Green
}
Write-Host "Bundle written to $OutDir"
if ($publishedTo) {
    Write-Host "Published to $publishedTo" -ForegroundColor Green
} elseif ($publishError) {
    Write-Host "PUBLISH FAILED - $publishError" -ForegroundColor Red
    Write-Host "The bundle is complete on local disk. Copy it across by hand:" -ForegroundColor Red
    Write-Host "  Copy-Item `"$OutDir`" `"$publishDest`" -Recurse" -ForegroundColor Red
} elseif (-not $driveThere) {
    Write-Host "Not published: $publishRoot is not on this machine." -ForegroundColor Yellow
    Write-Host "Carry the bundle across, or re-run with -Publish once the Drive is mounted." -ForegroundColor Yellow
} else {
    Write-Host "Not published: -NoPublish was given. The bundle is at $OutDir." -ForegroundColor DarkGray
}
Write-Host ""
Write-Host "Next, on a known-good machine:" -ForegroundColor Cyan
foreach ($s in $summary | Where-Object { $_.captured }) {
    Write-Host "  canary env --workload $($s.workload) --diff `"$OutDir\$($s.workload).environment.json`""
}

# Exit non-zero if the harness is unproven OR any workload failed doctor. Both mean the
# bundle cannot be read at face value, and a caller using this as a gate needs one answer.
# Which of the two happened is in commissioning.txt and the per-workload doctor files.
# Under -NoLaunch this is 4 every time, because a layer nobody attempted is not a pass. The
# distinction between "not attempted" and "measured and broken" lives in qc-summary.json's
# commissionScope, and is printed above - the exit code is deliberately not asked to carry it,
# because a gate that treats an unproven harness as green is the defect this replaced.
if ($commissionExit -ne 0) { exit $commissionExit }
if ($failed.Count -gt 0) { exit 1 }
exit 0
