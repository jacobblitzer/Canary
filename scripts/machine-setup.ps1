<#
.SYNOPSIS
  Work out what this machine is missing, and — only when told to — install it.

.DESCRIPTION
  Deployment campaign, Stage B. The 210 declared requirements say what each workload NEEDS;
  workloads/plugin-packages.json says which yak package PROVIDES each one; a captured
  environment.json says what the host actually HAS. This script is the join: needed − had =
  install list.

  DRY RUN BY DEFAULT. Nothing is installed, unblocked or changed unless you pass -Apply.
  That is not politeness — on a QC machine the plan itself is the deliverable, and an
  install performed before the machine was measured has destroyed the evidence it existed to
  provide.

  It never uninstalls, never overwrites a file it did not create, and never edits
  Grasshopper's Developer Settings. A developer folder registered there SHADOWS a deployed
  install, so changing it silently is how an install "succeeds" while old code keeps running
  — if one is in the way, this script says so and stops.

  VERIFICATION IS THE POINT. With -Apply it captures the environment before and after and
  diffs them, because an install is not verified by the installer reporting success. It is
  verified by the application reporting where it actually loaded the thing from.

.PARAMETER Workloads
  Which workloads' declarations to satisfy. Default: every workload with a workload.json.

.PARAMETER Apply
  Actually perform the actions. Without this, nothing changes.

.PARAMETER Only
  Restrict to these package names (e.g. -Only slop,cpig).

.PARAMETER Source
  Yak package source folder. Defaults to the value in workloads/plugin-packages.json.

.PARAMETER SkipUnblock
  Do not offer to unblock web-blocked .gha/.rhp files.

.PARAMETER CanaryExe
  Path to canary.exe if it is not in a place this script can find.

.EXAMPLE
  powershell -File scripts\machine-setup.ps1
  powershell -File scripts\machine-setup.ps1 -Apply -Only slop,cpig
#>
[CmdletBinding()]
param(
    [string[]] $Workloads,
    [switch]   $Apply,
    [string[]] $Only,
    [string]   $Source,
    [switch]   $SkipUnblock,
    [string]   $CanaryExe
)

# Continue, not Stop: this runs on machines where things are expected to fail, and a script
# that aborts on the first problem is useless on the machine with problems. Native calls are
# judged by $LASTEXITCODE.
$ErrorActionPreference = 'Continue'

$repo = Split-Path -Parent $PSScriptRoot
$workloadsRoot = Join-Path $repo 'workloads'
if (-not (Test-Path $workloadsRoot)) { throw "no workloads directory at $workloadsRoot" }

$mapFile = Join-Path $workloadsRoot 'plugin-packages.json'
if (-not (Test-Path $mapFile)) { throw "missing $mapFile - it maps requirement ids to yak packages" }
$map = Get-Content $mapFile -Raw | ConvertFrom-Json
if (-not $Source) { $Source = $map.source }

$canary = @($CanaryExe,
    (Join-Path $repo 'src\Canary.Harness\bin\Debug\net8.0-windows\canary.exe'),
    (Join-Path $repo 'src\Canary.Harness\bin\Release\net8.0-windows\canary.exe'),
    (Join-Path $PSScriptRoot 'canary.exe')) |
    Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1

if (-not $Workloads) {
    $Workloads = Get-ChildItem $workloadsRoot -Directory -ErrorAction SilentlyContinue |
        Where-Object { Test-Path (Join-Path $_.FullName 'workload.json') } |
        Select-Object -ExpandProperty Name
}

# --- 1. what is DECLARED -----------------------------------------------------
# Read the requirements straight out of the content. Plugin ids only: file and service
# requirements are doctor's job and are not things a package manager can fix.
$declared = @{}
foreach ($w in $Workloads) {
    $files = @(Join-Path $workloadsRoot "$w\workload.json")
    $testsDir = Join-Path $workloadsRoot "$w\tests"
    if (Test-Path $testsDir) { $files += (Get-ChildItem $testsDir -Filter *.json | Select-Object -ExpandProperty FullName) }
    foreach ($f in $files) {
        if (-not (Test-Path $f)) { continue }
        try { $j = Get-Content $f -Raw | ConvertFrom-Json } catch { continue }   # unparsable tests are doctor's problem
        foreach ($r in @($j.requires)) {
            if ($r -and $r.kind -eq 'plugin' -and $r.id) {
                if (-not $declared.ContainsKey($r.id)) { $declared[$r.id] = New-Object System.Collections.Generic.HashSet[string] }
                [void]$declared[$r.id].Add($w)
            }
        }
    }
}

# --- 2. what the host HAS ----------------------------------------------------
$loaded = New-Object System.Collections.Generic.HashSet[string]
$capturesRead = @()
foreach ($w in $Workloads) {
    $cap = Join-Path $workloadsRoot "$w\results\environment.json"
    if (-not (Test-Path $cap)) { continue }
    try {
        $c = Get-Content $cap -Raw | ConvertFrom-Json
        foreach ($line in ($c.host.loaded -split "`r?`n")) {
            if ($line) { [void]$loaded.Add(($line -split '=')[0].Trim()) }
        }
        $capturesRead += $w
    } catch { }
}

Write-Host ""
Write-Host "machine-setup on $env:COMPUTERNAME" -ForegroundColor Cyan
Write-Host "  workloads : $($Workloads -join ', ')"
Write-Host "  declared  : $($declared.Count) distinct plug-in requirement(s)"
if ($capturesRead.Count -gt 0) {
    Write-Host "  captures  : $($capturesRead -join ', ')  ($($loaded.Count) libraries seen loaded)"
} else {
    # Load-bearing distinction: with no capture, "missing" cannot be computed. Saying
    # "everything is missing" would be a confident false answer.
    Write-Host "  captures  : NONE - cannot tell what is already installed." -ForegroundColor Yellow
    Write-Host "              Run scripts\qc-capture.ps1 first, or every requirement below is listed as unknown." -ForegroundColor Yellow
}
Write-Host "  mode      : $(if ($Apply) { 'APPLY - changes will be made' } else { 'DRY RUN - nothing will change' })" -ForegroundColor $(if ($Apply) { 'Yellow' } else { 'Green' })
Write-Host ""

# --- 3. join: declared vs loaded, then to a package --------------------------
$idToPackage = @{}
foreach ($p in $map.packages) { foreach ($i in $p.ids) { $idToPackage[$i] = $p } }

$plan = [System.Collections.Generic.List[object]]::new()
foreach ($id in ($declared.Keys | Sort-Object)) {
    $state = if ($capturesRead.Count -eq 0) { 'unknown' } elseif ($loaded.Contains($id)) { 'present' } else { 'MISSING' }
    $pkg = $idToPackage[$id]
    $plan.Add([pscustomobject]@{
        id        = $id
        state     = $state
        package   = if ($pkg) { $pkg.package } else { $null }
        grounded  = if ($pkg) { $pkg.grounded } else { $null }
        neededBy  = ($declared[$id] | Sort-Object) -join ','
    })
}

foreach ($row in $plan) {
    $c = switch ($row.state) { 'present' { 'Green' } 'MISSING' { 'Red' } default { 'DarkGray' } }
    $pkgTxt = if ($row.package) { $row.package } else { 'NO PACKAGE MAPPED' }
    Write-Host ("  {0,-9} {1,-28} -> {2,-14} [{3}]" -f $row.state, $row.id, $pkgTxt, $row.neededBy) -ForegroundColor $c
}

$toInstall = @($plan | Where-Object { $_.state -eq 'MISSING' -and $_.package })
if ($Only) { $toInstall = @($toInstall | Where-Object { $Only -contains $_.package }) }
$unmapped = @($plan | Where-Object { $_.state -eq 'MISSING' -and -not $_.package })

Write-Host ""
if ($unmapped.Count -gt 0) {
    Write-Host "$($unmapped.Count) missing requirement(s) have NO package mapping - a human must resolve these:" -ForegroundColor Yellow
    foreach ($u in $unmapped) { Write-Host "    $($u.id)  (needed by $($u.neededBy))" -ForegroundColor Yellow }
    Write-Host ""
}

# --- 4. blocked files --------------------------------------------------------
# A .gha downloaded and left web-blocked does not load, and nothing inside the app says why.
$blocked = @()
if (-not $SkipUnblock) {
    foreach ($dir in @((Join-Path $env:APPDATA 'Grasshopper\Libraries'),
                       (Join-Path $env:APPDATA 'McNeel\Rhinoceros\packages'))) {
        if (-not (Test-Path $dir)) { continue }
        $blocked += @(Get-ChildItem $dir -Recurse -Include *.gha, *.rhp -ErrorAction SilentlyContinue |
            Where-Object { Get-Item -Path "$($_.FullName):Zone.Identifier" -ErrorAction SilentlyContinue })
    }
    if ($blocked.Count -gt 0) {
        Write-Host "$($blocked.Count) web-blocked plug-in file(s) - these silently fail to load:" -ForegroundColor Yellow
        foreach ($b in $blocked) { Write-Host "    $($b.FullName)" -ForegroundColor Yellow }
        Write-Host ""
    }
}

if ($toInstall.Count -eq 0 -and $blocked.Count -eq 0) {
    Write-Host "Nothing to do." -ForegroundColor Green
    exit 0
}

Write-Host "PLAN:" -ForegroundColor Cyan
foreach ($t in $toInstall) {
    $warn = if ($t.grounded -ne 'capture') { "   (id is INFERRED, not observed - confirm against a real capture)" } else { '' }
    Write-Host "  install package '$($t.package)' to provide $($t.id)$warn"
}
foreach ($b in $blocked) { Write-Host "  unblock $($b.Name)" }

if (-not $Apply) {
    Write-Host ""
    Write-Host "Dry run. Re-run with -Apply to perform these." -ForegroundColor Green
    exit 0
}

# --- 5. apply ----------------------------------------------------------------
$yak = @("$env:ProgramFiles\Rhino 8\System\yak.exe",
         "$env:ProgramFiles\Rhino 7\System\yak.exe") |
       Where-Object { Test-Path $_ } | Select-Object -First 1
if ($toInstall.Count -gt 0 -and -not $yak) { throw "yak.exe not found - cannot install packages. Is Rhino installed?" }
if ($toInstall.Count -gt 0 -and -not (Test-Path $Source)) { throw "yak source '$Source' is not reachable" }

$record = [System.Collections.Generic.List[object]]::new()

foreach ($b in $blocked) {
    Write-Host "  unblocking $($b.Name)..."
    Unblock-File -Path $b.FullName
    $record.Add([pscustomobject]@{ action='unblock'; target=$b.FullName; ok=$true })
}

foreach ($t in $toInstall) {
    Write-Host "  installing $($t.package)..." -ForegroundColor Cyan
    & $yak install --source $Source $t.package
    $code = $LASTEXITCODE
    $record.Add([pscustomobject]@{ action='yak-install'; target=$t.package; providesId=$t.id; exitCode=$code; ok=($code -eq 0) })
    if ($code -ne 0) { Write-Host "    FAILED (exit $code)" -ForegroundColor Red }
}

$log = Join-Path $repo ("machine-setup-{0}-{1}.json" -f $env:COMPUTERNAME, (Get-Date -Format 'yyyyMMdd-HHmmss'))
[pscustomobject]@{
    machine = $env:COMPUTERNAME
    appliedUtc = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    source = $Source
    actions = $record
} | ConvertTo-Json -Depth 6 | Set-Content -Path $log -Encoding utf8
Write-Host ""
Write-Host "Change record written to $log"

# --- 6. prove it -------------------------------------------------------------
# The whole discipline in one step: re-ask the APPLICATION what it loaded and from where.
# A yak install reporting success proves nothing if a developer folder shadows it.
Write-Host ""
if ($canary) {
    Write-Host "Re-capturing the environment to verify what actually loaded..." -ForegroundColor Cyan
    foreach ($w in $Workloads) {
        & $canary env --workload $w
        if ($LASTEXITCODE -ne 0) { Write-Host "  env failed for $w (exit $LASTEXITCODE)" -ForegroundColor Yellow }
    }
    Write-Host ""
    Write-Host "Now compare against the pre-install capture, and check the ORIGIN column:" -ForegroundColor Cyan
    Write-Host "  a package that installed but still loads from a developer folder was SHADOWED."
} else {
    Write-Host "canary.exe not found - cannot verify. An install is not verified by the installer" -ForegroundColor Yellow
    Write-Host "saying it succeeded; re-run scripts\qc-capture.ps1 and compare origins." -ForegroundColor Yellow
}

exit 0
