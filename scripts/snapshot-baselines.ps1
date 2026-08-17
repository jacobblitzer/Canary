<#
.SYNOPSIS
  Phase 2b C0 - snapshot every baselines/ directory before the result-layout cutover.

.DESCRIPTION
  There is NO git recovery for baselines: .gitignore line 3 is `results/`, and
  `git ls-files 'workloads/**/baselines/*.png'` returns 0. These PNGs are the only
  record of what every visual test is supposed to look like. The only other way back
  is re-approving whatever the code currently renders, which launders a regression
  into the reference.

  So this snapshot is a precondition of the migration, not a courtesy. It writes a
  SHA256 manifest alongside the copy and re-reads the copy to verify it, because an
  unverified backup is a belief rather than a backup.

  Native tools are judged by EXIT CODE, never by stderr: robocopy reports progress on
  stderr and writes 1 for "files copied", which a strict PowerShell would treat as
  failure. Anything under 8 is success.

.PARAMETER Destination
  Root for the snapshot. Defaults under LOCALAPPDATA.

.PARAMETER AlsoCopyTo
  Optional second cold copy (e.g. the Drive handoff). One-time delivery-side copy of
  static PNGs - nothing at runtime ever reads or writes it, so it does not violate the
  "Drive is delivery, local disk is runtime" rule.

.PARAMETER ExpectFiles
  Refuse to report success unless exactly this many PNGs were snapshotted. Omit to
  take the live count as the expectation.
#>
[CmdletBinding()]
param(
    [string]$WorkloadsRoot = 'C:\Repos\Canary\workloads',
    [string]$Destination,
    [string]$AlsoCopyTo,
    [int]$ExpectFiles = 0
)

$ErrorActionPreference = 'Stop'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
if (-not $Destination) {
    $Destination = Join-Path $env:LOCALAPPDATA "canary-phase2b-backup\$stamp"
}

Write-Host "Phase 2b C0 - baseline snapshot"
Write-Host "  source : $WorkloadsRoot"
Write-Host "  dest   : $Destination"

# --- 1. enumerate the source of truth -----------------------------------------
$dirs = Get-ChildItem -Path $WorkloadsRoot -Directory -Recurse -Filter 'baselines' |
        Sort-Object FullName
$srcFiles = $dirs | ForEach-Object { Get-ChildItem -Path $_.FullName -Filter '*.png' -File }
$srcCount = @($srcFiles).Count
$srcBytes = ($srcFiles | Measure-Object -Property Length -Sum).Sum

Write-Host ("  found  : {0} baselines dirs, {1} PNGs, {2:N2} MB" -f `
    @($dirs).Count, $srcCount, ($srcBytes / 1MB))

if ($ExpectFiles -gt 0 -and $srcCount -ne $ExpectFiles) {
    throw "Expected $ExpectFiles baseline PNGs but the tree holds $srcCount. Refusing: the snapshot's premise is already wrong."
}
if ($srcCount -eq 0) { throw "No baseline PNGs found under $WorkloadsRoot - refusing to write an empty snapshot." }

# --- 2. copy, preserving <workload>/<relative path> ---------------------------
New-Item -ItemType Directory -Force -Path $Destination | Out-Null
$copied = 0
foreach ($d in $dirs) {
    $rel = $d.FullName.Substring($WorkloadsRoot.Length).TrimStart('\')
    $target = Join-Path $Destination $rel
    New-Item -ItemType Directory -Force -Path $target | Out-Null

    # /E subdirs, /NFL /NDL /NJH /NJS quiet, /R:2 bounded retries
    & robocopy $d.FullName $target '*.png' /E /NFL /NDL /NJH /NJS /R:2 /W:1 | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy failed (exit $LASTEXITCODE) for $($d.FullName)" }
    $copied += @(Get-ChildItem -Path $target -Filter '*.png' -File).Count
}

# --- 3. manifest, hashed from the COPY so it attests to what landed -----------
$manifest = Join-Path $Destination 'MANIFEST.sha256.txt'
$rows = foreach ($d in $dirs) {
    $rel = $d.FullName.Substring($WorkloadsRoot.Length).TrimStart('\')
    $target = Join-Path $Destination $rel
    foreach ($f in Get-ChildItem -Path $target -Filter '*.png' -File | Sort-Object Name) {
        $h = (Get-FileHash -Path $f.FullName -Algorithm SHA256).Hash
        '{0}  {1}' -f $h, (Join-Path $rel $f.Name)
    }
}
$rows | Set-Content -Path $manifest -Encoding utf8

# --- 4. verify the copy against the SOURCE, file by file ----------------------
$mismatch = @()
foreach ($d in $dirs) {
    $rel = $d.FullName.Substring($WorkloadsRoot.Length).TrimStart('\')
    foreach ($f in Get-ChildItem -Path $d.FullName -Filter '*.png' -File) {
        $copy = Join-Path (Join-Path $Destination $rel) $f.Name
        if (-not (Test-Path $copy)) { $mismatch += "MISSING  $rel\$($f.Name)"; continue }
        $a = (Get-FileHash -Path $f.FullName -Algorithm SHA256).Hash
        $b = (Get-FileHash -Path $copy       -Algorithm SHA256).Hash
        if ($a -ne $b) { $mismatch += "DIFFERS  $rel\$($f.Name)" }
    }
}

Write-Host ""
Write-Host ("  copied : {0} PNGs" -f $copied)
Write-Host ("  hashed : {0} rows -> MANIFEST.sha256.txt" -f @($rows).Count)

if ($mismatch.Count -gt 0) {
    $mismatch | Select-Object -First 20 | ForEach-Object { Write-Host "  $_" }
    throw "$($mismatch.Count) file(s) failed verification. The snapshot is NOT usable."
}
if ($copied -ne $srcCount) {
    throw "Copied $copied but source holds $srcCount. Refusing to call this a snapshot."
}

Write-Host "  VERIFIED: every source PNG is present in the snapshot with a matching SHA256."

# --- 5. optional second cold copy --------------------------------------------
if ($AlsoCopyTo) {
    $second = Join-Path $AlsoCopyTo "canary-phase2b-backup\$stamp"
    Write-Host ""
    Write-Host "  second copy -> $second"
    New-Item -ItemType Directory -Force -Path $second | Out-Null
    & robocopy $Destination $second /E /NFL /NDL /NJH /NJS /R:2 /W:1 | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "second-copy robocopy failed (exit $LASTEXITCODE)" }

    # Verify by re-reading the manifest at the destination, not by trusting robocopy.
    $secondManifest = Join-Path $second 'MANIFEST.sha256.txt'
    if (-not (Test-Path $secondManifest)) { throw "second copy has no manifest" }
    $bad = 0
    foreach ($line in Get-Content $secondManifest) {
        if ($line -notmatch '^([0-9A-Fa-f]{64})\s\s(.+)$') { continue }
        $want, $relPath = $Matches[1], $Matches[2]
        $p = Join-Path $second $relPath
        if (-not (Test-Path $p)) { $bad++; continue }
        if ((Get-FileHash -Path $p -Algorithm SHA256).Hash -ne $want) { $bad++ }
    }
    if ($bad -gt 0) { throw "$bad file(s) in the second copy do not match the manifest." }
    Write-Host ("  VERIFIED: second copy matches its manifest ({0} files)." -f @($rows).Count)
}

Write-Host ""
Write-Host "C0 COMPLETE. Snapshot root:"
Write-Host "  $Destination"

# Explicit, and load-bearing: robocopy returns 1 for "files were copied", and without
# this the script inherits that 1 as its own exit code - reporting failure on the happy
# path to anything that judges it by exit code (which is how native tools must be
# judged). Every early failure above throws, so reaching here means success.
exit 0
