# bristle-22 seeder + runner (E7a commit purity, plan 2026-08-13).
#
# Seeds the engine with the EXACT job set the test discriminates between:
#   1. a COMMIT        origin "app [middot] e7a-purity"   (oldest)
#   2. a COMMIT        origin "app [middot] final draft"  <- the word 'draft' in a
#                      LEGIT commit label: the first-cut substring exclusion
#                      silently swallowed these (review 2026-08-13)
#   3. a PROXY DRAFT   origin "app [middot] draft", preview_px 480 (reserved origin)
#   4. a PROXY job     origin "app [middot] sneaky-proxy", preview_px 480 (newest) -
#                      a commit-looking origin whose edit.json carries the proxy
#                      flag: BR_Watch's manifest reader must skip it
# then runs the canary. BR_Watch must pick job 2 - newer than the e7a-purity
# commit, not excluded despite containing 'draft', while 3 (reserved origin)
# and 4 (manifest proxy flag) are both skipped even though they are NEWER.
#
# ENCODING (review 2026-08-13): this file is PURE ASCII on purpose. The first
# cut had literal UTF-8 middots, which PS 5.1 (no BOM => ANSI parse) misread as
# 'A-hat middot' and Invoke-RestMethod then re-mangled back to valid UTF-8 by
# accident - two bugs cancelling. The middot is built from its codepoint and
# the body goes over the wire as explicit UTF-8 bytes.
#
# Prereq (same as every bristle test): the engine running on :8377.
param([switch]$SeedOnly)

$ErrorActionPreference = "Stop"
$base = "http://127.0.0.1:8377"
$market = "C:/Repos/Bristle/tests/fixtures/scenes/market-color.jpg"
$mid = [string][char]0x00B7   # middot, codepoint-built so the source stays ASCII

function Submit($body) {
    $json = $body | ConvertTo-Json -Depth 8
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($json)
    $r = Invoke-RestMethod -Uri "$base/jobs" -Method Post `
        -ContentType "application/json; charset=utf-8" -Body $bytes
    return $r.job_id
}
function WaitDone($jid) {
    for ($i = 0; $i -lt 60; $i++) {
        $s = (Invoke-RestMethod -Uri "$base/jobs/$jid").state
        if ($s -eq "done") { return }
        if ($s -eq "failed") { throw "seed job $jid failed" }
        Start-Sleep -Milliseconds 500
    }
    throw "seed job $jid never finished"
}

# oldest -> newest; every trap is NEWER than the job Watch must pick
$purity = Submit @{ type = "edit_v1"; image_path = $market; origin = "app $mid e7a-purity";
    params = @{ recipe = @{ ops = @(@{ op = "grayscale" }) }; canvas_mm = @(180, 120) } }
WaitDone $purity
$commit = Submit @{ type = "edit_v1"; image_path = $market; origin = "app $mid final draft";
    params = @{ recipe = @{ ops = @(@{ op = "grayscale" }) }; canvas_mm = @(180, 120) } }
WaitDone $commit
$draft = Submit @{ type = "edit_v1"; image_path = $market; origin = "app $mid draft";
    params = @{ recipe = @{ ops = @(@{ op = "invert" }) }; canvas_mm = @(180, 120);
                preview_px = 480 } }
WaitDone $draft
$sneaky = Submit @{ type = "edit_v1"; image_path = $market; origin = "app $mid sneaky-proxy";
    params = @{ recipe = @{ ops = @(@{ op = "invert" }) }; canvas_mm = @(180, 120);
                preview_px = 480 } }
WaitDone $sneaky
Write-Host "seeded: purity=$purity commit=$commit draft=$draft sneaky=$sneaky (newest)"
Write-Host "Watch must pick $commit (final draft) - never $draft / $sneaky"

if (-not $SeedOnly) {
    Set-Location C:\Repos\Canary   # canary resolves workloads relative to cwd
    & "C:\Repos\Canary\src\Canary.Harness\bin\Debug\net8.0-windows\canary.exe" `
        run --workload rhino --test bristle-22-commit-purity --keep-open --headless
    exit $LASTEXITCODE
}
