<#
.SYNOPSIS
    Generates Canary test JSONs from CPig Slop test definitions.

.DESCRIPTION
    Walks `<CPigRoot>/research/slop_tests/*.json` and emits matching
    `cpig-NN-slug.json` test definitions under `<CanaryRoot>/workloads/rhino/tests/`.
    Each generated test loads `cpig_slop_loader.gh`, sets the `JsonPath` panel to
    the absolute path of the source Slop JSON, pulses the `Build` toggle, waits
    for the GH solution to complete, then asserts on Slop's `SlopSuccess` and
    `SlopLog` output panels.

    Mirrors the conventions in:
      - Canary/spec/CPIG_WORKLOAD.md
      - CPig/spec/CANARY.md

    The script is idempotent — running it again overwrites existing
    cpig-*.json files. Hand-tuned test JSONs should not be left in place
    where the script will clobber them; instead, fork the test name (e.g.
    `cpig-09-implicit-advanced-strict.json`) so it doesn't match the
    `NN_slug.json` -> `cpig-NN-slug.json` derivation.

.PARAMETER CPigRoot
    Path to the CPig repo root. Default: C:\Repos\CPig

.PARAMETER CanaryRoot
    Path to the Canary repo root. Default: C:\Repos\Canary

.PARAMETER WhatIf
    Print the actions that would be taken; don't write files.

.EXAMPLE
    .\cpig-test-from-slop.ps1
    Generates all 17 cpig-*.json files using the default paths.

.EXAMPLE
    .\cpig-test-from-slop.ps1 -WhatIf
    Dry-run.
#>

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$CPigRoot = 'C:\Repos\CPig',
    [string]$CanaryRoot = 'C:\Repos\Canary'
)

$ErrorActionPreference = 'Stop'

$slopTestsDir = Join-Path $CPigRoot 'research\slop_tests'
$canaryTestsDir = Join-Path $CanaryRoot 'workloads\rhino\tests'

if (-not (Test-Path $slopTestsDir)) {
    throw "Slop tests dir not found: $slopTestsDir"
}
if (-not (Test-Path $canaryTestsDir)) {
    New-Item -ItemType Directory -Path $canaryTestsDir | Out-Null
}

# Map "16_field_evaluate.json" -> "cpig-16-field-evaluate"
function Convert-ToCanaryName([string]$slopFileName) {
    $base = [System.IO.Path]::GetFileNameWithoutExtension($slopFileName)  # e.g. "16_field_evaluate"
    return "cpig-" + ($base -replace '_', '-')                            # "cpig-16-field-evaluate"
}

# Pull the human description from a Slop JSON's title scribble (first node
# whose type is "scribble"). Falls back to the file name if no scribble.
function Get-SlopDescription([string]$slopPath) {
    try {
        $obj = Get-Content -Raw $slopPath | ConvertFrom-Json
        $title = $obj.nodes | Where-Object { $_.type -eq 'scribble' } | Select-Object -First 1
        if ($title -and $title.name) { return $title.name }
    }
    catch { }
    return [System.IO.Path]::GetFileNameWithoutExtension($slopPath)
}

$slopFiles = Get-ChildItem -Path $slopTestsDir -Filter '*.json' | Sort-Object Name
Write-Host "Found $($slopFiles.Count) Slop test definitions in $slopTestsDir"

$generated = 0
foreach ($slopFile in $slopFiles) {
    $canaryTestName = Convert-ToCanaryName $slopFile.Name
    $description    = Get-SlopDescription $slopFile.FullName
    $slopPathAbs    = $slopFile.FullName -replace '\\', '/'

    $outputPath = Join-Path $canaryTestsDir "$canaryTestName.json"

    # Build the test JSON. Keep this template aligned with
    # Canary/spec/CPIG_WORKLOAD.md — change both together.
    $testDef = [ordered]@{
        name        = $canaryTestName
        workload    = 'rhino'
        description = "$description (auto-generated from $($slopFile.Name))"
        runMode     = 'shared'
        setup       = [ordered]@{
            file     = 'fixtures/cpig_slop_loader.gh'
            viewport = [ordered]@{
                projection  = 'Perspective'
                displayMode = 'Shaded'
                width       = 800
                height      = 600
            }
        }
        actions     = @(
            [ordered]@{ type = 'WaitForGrasshopperSolution'; timeoutMs = 5000 },
            [ordered]@{ type = 'GrasshopperSetToggle'; nickname = 'Build'; value = $false },
            [ordered]@{ type = 'GrasshopperSetToggle'; nickname = 'Cleanup'; value = $true },
            [ordered]@{ type = 'WaitForGrasshopperSolution'; timeoutMs = 5000 },
            [ordered]@{ type = 'GrasshopperSetToggle'; nickname = 'Cleanup'; value = $false },
            [ordered]@{ type = 'WaitForGrasshopperSolution'; timeoutMs = 2000 },
            [ordered]@{ type = 'GrasshopperSetPanelText'; nickname = 'JsonPath'; text = $slopPathAbs },
            [ordered]@{ type = 'GrasshopperSetToggle'; nickname = 'Build'; value = $true },
            [ordered]@{ type = 'WaitForGrasshopperSolution'; timeoutMs = 30000 }
        )
        checkpoints = @(
            [ordered]@{
                name      = 'post-build'
                atTimeMs  = 5000
                tolerance = 0.02
            }
        )
        asserts     = @(
            [ordered]@{ type = 'PanelEquals'; nickname = 'SlopSuccess'; text = 'True' },
            [ordered]@{ type = 'PanelDoesNotContain'; nickname = 'SlopLog'; text = 'FATAL' },
            [ordered]@{ type = 'PanelDoesNotContain'; nickname = 'SlopLog'; text = '!!!' }
        )
    }

    # JsonPath in the actions array uses forward-slashes; we want the
    # written file to use forward-slashes too so it's portable across
    # Windows tooling that escapes backslashes. ConvertTo-Json handles
    # this if we already emit forward-slashes.
    $json = $testDef | ConvertTo-Json -Depth 10

    if ($PSCmdlet.ShouldProcess($outputPath, "Write Canary test JSON ($description)")) {
        Set-Content -Path $outputPath -Value $json -Encoding UTF8
        $generated++
    }
}

Write-Host "Generated $generated test JSON file(s) in $canaryTestsDir"
Write-Host ""
Write-Host "Next steps:"
Write-Host "  1. Manually author workloads/rhino/fixtures/cpig_slop_loader.gh per spec/CPIG_WORKLOAD.md."
Write-Host "  2. Run 'canary run --workload rhino --filter cpig-*' once with no baselines."
Write-Host "  3. Inspect candidate PNGs and 'canary baseline approve <name>' those that look correct."
