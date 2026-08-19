<#
.SYNOPSIS
  Survey what this machine HAS, before and outside of any application.

.DESCRIPTION
  Deployment campaign, Stage A. `canary env` answers "what did the application load",
  which is the right question but can only be asked once the application runs. This answers
  the question that comes before it: what is on this machine at all.

  Everything here is decidable from the filesystem and the registry - no app is launched,
  nothing is installed, nothing is changed. It is the input a setup/reinstall pass needs in
  order to decide what to do, and it is safe to run on a machine in any state, including one
  where nothing works yet.

  Emits JSON on stdout, or to -OutFile.

.PARAMETER OutFile
  Write the survey here instead of stdout.

.EXAMPLE
  powershell -File scripts\machine-survey.ps1 -OutFile survey.json
#>
[CmdletBinding()]
param([string] $OutFile)

$ErrorActionPreference = 'Continue'   # a survey reports what it cannot read; it does not abort

function Try-Get {
    param([scriptblock] $Block, $Fallback = $null)
    try { & $Block } catch { $Fallback }
}

# --- identity ---------------------------------------------------------------
$identity = [ordered]@{
    machineName = $env:COMPUTERNAME
    user        = $env:USERNAME
    os          = Try-Get { (Get-CimInstance Win32_OperatingSystem).Caption } $null
    osVersion   = [System.Environment]::OSVersion.VersionString
    arch        = $env:PROCESSOR_ARCHITECTURE
    surveyedUtc = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
}

# --- toolchain --------------------------------------------------------------
$dotnetSdks = Try-Get { (& dotnet --list-sdks 2>$null) -split "`r?`n" | Where-Object { $_ } } @()
$dotnetRts  = Try-Get { (& dotnet --list-runtimes 2>$null) -split "`r?`n" | Where-Object { $_ } } @()
$toolchain = [ordered]@{
    dotnet     = [bool](Get-Command dotnet -ErrorAction SilentlyContinue)
    dotnetSdks = @($dotnetSdks)
    dotnetRuntimes = @($dotnetRts | Where-Object { $_ -match 'Microsoft\.(NETCore|WindowsDesktop)' })
    git        = Try-Get { (& git --version 2>$null) } $null
    python     = Try-Get { (& python --version 2>$null) } $null
    node       = Try-Get { (& node --version 2>$null) } $null
    yak        = $null   # filled below once Rhino is located
}

# --- Rhino ------------------------------------------------------------------
# Program Files first (the normal install), then the registry, because a machine can have
# Rhino without the default path and reporting "absent" there would send a setup pass
# installing a second copy.
$rhinoDirs = @(Get-ChildItem 'C:\Program Files' -Directory -Filter 'Rhino *' -ErrorAction SilentlyContinue |
    ForEach-Object {
        $exe = Join-Path $_.FullName 'System\Rhino.exe'
        [ordered]@{
            dir     = $_.FullName
            exe     = if (Test-Path $exe) { $exe } else { $null }
            version = if (Test-Path $exe) { (Get-Item $exe).VersionInfo.ProductVersion } else { $null }
        }
    })
$rhinoReg = @(Try-Get {
    Get-ChildItem 'HKLM:\SOFTWARE\McNeel\Rhinoceros' -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty PSChildName
} @())

if ($rhinoDirs.Count -gt 0 -and $rhinoDirs[0].dir) {
    $yakExe = Join-Path $rhinoDirs[0].dir 'System\yak.exe'
    if (Test-Path $yakExe) { $toolchain.yak = $yakExe }
}

# --- Grasshopper / plug-in surface -----------------------------------------
# The three places a .gha can legitimately live, plus whatever the operator added by hand.
$ghLibraries = Join-Path $env:APPDATA 'Grasshopper\Libraries'
$packagesRoot = Join-Path $env:APPDATA 'McNeel\Rhinoceros\packages'

function Describe-Folder([string]$path) {
    if (-not (Test-Path $path)) { return [ordered]@{ path = $path; exists = $false; files = @() } }
    $files = @(Get-ChildItem $path -Recurse -Include *.gha, *.rhp -ErrorAction SilentlyContinue |
        ForEach-Object {
            [ordered]@{
                name    = $_.Name
                path    = $_.FullName
                version = Try-Get { $_.VersionInfo.FileVersion } $null
                sizeKB  = [math]::Round($_.Length / 1KB)
                writtenUtc = $_.LastWriteTimeUtc.ToString('yyyy-MM-ddTHH:mm:ssZ')
                # A .gha downloaded from the internet and not unblocked will not load, and
                # nothing in the app reports why. Cheap to check here, invisible later.
                blocked = [bool](Get-Item -Path "$($_.FullName):Zone.Identifier" -ErrorAction SilentlyContinue)
            }
        })
    [ordered]@{ path = $path; exists = $true; files = $files }
}

$grasshopper = [ordered]@{
    librariesFolder = Describe-Folder $ghLibraries
    packages        = Describe-Folder $packagesRoot
    # Grasshopper's own settings file records the developer-settings folders it scans.
    # This is the ONLY way to see them without launching Rhino, and a dev folder here
    # shadows a deployed install - the single most common install-looks-fine failure.
    settingsFiles   = @(Get-ChildItem (Join-Path $env:APPDATA 'Grasshopper') -Filter '*.xml' -ErrorAction SilentlyContinue |
                        ForEach-Object { $_.FullName })
}

# --- content / repos / payload ---------------------------------------------
$repoRoot = 'C:\Repos'
$repos = @()
if (Test-Path $repoRoot) {
    $repos = @(Get-ChildItem $repoRoot -Directory -ErrorAction SilentlyContinue | ForEach-Object {
        $isGit = Test-Path (Join-Path $_.FullName '.git')
        [ordered]@{
            name   = $_.Name
            path   = $_.FullName
            isGit  = $isGit
            branch = if ($isGit) { Try-Get { (& git -C $_.FullName rev-parse --abbrev-ref HEAD 2>$null) } $null } else { $null }
            head   = if ($isGit) { Try-Get { (& git -C $_.FullName rev-parse --short HEAD 2>$null) } $null } else { $null }
            dirty  = if ($isGit) { [bool](Try-Get { (& git -C $_.FullName status --porcelain 2>$null) } $null) } else { $null }
        }
    })
}

$driveCandidates = @('G:\My Drive\Builds', 'G:\My Drive\Builds\_yak', 'G:\My Drive\claude-share')
$drive = @($driveCandidates | ForEach-Object {
    [ordered]@{ path = $_; exists = (Test-Path $_) }
})

$yakPackages = @()
$yakDir = 'G:\My Drive\Builds\_yak'
if (Test-Path $yakDir) {
    $yakPackages = @(Get-ChildItem $yakDir -Filter *.yak -ErrorAction SilentlyContinue |
        ForEach-Object { [ordered]@{ name = $_.Name; sizeKB = [math]::Round($_.Length / 1KB) } })
}

# --- canary itself ----------------------------------------------------------
# Where could this machine get a canary.exe? A dev tree, a Drive payload, or nowhere -
# and "nowhere" is the answer that decides whether a QC session is even possible today.
$canaryCandidates = @(
    "$repoRoot\Canary\src\Canary.Harness\bin\Debug\net8.0-windows\canary.exe",
    "$repoRoot\Canary\src\Canary.Harness\bin\Release\net8.0-windows\canary.exe",
    'G:\My Drive\Builds\Canary\canary.exe',
    'G:\My Drive\Builds\canary\canary.exe'
) | ForEach-Object { [ordered]@{ path = $_; exists = (Test-Path $_) } }

$survey = [ordered]@{
    identity     = $identity
    toolchain    = $toolchain
    rhino        = [ordered]@{ installs = @($rhinoDirs); registryVersions = $rhinoReg }
    grasshopper  = $grasshopper
    repos        = $repos
    drive        = $drive
    yakPackages  = $yakPackages
    canary       = @($canaryCandidates)
}

$json = $survey | ConvertTo-Json -Depth 8
if ($OutFile) {
    $json | Set-Content -Path $OutFile -Encoding utf8
    Write-Host "survey written to $OutFile"
} else {
    $json
}
