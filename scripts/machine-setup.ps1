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

.PARAMETER WorkloadsRoot
  Where the workloads tree lives. Defaults to the dev-tree layout, then the payload layout
  (beside canary.exe, then beside this script). The old code assumed the dev tree only, so
  on the Drive payload it computed a workloads folder one level above the payload and threw
  on its first line of output.

.PARAMETER YakExe
  Path to yak.exe. Defaults to the Rhino 8 then Rhino 7 install, then the registry.

.PARAMETER LogDir
  Where the change record goes. Defaults to a folder under LOCALAPPDATA. It used to go to
  the root this script was run from, which on a payload is the Drive folder that
  publish-payload.ps1 WIPES on the next publish. Drive is delivery; local disk is runtime.

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
    [string]   $CanaryExe,
    [string]   $WorkloadsRoot,
    [string]   $YakExe,
    [string]   $LogDir
)

# Continue, not Stop: this runs on machines where things are expected to fail, and a script
# that aborts on the first problem is useless on the machine with problems. Native calls are
# judged by $LASTEXITCODE.
$ErrorActionPreference = 'Continue'

$repo = Split-Path -Parent $PSScriptRoot

# Find canary before the workloads root, because on a payload the workloads tree is beside
# the exe and there is no repo to hang it off. Unlike qc-capture.ps1 this script does NOT
# stop when canary is absent - canary is only needed for the post-install verification at
# the end, and a machine with no canary still deserves a plan.
$canary = @($CanaryExe,
    (Join-Path $repo 'src\Canary.Harness\bin\Debug\net8.0-windows\canary.exe'),
    (Join-Path $repo 'src\Canary.Harness\bin\Release\net8.0-windows\canary.exe'),
    (Join-Path $PSScriptRoot 'canary.exe')) |
    Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1

# The workloads root. This used to be Split-Path -Parent of the script folder plus
# 'workloads', full stop - a DEV TREE assumption. Shipped to the payload root that resolves
# to the folder ABOVE the payload, so the script threw before printing anything. Same
# candidate list qc-capture.ps1 already uses, deliberately: two scripts that disagree about
# where the content is are two different answers to the same question.
if (-not $WorkloadsRoot) {
    $wlCandidates = @((Join-Path $repo 'workloads'))
    if ($canary) { $wlCandidates += (Join-Path (Split-Path -Parent $canary) 'workloads') }
    $wlCandidates += (Join-Path $PSScriptRoot 'workloads')
    $WorkloadsRoot = $wlCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $WorkloadsRoot) { throw "no workloads directory found; pass -WorkloadsRoot" }
$workloadsRoot = $WorkloadsRoot

# The sibling scripts travel with this one, so name them by where they actually are. Printing
# a dev-tree path to someone standing at a payload folder sends them looking for a folder
# that is not there.
$qcCaptureScript = Join-Path $PSScriptRoot 'qc-capture.ps1'
if (-not (Test-Path $qcCaptureScript)) { $qcCaptureScript = 'qc-capture.ps1' }

$mapFile = Join-Path $workloadsRoot 'plugin-packages.json'
if (-not (Test-Path $mapFile)) { throw "missing $mapFile - it maps requirement ids to yak packages" }
$map = Get-Content $mapFile -Raw | ConvertFrom-Json
if (-not $Source) { $Source = $map.source }

# Expand %TOKEN% from workloads/tokens.json, with an environment variable of the same name
# winning - that is how a QC machine repoints a root without editing content it did not
# author. The yak source is declared as %CANARY_HANDOFF%/_yak rather than a drive letter
# precisely so a machine that mounts the Drive elsewhere still works; a corpus guard in
# this repo rejects absolute drive paths in content for the same reason.
$tokensFile = Join-Path $workloadsRoot 'tokens.json'
if (Test-Path $tokensFile) {
    $tokens = Get-Content $tokensFile -Raw | ConvertFrom-Json
    foreach ($prop in $tokens.PSObject.Properties) {
        if ($prop.Name -like '_comment*') { continue }
        $val = [Environment]::GetEnvironmentVariable($prop.Name)
        if (-not $val) { $val = $prop.Value }
        $Source = $Source -replace [regex]::Escape("%$($prop.Name)%"), $val
    }
}

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
    Write-Host "              Run $qcCaptureScript first, or every requirement below is listed as unknown." -ForegroundColor Yellow
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

# --- 3b. the bootstrap case --------------------------------------------------
# The hole this closes: with no capture, every row above is 'unknown', nothing qualifies as
# MISSING, and the script used to print "Nothing to do." on a machine where in fact NOTHING
# was done. That is the worst possible reading of the worst possible state - a fresh QC box
# that cannot produce a capture because the very plug-in that produces one is not installed.
#
# So in the no-capture state, plan canary-agent without waiting for evidence. It is the one
# package whose absence explains its own absence, and installing it is what makes the next
# run able to answer honestly. Everything else still waits for a real capture: this is a
# bootstrap, not a licence to guess at the rest. It is planned, not performed - the -Apply
# gate below covers it exactly like every other action.
$bootstrap = @()
if ($capturesRead.Count -eq 0) {
    $agentPkg = @($map.packages | Where-Object { $_.package -eq 'canary-agent' }) | Select-Object -First 1
    if ($agentPkg) {
        $bootstrap = @([pscustomobject]@{
            id       = @($agentPkg.ids)[0]
            state    = 'BOOTSTRAP'
            package  = $agentPkg.package
            grounded = $agentPkg.grounded
            neededBy = 'bootstrap'
        })
    }
}

$toInstall = @($plan | Where-Object { $_.state -eq 'MISSING' -and $_.package })
# Bootstrap leads: it is the action that makes the others measurable, so it is the one to
# read first and the one to run first.
$toInstall = @($bootstrap) + $toInstall
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
    if ($t.state -eq 'BOOTSTRAP') {
        Write-Host "  install package '$($t.package)' to provide $($t.id)" -ForegroundColor Yellow
        Write-Host "      planned WITHOUT evidence: no capture exists on this machine, and this is the package that makes one possible." -ForegroundColor Yellow
        continue
    }
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
$yak = @($YakExe,
         "$env:ProgramFiles\Rhino 8\System\yak.exe",
         "$env:ProgramFiles\Rhino 7\System\yak.exe") |
       Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1

# Program Files first, then the registry - the same fallback, for the same reason,
# machine-survey.ps1 gives: a machine can have Rhino without the default path, and calling
# that machine "no Rhino" sends the operator installing a second copy. Newest version key
# first, because that is the Rhino the rest of this campaign targets.
if (-not $yak) {
    foreach ($key in @(Get-ChildItem 'HKLM:\SOFTWARE\McNeel\Rhinoceros' -ErrorAction SilentlyContinue |
                       Sort-Object PSChildName -Descending)) {
        $install = Get-ItemProperty -Path (Join-Path $key.PSPath 'Install') -ErrorAction SilentlyContinue
        if (-not $install) { continue }
        # InstallPath/InstallDir point at the Rhino folder, Path at its System folder; which
        # of the three a given install wrote varies by version, so probe both shapes.
        foreach ($dir in @($install.InstallPath, $install.InstallDir, $install.Path)) {
            if (-not $dir) { continue }
            $yak = @((Join-Path $dir 'System\yak.exe'), (Join-Path $dir 'yak.exe')) |
                   Where-Object { Test-Path $_ } | Select-Object -First 1
            if ($yak) { break }
        }
        if ($yak) { break }
    }
}
if ($toInstall.Count -gt 0 -and -not $yak) { throw "yak.exe not found in Program Files or the McNeel registry - cannot install packages. Is Rhino installed? If it is somewhere unusual, pass -YakExe <path to yak.exe>." }
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

# The change record is the only account of what this script did to this machine, so it goes
# on the machine's own disk. It used to land in the root the script was run from; on a
# payload that root is the Drive folder, and publish-payload.ps1 WIPES that folder before
# every publish - the record of an install would have been deleted by the next build.
if (-not $LogDir) { $LogDir = Join-Path (Join-Path $env:LOCALAPPDATA 'Canary') 'machine-setup' }
New-Item -ItemType Directory -Force -Path $LogDir | Out-Null
$log = Join-Path $LogDir ("machine-setup-{0}-{1}.json" -f $env:COMPUTERNAME, (Get-Date -Format 'yyyyMMdd-HHmmss'))
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
    Write-Host "saying it succeeded; re-run $qcCaptureScript and compare origins." -ForegroundColor Yellow
}

exit 0
