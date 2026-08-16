# Verify a Canary payload before trusting it (campaign Phase 2).
#
# Checks THREE things, in the order that would have caught machine 2's
# incident soonest:
#   1. TARGET FRAMEWORK CONSISTENCY - every managed assembly at the payload
#      root must be net8.0, and everything under agent\ must be net48. The
#      machine-2 payload passed a file-list check and a dependency-graph check
#      and was still broken, because the FILES were right and their FRAMEWORK
#      was wrong (a net48 System.Text.Json.dll sitting where the net8.0 one
#      belonged, demanding Microsoft.Bcl.AsyncInterfaces).
#   2. completeness + integrity against the manifest's source hashes;
#   3. provenance - a dirty-tree stamp is reported, not hidden.
param([string]$Root = $PSScriptRoot)
$ErrorActionPreference = "Stop"

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
exit 0
