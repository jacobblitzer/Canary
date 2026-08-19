# Publish the Canary Drive payload (campaign clean-install-hardening Phase 2).
#
# WHY THIS EXISTS: there was no publish script. The payload accreted by hand,
# except for one MSBuild target on the net48 Rhino agent that fired on every
# build and sprayed its .NET Framework closure over the net8.0 harness's
# assemblies of the same name. Machine 2 inherited a canary.exe that threw
# FileNotFoundException(Microsoft.Bcl.AsyncInterfaces) on the first JSON it
# touched, while --help worked - so the payload looked healthy. The deps.json
# was correct all along; the DLLs beside it were from another framework.
#
# WHAT THIS GUARANTEES:
#   - harness (net8.0) and agent (net48) land in SEPARATE folders, both from
#     ONE commit, both Release;
#   - a dirty tree refuses to publish (a stamp that names a commit must
#     describe the bytes);
#   - a manifest is built from the PUBLISH OUTPUT and the payload is verified
#     against it before any stamp is written;
#   - every shipped managed assembly's target framework is recorded, so
#     verify-payload.ps1 can catch a cross-framework overwrite - the failure a
#     file-list-vs-dependency-graph check would have passed.
#   - the payload is assembled and VERIFIED in a staging folder, and the Drive
#     destination is not touched until that verification has passed. See the
#     assemble block for why that ordering is the whole point.
#
# -IncludeBristle restores the old, repo-dependent test globs. Default OFF; the
# reason is spelled out where the globs are.
param([switch]$AllowDirty, [switch]$IncludeBristle)
$ErrorActionPreference = "Stop"

$SRC  = "C:\Repos\Canary"
$DST  = "G:\My Drive\Builds\Canary"
$stage = Join-Path $env:TEMP "canary-publish-$(Get-Random)"
# the finished payload is built HERE first and only mirrored to $DST at the very end
$payload = Join-Path $stage "payload"

$branch = (git -C $SRC rev-parse --abbrev-ref HEAD).Trim()
$commit = (git -C $SRC rev-parse --short HEAD).Trim()
$dirty  = [bool](git -C $SRC status --porcelain)
if ($dirty -and -not $AllowDirty) {
    throw ("REFUSING to publish a DIRTY tree - the stamp would name a commit " +
           "that does not describe these bytes. Commit first, or use -AllowDirty.")
}

Write-Host "publishing harness (net8.0) + agent (net48) from $branch @ $commit..."
& dotnet publish "$SRC\src\Canary.Harness\Canary.Harness.csproj" -c Release `
    -o "$stage\harness" --nologo | Out-Null
if ($LASTEXITCODE) { throw "dotnet publish (harness) failed: $LASTEXITCODE" }
& dotnet publish "$SRC\src\Canary.UI.Avalonia\Canary.UI.Avalonia.csproj" -c Release `
    -o "$stage\harness" --nologo | Out-Null
if ($LASTEXITCODE) { throw "dotnet publish (UI) failed: $LASTEXITCODE" }
# the agent is net48 and must NEVER share a folder with the net8.0 output
& dotnet build "$SRC\src\Canary.Agent.Rhino\Canary.Agent.Rhino.csproj" -c Release `
    -p:CanaryShipToDrive=false --nologo | Out-Null
if ($LASTEXITCODE) { throw "dotnet build (agent) failed: $LASTEXITCODE" }
$agentDir = "$SRC\src\Canary.Agent.Rhino\bin\Release\net48"

# ------------------------------------------------------------- assemble
# ASSEMBLED INTO A STAGE, NOT INTO THE DESTINATION.
# The old order wiped $DST first and self-verified afterwards, so anything that threw in
# between left the QC machine with an emptied payload folder and no BUILD_INFO.txt to say
# what had happened - a publish that destroyed the operator's only copy and then refused to
# replace it. That is exactly what has been happening: the readiness gate has been failing
# on every run since the tests grew their `requires` declarations, so each attempt deleted
# the payload and stopped.
#
# The fix chosen here is VERIFY BEFORE WIPING - a complete payload is built and put through
# verify-payload.ps1 in %TEMP%, and $DST is not touched until it passes. Two smaller
# properties fall out of it and are worth keeping on purpose:
#   - the verified stage survives a failed mirror (it is only deleted on success), so the
#     recovery is a folder copy, not a rebuild;
#   - the only remaining destructive window is the mirror itself, and the catch around it
#     prints an unmissable block naming the destination as incomplete.
# The alternative - mirror to a sibling folder and swap - was rejected because Drive sync
# treats a rename as a delete plus an upload of the whole payload, which is the slowest and
# least atomic thing available here.
$stageHarness = Join-Path $stage "harness"
$payloadAgent = Join-Path $payload "agent"
New-Item -ItemType Directory -Force $payload, $payloadAgent | Out-Null
Copy-Item (Join-Path $stageHarness "*") $payload -Recurse -Force
Copy-Item (Join-Path $agentDir "*") $payloadAgent -Recurse -Force -Exclude *.pdb

$srcWorkloads = Join-Path $SRC "workloads"
$dstWorkloads = Join-Path $payload "workloads"
$workloads = @("rhino")

# ---- LAYER-3 GUARD, run before anything is copied and long before $DST is touched.
# Commissioning layer 3 asks whether a pixel baseline made on ANOTHER machine matches one
# made here, and it can only ask that of a workload it has a foreign capture for. A workload
# with no reference image does not FAIL layer 3 - it leaves it NotRun, which reads as quiet
# success and silently deletes the campaign's headline finding. So refuse to publish rather
# than ship a workload that cannot be asked the question.
$srcCommissioning = Join-Path $srcWorkloads "commissioning"
$srcReferences    = Join-Path $srcCommissioning "references"
$noReference = @($workloads | Where-Object {
    -not (Test-Path (Join-Path $srcReferences ($_ + "-reference.png")))
})
if ($noReference.Count) {
    throw ("REFUSING to publish: no commissioning layer-3 reference image for: " +
           ($noReference -join ", ") + ". Each shipped workload needs " +
           "workloads/commissioning/references/<workload>-reference.png, captured on a " +
           "DIFFERENT machine. Without it layer 3 reports NotRun, and NotRun is never a pass.")
}

# the workloads tree the harness resolves before it can run anything
foreach ($w in $workloads) {
    $sw       = Join-Path $srcWorkloads $w
    $swTests  = Join-Path $sw "tests"
    $swSuites = Join-Path $sw "suites"
    $swFix    = Join-Path $sw "fixtures"
    $wd       = Join-Path $dstWorkloads $w
    $wdTests  = Join-Path $wd "tests"
    $wdSuites = Join-Path $wd "suites"
    $wdFix    = Join-Path $wd "fixtures"
    New-Item -ItemType Directory -Force $wdTests, $wdFix, $wdSuites | Out-Null
    Copy-Item (Join-Path $sw "workload.json") $wd -Force
    # ---- THE DEFAULT FLIPPED, from the bristle corpus to the one-test smoke suite.
    # Those tests carry 378 references to %CANARY_REPO_BRISTLE% and drive a live python
    # service, so on a machine with no repos and no service they cannot run - not "might
    # fail", cannot run. Shipping them made `canary doctor` red BY CONSTRUCTION on every QC
    # machine, and a doctor that is always red destroys the one distinction the campaign is
    # built on: doctor red is supposed to mean the install is incomplete, so that
    # commissioning green + doctor green + smoke red is readable as a real plug-in finding.
    # smoke-test.json has zero requires and zero tokens, so it asks the harness the only
    # question a bare machine can answer.
    Copy-Item (Join-Path $swTests "smoke-test.json") $wdTests -Force
    Copy-Item (Join-Path $swSuites "smoke.json") $wdSuites -Force
    if ($IncludeBristle) {
        # additive, for a machine that DOES have the Bristle stack: the old globs on top of
        # smoke, so staging a full-corpus payload is still one switch away.
        Copy-Item (Join-Path $swTests "bristle-*.json") $wdTests -Force
        Copy-Item (Join-Path $swSuites "bristle.json") $wdSuites -Force
    }
    Copy-Item (Join-Path $swFix "*") $wdFix -Recurse -Force
    # ---- baselines.lock.json, FILTERED to the tests this payload actually carries.
    # Phase 2b. Shipping the dev machine whole ledger would make `canary doctor` on the
    # target report missing baselines for tests that are not even in the payload - noise
    # that gets a guard switched off. Shipping NOTHING is worse: an absent ledger is an
    # error by design (that is what stops a deleted file silently disabling the guard),
    # so a payload must carry one even when it is empty.
    #
    # Today this is 0 rows: smoke-test has no approved baseline in the ledger, and the
    # bristle checkpoints that would be the other candidates are capture-only and do not
    # ship by default anyway. A committed "rows": [] is the honest declaration that nothing
    # here is compared. It is NOT hard-coded to empty: the day a payload ships a test whose
    # checkpoint HAS an approved baseline, its row travels, and the target reports that
    # baseline missing instead of New.
    $srcLedger = Join-Path $sw "baselines.lock.json"
    $shipped = @(Get-ChildItem $wdTests -Filter *.json -File | ForEach-Object { $_.BaseName })
    $rows = @()
    if (Test-Path $srcLedger) {
        $rows = @((Get-Content $srcLedger -Raw | ConvertFrom-Json).rows |
                  Where-Object { $shipped -contains $_.test })
    }
    [pscustomobject]@{ version = 1; workload = $w; rows = $rows } |
        ConvertTo-Json -Depth 6 | Set-Content (Join-Path $wd "baselines.lock.json") -Encoding utf8
    Write-Host ("  {0}: shipped ledger has {1} row(s) for {2} shipped test(s)" -f $w, $rows.Count, $shipped.Count)
    if ($rows.Count -gt 0) {
        Write-Host ("  WARNING: {0} ledgered baseline(s) travel with this payload but it ships NO images." -f $rows.Count) -ForegroundColor Yellow
        Write-Host "  The target will report these Failed until the images are delivered - correct, and loud." -ForegroundColor Yellow
    }
}

# ---- the commissioning workload, which travels WHOLE.
# It answers "can this machine test at all?" before any plug-in result means anything, and
# it is the only workload a USER-tier machine gets. ALL FOUR reference images ship: three
# feed layer 1, the comparer, which is the layer that runs where nothing else does; and
# rhino-reference.png feeds layer 3, the foreign capture. Shipping a curated three-image
# subset looks tidy: layer 1 still passes, and layer 3 goes permanently NotRun - which is
# the campaign's headline expected finding deleted, silently, by a tidy-up.
# results/ deliberately does NOT travel: it is this machine's answers, it is gitignored,
# and a fresh payload carrying them would look commissioned before anyone commissioned it.
$dstCommissioning = Join-Path $dstWorkloads "commissioning"
New-Item -ItemType Directory -Force (Join-Path $dstCommissioning "references") | Out-Null
Copy-Item (Join-Path $srcCommissioning "workload.json") $dstCommissioning -Force
Copy-Item (Join-Path $srcCommissioning "README.md") $dstCommissioning -Force
Copy-Item (Join-Path $srcReferences "*.png") (Join-Path $dstCommissioning "references") -Force

# ---- the two files at the workloads root that make a shipped test resolvable at all.
# tokens.json is the entire point of the token indirection: without it every %CANARY_REPO_*%
# stays literal, and doctor reds out on preconditions that have nothing to do with the
# plug-in under test - the second signal impersonating the third. plugin-packages.json is
# what machine-setup.ps1 reads to know which yak packages a QC machine still needs.
Copy-Item (Join-Path $srcWorkloads "tokens.json") $dstWorkloads -Force
Copy-Item (Join-Path $srcWorkloads "plugin-packages.json") $dstWorkloads -Force

# ---- the operator's four scripts, at the payload ROOT beside canary.exe.
# A QC machine gets this folder and nothing else - no repo clone, no scripts checkout - so
# anything the operator is told to run has to already be sitting next to the exe. They are
# copied before the manifest block on purpose, so their hashes travel and verify-payload
# can tell a truncated sync from a complete one.
$srcScripts = Join-Path $SRC "scripts"
foreach ($s in @("verify-payload.ps1", "machine-survey.ps1", "qc-capture.ps1", "machine-setup.ps1")) {
    Copy-Item (Join-Path $srcScripts $s) $payload -Force
}

# ------------------------------- manifest: hashes + TARGET FRAMEWORK per dll
Add-Type -AssemblyName System.Runtime 2>$null
function Get-AssemblyTfm {
    param([string]$Path)
    try {
        $bytes = [IO.File]::ReadAllBytes($Path)
        $txt = [Text.Encoding]::UTF8.GetString($bytes)
        if ($txt -match '\.NETCoreApp,Version=v[\d.]+')   { return $Matches[0] }
        if ($txt -match '\.NETFramework,Version=v[\d.]+') { return $Matches[0] }
        if ($txt -match '\.NETStandard,Version=v[\d.]+')  { return $Matches[0] }
    } catch { }
    return $null
}
$files = @()
Get-ChildItem $payload -Recurse -File | ForEach-Object {
    $rel = $_.FullName.Substring($payload.Length).TrimStart('\')
    $files += [pscustomobject]@{
        path = $rel
        sha256 = (Get-FileHash $_.FullName -Algorithm SHA256).Hash
        bytes = $_.Length
        tfm = if ($_.Extension -in ".dll", ".exe", ".rhp") { Get-AssemblyTfm $_.FullName } else { $null }
    }
}
$manifest = [pscustomobject]@{
    generated = (Get-Date -Format s); repo = "Canary"
    branch = $branch; commit = $commit; dirty = $dirty
    harnessTfm = ".NETCoreApp,Version=v8.0"; agentTfm = ".NETFramework,Version=v4.8"
    note = "harness assemblies live at the payload root and must ALL be net8.0; the net48 agent lives under agent\. A mixed TFM at the root is the machine-2 corruption signature."
    files = $files
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $payload "MANIFEST.json") -Encoding utf8

# ------------------------------------------------------------ self-verify
# Against the STAGE, while the destination is still the last known-good payload. A failure
# here costs the operator nothing but the time this script already spent.
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $payload "verify-payload.ps1") -Root $payload
if ($LASTEXITCODE) {
    throw ("the staged payload FAILED its own verification - nothing was published and " +
           "the existing payload at $DST is UNTOUCHED. Fix the fault and re-run.")
}

# The runtime line is not decoration. Both projects are net8.0-windows published
# framework-dependent, so the payload needs the .NET 8 Windows Desktop Runtime on the target
# and ships nothing that provides it. When it is absent, canary.exe dies in the apphost with
# "You must install .NET" BEFORE a single line of Canary code runs - no log, no exit code
# anyone here chose - and an operator reasonably reads that as a corrupt payload and starts
# re-syncing Drive. Naming it on the stamp turns a day of that into one download.
$stamp = @(
    "Canary Drive payload",
    "staged   : $(Get-Date -Format yyyy-MM-dd)",
    "source   : $branch @ $commit$(if ($dirty) { ' (DIRTY)' })",
    "layout   : harness (net8.0) at the root; Rhino agent (net48) under agent\",
    "runtime  : REQUIRES the .NET 8 Windows Desktop Runtime (x64), which this payload does",
    "           NOT contain. Without it canary.exe fails in the Windows apphost with 'You",
    "           must install .NET' before any Canary code runs - that is a missing runtime,",
    "           not a corrupt payload. Get it from:",
    "           https://dotnet.microsoft.com/download/dotnet/8.0",
    "rhino    : ships the 'smoke' suite only (re-publish with -IncludeBristle for the",
    "           bristle corpus, which needs the Bristle repo and its python service)",
    "verified : $($files.Count) files, hashes + target frameworks (MANIFEST.json)",
    "verify   : powershell -File verify-payload.ps1"
) -join "`n"
[IO.File]::WriteAllText((Join-Path $payload "BUILD_INFO.txt"), $stamp, (New-Object System.Text.UTF8Encoding($false)))

# --------------------------------------------------------------- publish
# The ONLY destructive step, and it now runs on a payload that has already proved itself.
# If the mirror dies part-way the destination really is unusable, so say so in a block
# nobody can scroll past, and leave the verified stage on disk as the recovery copy.
$destWiped = $false
try {
    Remove-Item (Join-Path $DST "*") -Recurse -Force -ErrorAction SilentlyContinue
    $destWiped = $true
    New-Item -ItemType Directory -Force $DST | Out-Null
    Copy-Item (Join-Path $payload "*") $DST -Recurse -Force
} catch {
    if ($destWiped) {
        Write-Host ""
        Write-Host "**********************************************************************" -ForegroundColor Red
        Write-Host "  THE DESTINATION IS NOW INCOMPLETE. DO NOT INSTALL FROM IT." -ForegroundColor Red
        Write-Host "    $DST" -ForegroundColor Red
        Write-Host "  The old payload was deleted and the new one did not finish copying." -ForegroundColor Red
        Write-Host "  A VERIFIED payload is still on this machine at:" -ForegroundColor Red
        Write-Host "    $payload" -ForegroundColor Red
        Write-Host "  Copy that folder's contents over the destination, or re-run this script." -ForegroundColor Red
        Write-Host "  It is NOT deleted on this path - that is deliberate." -ForegroundColor Red
        Write-Host "**********************************************************************" -ForegroundColor Red
    }
    throw
}

Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "published $branch @ $commit - $($files.Count) files, verified"
