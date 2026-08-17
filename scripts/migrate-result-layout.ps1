<#
.SYNOPSIS
  Phase 2b C2 + C2b - bring suite-nested baselines to the flat contract. COPIES ONLY.

.DESCRIPTION
  The flat contract (a test's evidence directory is a pure function of workload+test)
  is what the run path already reads. 59 approved baselines currently live ONLY at
  results/<suite>/<test>/baselines/, where the shared run path cannot see them - six
  suites are green-but-blind today because of it. This brings those to
  results/<test>/baselines/.

  TWO INVARIANTS, both load-bearing:

  1. NOTHING IS DELETED OR MOVED. Every operation is a copy. The nested originals stay
     exactly where they are, so this step is reversible by deleting what it added, and a
     mistake cannot destroy an approved reference.

  2. AN EXISTING FLAT BASELINE IS NEVER OVERWRITTEN. Where both layouts hold a baseline
     for one identity (9 pairs, 36 PNGs), flat wins and the nested generation is LEFT
     EXACTLY WHERE IT IS - reported, not touched.

     A first version of this script COPIED those 36 into
     results/<test>/archived/pre-2b-<suite>/ "so the generations stay inspectable side by
     side". That was wrong twice over. It was redundant - invariant 1 means the nested
     original is still on disk, so the bytes already existed in two places (three with
     C0's snapshot). And archived/<slot> is an OWNED namespace meaning "a snapshot of a
     run": PastRunsScanner.ScanKindAsync adds a UI row for every directory under
     archived/, deliberately, even with no result.json - so the parked dirs appeared as
     phantom "(snapshot)" rows that fail to load. The sibling alternative is no better:
     ResultsViewerViewModel.CopyDirectoryRecursive copies every non-archived subdir into
     each future snapshot, so a results/<test>/pre-2b/ would be duplicated forever.

     The nested originals ARE the parked generation until C3 retires them. Relocating
     them out of results/ is C3/C4's job, done once, deliberately.

  Original LastWriteTimeUtc is preserved on every copy: the location changed, the
  approval did not, and the ledger's approvedUtc should keep meaning what it says.

  Native tools are judged by EXIT CODE. Refuses to do anything unless C0's snapshot
  exists and the ledger verifies green first.

.PARAMETER Apply
  Actually write. Without this the script prints the full plan and changes nothing.
#>
[CmdletBinding()]
param(
    [string]$WorkloadsRoot = 'C:\Repos\Canary\workloads',
    [string]$Canary = 'C:\Repos\Canary\src\Canary.Harness\bin\Debug\net8.0-windows\canary.exe',
    [string]$SnapshotRoot = "$env:LOCALAPPDATA\canary-phase2b-backup",
    [switch]$Apply
)

$ErrorActionPreference = 'Stop'

function Get-Sha([string]$p) { (Get-FileHash -Path $p -Algorithm SHA256).Hash.ToLower() }

Write-Host "Phase 2b C2 + C2b - nested baselines -> flat contract"
Write-Host ("  mode : {0}" -f $(if ($Apply) { 'APPLY' } else { 'DRY RUN (pass -Apply to write)' }))

# --- precondition 1: C0 snapshot exists and is non-trivial --------------------
if (-not (Test-Path $SnapshotRoot)) {
    throw "No C0 snapshot under $SnapshotRoot. Run scripts/snapshot-baselines.ps1 first - there is no git recovery for baselines."
}
$snap = Get-ChildItem -Path $SnapshotRoot -Directory | Sort-Object Name -Descending | Select-Object -First 1
if (-not $snap) { throw "No snapshot directory inside $SnapshotRoot." }
$snapManifest = Join-Path $snap.FullName 'MANIFEST.sha256.txt'
if (-not (Test-Path $snapManifest)) { throw "Snapshot $($snap.Name) has no MANIFEST.sha256.txt - not a verified snapshot." }
$snapRows = @(Get-Content $snapManifest | Where-Object { $_ -match '^[0-9A-Fa-f]{64}\s\s' }).Count
Write-Host ("  C0   : snapshot {0}, {1} hashed files" -f $snap.Name, $snapRows)
if ($snapRows -lt 1) { throw "Snapshot manifest is empty." }

# --- precondition 2: the ledger verifies GREEN under today's rule ------------
$workloads = @('rhino', 'penumbra', 'qualia', 'qualia-web', 'qualia-desktop')
foreach ($w in $workloads) {
    & $Canary baselines verify --workload $w --layout dual --workloads-dir $WorkloadsRoot | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "baselines verify --workload $w --layout dual failed (exit $LASTEXITCODE). Refusing to migrate against an unverified ledger."
    }
}
Write-Host "  C1   : all $($workloads.Count) ledgers verify green under --layout dual"

# --- build the plan ----------------------------------------------------------
# Every nested baseline PNG, classified by whether flat already holds that identity.
$copy = @()      # nest-only -> flat
$park = @()      # collision -> archived/pre-2b-<suite>/

foreach ($w in (Get-ChildItem -Path $WorkloadsRoot -Directory)) {
    $results = Join-Path $w.FullName 'results'
    if (-not (Test-Path $results)) { continue }

    foreach ($scopeDir in (Get-ChildItem -Path $results -Directory)) {
        # A nested baseline lives at results/<scope>/<test>/baselines/*.png
        foreach ($testDir in (Get-ChildItem -Path $scopeDir.FullName -Directory -ErrorAction SilentlyContinue)) {
            $nested = Join-Path $testDir.FullName 'baselines'
            if (-not (Test-Path $nested)) { continue }

            $test = $testDir.Name
            $scope = $scopeDir.Name
            if ($scope -eq $test) { continue }   # a flat dir, not a scope

            foreach ($png in (Get-ChildItem -Path $nested -Filter '*.png' -File)) {
                $flat = Join-Path (Join-Path (Join-Path $results $test) 'baselines') $png.Name
                $row = [pscustomobject]@{
                    Workload = $w.Name
                    Scope    = $scope
                    Test     = $test
                    File     = $png.Name
                    Src      = $png.FullName
                    Dst      = $flat
                    SrcSha   = Get-Sha $png.FullName
                    DstSha   = $(if (Test-Path $flat) { Get-Sha $flat } else { $null })
                }
                if ($null -eq $row.DstSha) { $copy += $row } else { $park += $row }
            }
        }
    }
}

$identical = @($park | Where-Object { $_.SrcSha -eq $_.DstSha })
$differing = @($park | Where-Object { $_.SrcSha -ne $_.DstSha })

Write-Host ""
Write-Host "C2 - COPY nested -> flat (no flat baseline exists for these identities)"
Write-Host ("  {0} PNGs across {1} test dirs" -f @($copy).Count, @($copy | Select-Object -ExpandProperty Test -Unique).Count)
foreach ($g in ($copy | Group-Object Workload, Scope | Sort-Object Name)) {
    $dirs = @($g.Group | Select-Object -ExpandProperty Test -Unique).Count
    Write-Host ("    {0,-34} {1,3} dirs  {2,3} PNGs" -f $g.Name, $dirs, $g.Count)
}
foreach ($r in $copy) {
    Write-Host ("      {0}/{1}/{2}/{3}  ->  results/{2}/baselines/{3}   [{4}]" -f `
        $r.Workload, $r.Scope, $r.Test, $r.File, $r.SrcSha.Substring(0, 12))
}

Write-Host ""
Write-Host "C2b - SUPERSEDED, left in place (flat already holds this identity; flat wins)"
Write-Host ("  {0} PNGs stay at results/<suite>/<test>/baselines/   ({1} byte-identical to flat and therefore carry no information, {2} genuinely differ)" -f `
    @($park).Count, @($identical).Count, @($differing).Count)
Write-Host "  NOT copied anywhere: invariant 1 means these are already the parked generation."
foreach ($r in ($park | Sort-Object Workload, Test, Scope, File)) {
    $tag = $(if ($r.SrcSha -eq $r.DstSha) { 'same-as-flat' } else { 'DIFFERS' })
    Write-Host ("      {0}/{1}/{2}/{3}  [{4}]" -f $r.Workload, $r.Scope, $r.Test, $r.File, $tag)
}
if (@($differing).Count -gt 0) {
    Write-Host ""
    Write-Host ("  PREDICTED, LOUD CONSEQUENCE OF C3 - stated before running, not after: {0} checkpoints" -f @($differing).Count)
    Write-Host "  across the tests above will compare against the FLAT image once the cutover lands."
    Write-Host "  All are 960x540, so a real diff percentage decides, not a size mismatch. Expect up to"
    Write-Host "  that many Failed on the first --suite smoke / --suite buyout-canonical run, each with a"
    Write-Host "  diff written to results/<test>/diffs/, each clearing with one inspect-then-approve."
    Write-Host "  That is the correct shape of being wrong about 'flat wins'."
}

if (-not $Apply) {
    Write-Host ""
    Write-Host "DRY RUN - nothing written. Re-run with -Apply."
    exit 0
}

# --- apply -------------------------------------------------------------------
Write-Host ""
Write-Host "applying..."
$copied = 0
foreach ($r in $copy) {
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $r.Dst) | Out-Null
    if (Test-Path $r.Dst) { throw "REFUSING: $($r.Dst) appeared since the plan was built." }
    Copy-Item -Path $r.Src -Destination $r.Dst
    # The approval did not move in time, only in space.
    (Get-Item $r.Dst).LastWriteTimeUtc = (Get-Item $r.Src).LastWriteTimeUtc
    if ((Get-Sha $r.Dst) -ne $r.SrcSha) { throw "hash mismatch after copying to $($r.Dst)" }
    $copied++
}

Write-Host ("  copied      : {0}" -f $copied)
Write-Host ("  left alone  : {0} superseded nested PNGs (C2b - already the parked generation)" -f @($park).Count)

# --- post-move arithmetic, stated so a silent shortfall is impossible --------
$flatDirs = @(Get-ChildItem -Path $WorkloadsRoot -Directory -Recurse -Filter 'baselines' |
    Where-Object {
        $rel = $_.FullName.Substring($WorkloadsRoot.Length).TrimStart('\').Replace('\', '/')
        # <workload>/results/<test>/baselines  -> 4 segments
        ($rel -split '/').Count -eq 4
    })
$flatPngs = ($flatDirs | ForEach-Object { Get-ChildItem $_.FullName -Filter '*.png' -File }).Count

Write-Host ""
Write-Host ("  flat baseline dirs now : {0}" -f @($flatDirs).Count)
Write-Host ("  flat baseline PNGs now : {0}" -f $flatPngs)
Write-Host "  nested originals were NOT removed - this step is reversible by deleting what it added."
Write-Host ""
Write-Host "NEXT, still under the old code, and this green IS the migration's proof:"
Write-Host "  canary baselines verify --workload <w> --layout flat   -> must be green for all five"

exit 0
