<#
.SYNOPSIS
  Read a QC bundle brought back from another machine, and file its learnings where the next
  session will actually see them.

.DESCRIPTION
  Deployment campaign, the return leg. `qc-capture.ps1` produces the bundle on the QC
  machine; nothing on the dev machine read it afterwards except `canary env --diff`, which
  opens exactly ONE file out of the bundle and ignores everything else. So the trip produced
  evidence and there was no door for that evidence to come in through - the learnings sat in
  a Drive folder that no session-start rule looks at.

  This is that door. It does three things, and repairs nothing:

    1. Copies the bundle's learnings into docs/feedback/inbox/. That is the register the
       session-start rule already reads (AGENTS.md step 4) and the MCP `list_feedback` tool
       already serves. A learning written on the QC machine is a legal feedback item the
       moment it is written - there is no second register and no conversion step, because a
       parallel register would be the one nobody reads.

    2. Reconstructs the three signals MECHANICALLY out of qc-summary.json exit codes -
       never out of the prose in the learnings. Whoever wrote that prose was standing at an
       unfamiliar machine under time pressure, and the entire reason the exit codes are
       separate (commission 4, doctor 1, run path 3) is so the verdict does not depend on
       how the day felt.

    3. Prints the ready-to-run `canary env --diff` line for each workload the bundle
       actually captured, so the next step is a paste and not a reconstruction.

  It never overwrites an item already in the inbox. A slug collision means two different
  observations are claiming one id, and quietly keeping one of them is exactly how the other
  disappears. Collisions are reported and left alone.

  What it does NOT do is judge the machine. The verdict it prints comes from the bundle's
  own exit codes; this script's exit code says only whether the import itself was complete.

.PARAMETER BundlePath
  The bundle directory - the one containing qc-summary.json, normally the published copy
  under the claude-share folder on the Drive.

.PARAMETER InboxDir
  Where to file the learnings. Defaults to docs/feedback/inbox under this repo.

.EXAMPLE
  powershell -File scripts/import-qc-bundle.ps1 "G:/My Drive/claude-share/qc-MACHINE-20260819"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string] $BundlePath,
    [string] $InboxDir
)

# 'Continue', for the same reason qc-capture.ps1 uses it: the bundles most worth reading are
# the ones from a machine where things went wrong, and a reader that aborts on the first
# missing file reports less than the bundle actually contains. Everything below is judged by
# Test-Path and by exit codes read out of JSON, never by a thrown error.
$ErrorActionPreference = 'Continue'

$repo = Split-Path -Parent $PSScriptRoot
if (-not $InboxDir) {
    # Join-Path per segment. This campaign has had a separator inside a generated string
    # literal collapse into something else entirely, and the parser reported the script
    # clean while it did.
    $InboxDir = Join-Path (Join-Path (Join-Path $repo 'docs') 'feedback') 'inbox'
}

if (-not (Test-Path -LiteralPath $BundlePath)) {
    Write-Host "no bundle at $BundlePath" -ForegroundColor Red
    Write-Host "Pass the bundle directory itself - the one containing qc-summary.json." -ForegroundColor Yellow
    exit 1
}

# Everything the bundle should carry and does not gets named here and printed at the end,
# rather than each check quietly doing nothing. A silent skip and a complete bundle look
# identical from the outside, which is how a half-copied folder gets trusted.
$missing = [System.Collections.Generic.List[string]]::new()

function Read-JsonFile {
    param([string] $Path, [string] $Label)
    if (-not (Test-Path -LiteralPath $Path)) {
        $missing.Add("$Label ($(Split-Path -Leaf $Path)) is not in this bundle")
        return $null
    }
    try {
        return (Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json)
    } catch {
        $missing.Add("$Label ($(Split-Path -Leaf $Path)) is present but unreadable: $($_.Exception.Message)")
        return $null
    }
}

# ConvertFrom-Json hands back a PSCustomObject, so a field the producing script never wrote
# is an ABSENT property, not a null one. Asking for it directly would read as $null either
# way and make "never recorded" indistinguishable from "recorded as zero".
function Test-Prop {
    param($Object, [string] $Name)
    if ($null -eq $Object) { return $false }
    return (@($Object.PSObject.Properties.Name) -contains $Name)
}

Write-Host ""
Write-Host "QC bundle: $BundlePath" -ForegroundColor Cyan
Write-Host ""

# --- provenance -------------------------------------------------------------
# The fields a learning's frontmatter has to carry, printed first because a report that
# cannot say which machine, which Canary and which tier produced it is not evidence - and
# because whoever fills in the template needs them to hand, not buried in a JSON file.
$report = Read-JsonFile (Join-Path $BundlePath 'commissioning-report.json') 'the commissioning report'
if ($report -and (Test-Prop $report 'machine')) {
    $m = $report.machine
    Write-Host "provenance (copy these into the learning's frontmatter)" -ForegroundColor Cyan
    Write-Host "  machine       : $($m.machineName)"
    Write-Host "  tier          : $($m.tier)   $($m.tierEvidence)"
    Write-Host "  canaryVersion : $($m.canaryBuild)"
    Write-Host "  os / runtime  : $($m.os) / $($m.runtime)"
} else {
    Write-Host "provenance : NOT AVAILABLE - no machine block in this bundle" -ForegroundColor Yellow
    Write-Host "             A learning from here cannot say which Canary or which tier saw it." -ForegroundColor Yellow
}
Write-Host ""

# --- the three signals ------------------------------------------------------
$summary = Read-JsonFile (Join-Path $BundlePath 'qc-summary.json') 'the bundle summary'

Write-Host "the three signals, read from qc-summary.json" -ForegroundColor Cyan
if (-not $summary) {
    # Fatal to the READING of the bundle, though not to this script: with no summary there
    # are no exit codes, and rebuilding the verdict from the .txt files means reading prose,
    # which is the one thing this step exists to avoid.
    Write-Host "  qc-summary.json is missing or unreadable - the verdict cannot be" -ForegroundColor Red
    Write-Host "  reconstructed from data. Do not infer it from commissioning.txt." -ForegroundColor Red
} else {
    $commissionExit = $null
    if (Test-Prop $summary 'commissionExit') { $commissionExit = $summary.commissionExit }
    $proven = $null
    if (Test-Prop $summary 'harnessProven') { $proven = $summary.harnessProven }

    if ($null -eq $commissionExit) {
        Write-Host "  commissioning : NOT RECORDED - commission was never attempted here." -ForegroundColor Red
        Write-Host "                  NotRun is not a pass. Nothing below is readable yet." -ForegroundColor Red
        $missing.Add('qc-summary.json carries no commissionExit')
    } elseif ($commissionExit -eq 0) {
        Write-Host "  commissioning : exit 0 - harness PROVEN on that machine" -ForegroundColor Green
    } else {
        Write-Host "  commissioning : exit $commissionExit - HARNESS NOT PROVEN" -ForegroundColor Red
        Write-Host "                  Every result in this bundle is unreadable. Not a plug-in defect." -ForegroundColor Red
    }

    # The flag and the exit code are two recordings of one fact. When they disagree the
    # bundle is arguing with itself, and neither half can be quoted until it is re-captured.
    if ($null -ne $proven -and $null -ne $commissionExit) {
        if ([bool]$proven -ne ($commissionExit -eq 0)) {
            Write-Host "  INCONSISTENT  : harnessProven=$proven but commissionExit=$commissionExit." -ForegroundColor Red
            Write-Host "                  Do not quote either until the bundle is re-captured." -ForegroundColor Red
            $missing.Add('harnessProven and commissionExit disagree in qc-summary.json')
        }
    }

    $rows = @()
    if (Test-Prop $summary 'workloads') { $rows = @($summary.workloads) }
    if ($rows.Count -eq 0) {
        Write-Host "  doctor        : NO WORKLOADS in the summary - nothing was doctored." -ForegroundColor Red
        $missing.Add('qc-summary.json lists no workloads')
    }
    foreach ($row in $rows) {
        $name = $row.workload
        $doctorExit = $null
        if (Test-Prop $row 'doctorExit') { $doctorExit = $row.doctorExit }
        if ($null -eq $doctorExit) {
            Write-Host "  doctor $name : NOT RECORDED - never attempted" -ForegroundColor Red
        } elseif ($doctorExit -eq 0) {
            Write-Host "  doctor $name : exit 0 - install complete" -ForegroundColor Green
        } elseif ($doctorExit -eq 5) {
            # 5 is NOT PROVEN, and it is deliberately not 1. Nothing doctor looked at was
            # contradicted; some of it was never asked. Reading it as "install incomplete"
            # sends someone installing packages to fix a machine that has simply never been
            # commissioned or captured - which is the same collapse, one level down, as
            # reading a broken harness as a broken plug-in.
            Write-Host "  doctor $name : exit 5 - NOT PROVEN. Checks could not run; this is not a" -ForegroundColor Yellow
            Write-Host "                  failure and it is not a pass. Read the doctor text for which ones." -ForegroundColor Yellow
        } else {
            Write-Host "  doctor $name : exit $doctorExit - install INCOMPLETE, NOT a plug-in defect" -ForegroundColor Red
        }
        $doctorText = Join-Path $BundlePath ($name + '.doctor.txt')
        if (-not (Test-Path -LiteralPath $doctorText)) {
            $missing.Add("the doctor output for $name - an exit code with no verbatim lines behind it cannot be quoted")
        }
    }

    # The combination, spelled out rather than left to the reader. Three signals collapsed
    # into one word is how a day gets spent hunting a plug-in defect that was an incomplete
    # install.
    $allDoctorsGreen = ($rows.Count -gt 0)
    foreach ($row in $rows) {
        if (-not (Test-Prop $row 'doctorExit')) { $allDoctorsGreen = $false }
        elseif ($row.doctorExit -ne 0) { $allDoctorsGreen = $false }
    }
    Write-Host ""
    if ($commissionExit -eq 0 -and $allDoctorsGreen) {
        Write-Host "  => commissioning green + doctor green. A red suite in this bundle IS a real finding." -ForegroundColor Green
    } else {
        Write-Host "  => a signal under the suite is red. A red suite in this bundle is NOT yet a finding." -ForegroundColor Yellow
    }
    if ((Test-Prop $summary 'noLaunch') -and $summary.noLaunch) {
        Write-Host "  => captured with -NoLaunch: no application ran, so commissioning layers 2 and 3" -ForegroundColor Yellow
        Write-Host "     and every env probe are NotRun here. NotRun is never a pass." -ForegroundColor Yellow
    }
}
Write-Host ""

# --- the learnings ----------------------------------------------------------
$learningsDir = Join-Path $BundlePath 'learnings'
$imported = [System.Collections.Generic.List[string]]::new()
$collided = [System.Collections.Generic.List[string]]::new()

Write-Host "learnings -> $InboxDir" -ForegroundColor Cyan
if (-not (Test-Path -LiteralPath $learningsDir)) {
    Write-Host "  no learnings folder in this bundle." -ForegroundColor Yellow
    $missing.Add('the learnings folder - a published QC bundle is expected to carry one, even empty')
} else {
    # The folder ships with a README explaining what goes in it, and the first run of this
    # script duly filed that README as a finding. Anything that is not named for the
    # convention is scaffolding, not a learning - and it is REPORTED as skipped rather
    # than dropped, because a finding whose file name was typed wrong would otherwise
    # vanish silently, which is the one outcome this whole round trip exists to prevent.
    $all = @(Get-ChildItem -LiteralPath $learningsDir -Filter *.md -File -ErrorAction SilentlyContinue)
    $items = @($all | Where-Object { $_.BaseName -match '^\d{4}-\d{2}-\d{2}-\d{3}-' })
    $skipped = @($all | Where-Object { $_.BaseName -notmatch '^\d{4}-\d{2}-\d{2}-\d{3}-' })
    foreach ($sk in $skipped) {
        if ($sk.Name -eq 'README.md') {
            Write-Host "  skipped   README.md - the folder's own instructions, not a finding" -ForegroundColor DarkGray
        } else {
            Write-Host "  SKIPPED   $($sk.Name) - not named YYYY-MM-DD-NNN-slug.md, so it was NOT imported" -ForegroundColor Yellow
            $missing.Add("$($sk.Name) is in the learnings folder but is not named for the convention - if it is a finding, rename it and re-run")
        }
    }
    if ($items.Count -eq 0) {
        Write-Host "  the learnings folder is present and empty - that machine wrote nothing down." -ForegroundColor Yellow
    }
    New-Item -ItemType Directory -Force -Path $InboxDir | Out-Null
    foreach ($item in $items) {
        $target = Join-Path $InboxDir $item.Name
        if (Test-Path -LiteralPath $target) {
            # Refused, not merged and not silently renamed. Two observations under one id is
            # a question for a person; answering it here would be right about half the time.
            $collided.Add($item.Name)
            Write-Host "  COLLISION $($item.Name) - already in the inbox, left untouched" -ForegroundColor Red
            continue
        }
        Copy-Item -LiteralPath $item.FullName -Destination $target
        $imported.Add($item.Name)
        Write-Host "  imported  $($item.Name)" -ForegroundColor Green

        # A learning with no provenance frontmatter is still filed - losing the observation
        # would be worse - but it is named, because unattributed it is a story rather than
        # evidence, and a month from now nobody will know which machine it was true of.
        $head = (Get-Content -LiteralPath $target -TotalCount 40) -join "`n"
        $absent = @()
        foreach ($key in @('machine', 'tier', 'canaryVersion', 'commissionExit')) {
            if ($head -notmatch ('(?m)^' + $key + '\s*:')) { $absent += $key }
        }
        if ($absent.Count -gt 0) {
            Write-Host "            NO PROVENANCE: missing $($absent -join ', ') - see docs/templates/qc-learning-template.md" -ForegroundColor Yellow
        }
    }
}
Write-Host ""

# --- the next command -------------------------------------------------------
Write-Host "environment diffs you can run now" -ForegroundColor Cyan
$printed = 0
if ($summary -and (Test-Prop $summary 'workloads')) {
    foreach ($row in @($summary.workloads)) {
        $name = $row.workload
        $envJson = Join-Path $BundlePath ($name + '.environment.json')
        if (Test-Path -LiteralPath $envJson) {
            Write-Host "  canary env --workload $name --diff `"$envJson`""
            $printed = $printed + 1
        } elseif ((Test-Prop $row 'captured') -and $row.captured) {
            # The summary says a capture was written and the file is not here, so the copy
            # off that machine was incomplete. That matters more than the one missing file:
            # it means nothing else in this bundle can be assumed to have arrived either.
            Write-Host "  $name : the summary says captured, but that environment file is NOT in the bundle" -ForegroundColor Red
            $missing.Add("the environment capture for $name, which the summary says was written")
        } else {
            Write-Host "  $name : no environment captured on that machine - nothing to diff" -ForegroundColor DarkGray
        }
    }
}
if ($printed -eq 0) {
    Write-Host "  none - this bundle carries no environment capture to compare against." -ForegroundColor Yellow
}
Write-Host ""

foreach ($f in @('machine-survey.json', 'commissioning.txt')) {
    if (-not (Test-Path -LiteralPath (Join-Path $BundlePath $f))) { $missing.Add("$f is not in this bundle") }
}

if ($missing.Count -gt 0) {
    Write-Host "this bundle is missing things it was expected to carry:" -ForegroundColor Yellow
    foreach ($x in $missing) { Write-Host "  - $x" -ForegroundColor Yellow }
    Write-Host ""
}

Write-Host "imported $($imported.Count) learning(s); $($collided.Count) collision(s) refused." -ForegroundColor Cyan
if ($imported.Count -gt 0) {
    Write-Host "They are feedback items now: the next session lists them at start (AGENTS.md step 4)." -ForegroundColor Cyan
}

# The exit code answers ONE question: was the import complete? It deliberately does not
# encode the health of the QC machine - that verdict is printed above and belongs to the
# exit codes inside the bundle. Overloading the two would make a broken QC machine and a
# half-copied folder look identical to a caller, which is the confusion this whole campaign
# is organised around not making.
if ($collided.Count -gt 0 -or $missing.Count -gt 0) { exit 1 }
exit 0
