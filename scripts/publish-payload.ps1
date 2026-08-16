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
param([switch]$AllowDirty)
$ErrorActionPreference = "Stop"

$SRC  = "C:\Repos\Canary"
$DST  = "G:\My Drive\Builds\Canary"
$stage = Join-Path $env:TEMP "canary-publish-$(Get-Random)"

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
Remove-Item "$DST\*" -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force "$DST", "$DST\agent" | Out-Null
Copy-Item "$stage\harness\*" $DST -Recurse -Force
Copy-Item "$agentDir\*" "$DST\agent" -Recurse -Force -Exclude *.pdb
# the workloads tree the harness resolves before it can run anything
foreach ($w in @("rhino")) {
    $wd = "$DST\workloads\$w"
    New-Item -ItemType Directory -Force "$wd\tests", "$wd\fixtures", "$wd\suites" | Out-Null
    Copy-Item "$SRC\workloads\$w\workload.json" $wd -Force
    Copy-Item "$SRC\workloads\$w\tests\bristle-*.json" "$wd\tests" -Force
    Copy-Item "$SRC\workloads\$w\fixtures\*" "$wd\fixtures" -Recurse -Force
    Copy-Item "$SRC\workloads\$w\suites\bristle.json" "$wd\suites" -Force
}
Copy-Item "$SRC\scripts\verify-payload.ps1" $DST -Force

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
Get-ChildItem $DST -Recurse -File | ForEach-Object {
    $rel = $_.FullName.Substring($DST.Length).TrimStart('\')
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
$manifest | ConvertTo-Json -Depth 5 | Set-Content "$DST\MANIFEST.json" -Encoding utf8

# ------------------------------------------------------------ self-verify
& powershell -NoProfile -ExecutionPolicy Bypass -File "$DST\verify-payload.ps1" -Root $DST
if ($LASTEXITCODE) { throw "the payload FAILED its own verification - BUILD_INFO not written" }

$stamp = @(
    "Canary Drive payload",
    "staged   : $(Get-Date -Format yyyy-MM-dd)",
    "source   : $branch @ $commit$(if ($dirty) { ' (DIRTY)' })",
    "layout   : harness (net8.0) at the root; Rhino agent (net48) under agent\",
    "verified : $($files.Count) files, hashes + target frameworks (MANIFEST.json)",
    "verify   : powershell -File verify-payload.ps1"
) -join "`n"
[IO.File]::WriteAllText("$DST\BUILD_INFO.txt", $stamp, (New-Object System.Text.UTF8Encoding($false)))
Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "published $branch @ $commit - $($files.Count) files, verified"
