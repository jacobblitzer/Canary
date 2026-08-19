# Verify a Canary payload before trusting it (campaign Phase 2).
#
# Checks FOUR things, in the order that would have caught machine 2's
# incident soonest:
#   1. TARGET FRAMEWORK CONSISTENCY - every managed assembly at the payload
#      root must be net8.0, and everything under agent\ must be net48. The
#      machine-2 payload passed a file-list check and a dependency-graph check
#      and was still broken, because the FILES were right and their FRAMEWORK
#      was wrong (a net48 System.Text.Json.dll sitting where the net8.0 one
#      belonged, demanding Microsoft.Bcl.AsyncInterfaces).
#   2. completeness + integrity against the manifest's source hashes;
#   3. COMMISSIONING CONTENT BY NAME - the one thing the manifest structurally
#      cannot catch, because it is built from what was copied (see below);
#   4. provenance - a dirty-tree stamp is reported, not hidden.
#
# -Root is the payload folder. It defaults to the folder this script sits in,
# because on a QC machine the script IS in the payload; publish-payload.ps1
# passes it explicitly to verify a staged payload that is not here yet.
param([string]$Root = $PSScriptRoot)
$ErrorActionPreference = "Stop"

# $Root used to be honoured for the manifest and quietly ignored for canary.exe and the
# workloads dir, which both hard-coded $PSScriptRoot. That is invisible on the dev machine -
# publish passes -Root $DST and the script it runs lives in $DST, so the two agree - and it
# means every -Root anyone else passes verified one payload's bytes while doctoring another
# payload's tests. Resolve it once here and use it everywhere below.
if (-not (Test-Path $Root)) {
    Write-Host "NO SUCH PAYLOAD ROOT: $Root" -ForegroundColor Red
    exit 1
}
$Root = (Resolve-Path $Root).Path
Write-Host "verifying payload root: $Root"

$mp = Join-Path $Root "MANIFEST.json"
if (-not (Test-Path $mp)) {
    Write-Host "NO MANIFEST at $mp - payload predates verification or is incomplete." -ForegroundColor Red
    exit 1
}
$m = Get-Content $mp -Raw | ConvertFrom-Json
Write-Host "payload: $($m.repo) $($m.branch) @ $($m.commit)$(if ($m.dirty) { ' (DIRTY)' }) - $($m.files.Count) files"

$missing = @(); $mismatch = @(); $tfm = @()
foreach ($f in $m.files) {
    $p = Join-Path $Root $f.path
    if (-not (Test-Path $p)) { $missing += $f.path; continue }
    if ((Get-FileHash $p -Algorithm SHA256).Hash -ne $f.sha256) { $mismatch += $f.path; continue }
    if ($f.tfm) {
        $underAgent = $f.path -like "agent\*"
        $want = if ($underAgent) { $m.agentTfm } else { $m.harnessTfm }
        # third-party assemblies may legitimately be netstandard; only flag a
        # HARNESS-root assembly that is .NETFramework (the corruption shape)
        if (-not $underAgent -and $f.tfm -like ".NETFramework*") {
            $tfm += "$($f.path)  [$($f.tfm)] - a .NET Framework assembly at the net8.0 harness root"
        }
        if ($underAgent -and $f.tfm -like ".NETCoreApp*") {
            $tfm += "$($f.path)  [$($f.tfm)] - a .NET Core assembly inside the net48 agent folder"
        }
    }
}
foreach ($x in $missing)  { Write-Host "MISSING   $x" -ForegroundColor Red }
foreach ($x in $mismatch) { Write-Host "MISMATCH  $x" -ForegroundColor Yellow }
foreach ($x in $tfm)      { Write-Host "WRONG-TFM $x" -ForegroundColor Red }
if ($m.dirty) { Write-Host "WARNING: staged from a DIRTY tree." -ForegroundColor Yellow }

if ($missing.Count -or $mismatch.Count -or $tfm.Count) {
    Write-Host "`nFAILED: $($missing.Count) missing, $($mismatch.Count) corrupt, $($tfm.Count) wrong-framework. Re-publish (scripts/publish-payload.ps1); do not install." -ForegroundColor Red
    exit 1
}
Write-Host "`nOK: $($m.files.Count) files present, byte-identical, and framework-consistent." -ForegroundColor Green

# ---- commissioning content, asserted BY NAME ------------------------------
# Phase 3. The manifest is built from what publish copied, so a file that was never copied
# is by construction never missed - the check above cannot see a hole it was never told
# about. And `canary commission` only WARNS when no commissioning report exists, which is
# the state of every freshly published payload. Between those two, a payload could stamp
# itself healthy while carrying none of the content that decides whether ANY result on that
# machine is readable. So the names are written out here, longhand, and a payload missing
# any of them is not publishable and not installable.
#
# This lives in VERIFY rather than in publish on purpose: publish runs once, on the dev
# machine, where the files are obviously present. Verify runs again on the QC machine, after
# Drive sync has had its chance to drop a PNG, in front of the operator who can act on it.
$commissioning = Join-Path (Join-Path $Root "workloads") "commissioning"
$references    = Join-Path $commissioning "references"
$needed = @(
    (Join-Path $commissioning "workload.json"),
    (Join-Path $commissioning "README.md"),
    # layer 1: the three synthetic comparer images. Their answers are known exactly, which
    # is what lets layer 1 run on a machine with no app and nothing else to compare against.
    (Join-Path $references "comparer-a.png"),
    (Join-Path $references "comparer-b.png"),
    (Join-Path $references "comparer-a-nudged.png")
)
# layer 3: one foreign capture per shipped workload. A workload with no reference does not
# fail layer 3, it leaves it NotRun - and NotRun is never a pass, so name it as missing.
$shippedWorkloads = @()
$workloadsDir = Join-Path $Root "workloads"
if (Test-Path $workloadsDir) {
    $shippedWorkloads = @(Get-ChildItem $workloadsDir -Directory |
                          Where-Object { $_.Name -ne "commissioning" } |
                          ForEach-Object { $_.Name })
}
foreach ($w in $shippedWorkloads) { $needed += (Join-Path $references ($w + "-reference.png")) }

$absent = @($needed | Where-Object { -not (Test-Path $_) })
# Both faults are reported before exiting - an operator who has to re-sync should learn
# everything that is wrong in one pass, not one file per attempt.
if ($absent.Count) {
    Write-Host "`nCOMMISSIONING CONTENT MISSING - this payload cannot answer 'can this machine test at all?':" -ForegroundColor Red
    foreach ($x in $absent) { Write-Host "  ABSENT  $x" -ForegroundColor Red }
    Write-Host "Without it commissioning reports NotRun, which is never a pass." -ForegroundColor Red
}
if ($shippedWorkloads.Count -eq 0) {
    # a per-workload assertion over zero workloads is vacuously true, which is the same
    # shape of lie as NotRun: nothing was checked and it looks like a pass.
    Write-Host "`nNO WORKLOAD SHIPPED beside commissioning - this payload can run nothing." -ForegroundColor Red
}
if ($absent.Count -or $shippedWorkloads.Count -eq 0) {
    Write-Host "Re-publish (scripts/publish-payload.ps1); do not install." -ForegroundColor Red
    exit 1
}
Write-Host ("OK: commissioning content present - {0} file(s), including a layer-3 reference for each of: {1}" -f $needed.Count, ($shippedWorkloads -join ", ")) -ForegroundColor Green

# ---- readiness, which byte integrity cannot answer -------------------------
# Phase 2b. Everything above proves the bytes arrived intact. It passes happily on a
# payload whose tests point at roots this machine does not have, or whose ledgered
# baselines were never delivered - and a run in that state reports New, which the exit
# code excludes, so it prints a pass while comparing nothing. Different question, so it
# needs a different check.
#
# Phase 3: the suite is `smoke`, not `bristle`. A default payload no longer carries the
# bristle corpus at all, and doctoring a suite that is not here would fail for the one
# reason that says nothing about this machine while drowning out the reasons that do. Smoke
# stays the gate even on a -IncludeBristle payload: this check asks whether the payload can
# be trusted, and bristle's repo and service preconditions are a separate question the
# operator asks deliberately with `canary doctor --suite bristle`.
$canary = Join-Path $Root "canary.exe"
if (Test-Path $canary) {
    Write-Host "`nreadiness check: canary doctor --workload rhino --suite smoke"
    & $canary doctor --workload rhino --suite smoke --workloads-dir (Join-Path $Root "workloads")
    $doctorExit = $LASTEXITCODE

    # EXIT 5 IS NOT PROVEN, AND AT PUBLISH TIME IT IS THE CORRECT ANSWER.
    # This script asks whether the BYTES are intact and the CONTENT is complete. Doctor's
    # other job - has anything measured this machine - is answered by an environment capture
    # and a commissioning report, and a payload that was assembled thirty seconds ago has
    # neither by definition. Treating 5 as a failure made the gate unpassable: a freshly
    # staged payload can never have been commissioned, so it could never be published.
    #
    # 1 still fails hard. That means doctor found something it could check and it was wrong,
    # which is precisely what this gate is for.
    if ($doctorExit -eq 5) {
        Write-Host ""
        Write-Host "doctor: NOT PROVEN (exit 5) - expected here. The content checks passed; the" -ForegroundColor Yellow
        Write-Host "  checks that could not run need a machine that has been commissioned and" -ForegroundColor Yellow
        Write-Host "  captured, which a payload folder is not. Run them on the TARGET machine:" -ForegroundColor Yellow
        Write-Host "    canary commission --workload rhino" -ForegroundColor Yellow
        Write-Host "    canary env        --workload rhino" -ForegroundColor Yellow
    } elseif ($doctorExit -ne 0) {
        Write-Host "FAILED: the bytes are intact but this machine cannot be trusted to report on these tests." -ForegroundColor Red
        exit 1
    }
} else {
    Write-Host "`nNOTE: canary.exe not found at the payload root - skipping the readiness check." -ForegroundColor Yellow
}
exit 0
