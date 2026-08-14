# bristle regression-gate seeder + runner (E7d, 2026-08-14).
#
# bristle-17 and bristle-20 both assert BR_Watch picks up an app COMMIT
# ("picked up" in WatchLog). That commit used to be whatever the last app
# session left behind - FIXTURE ROT: the engine prunes jobs (keep_last), so
# after ~50 jobs of churn no app commit remains, Watch stays at "watching for
# app commits..." and the tests fail. Worse, keepOpenOnFailure holds a
# HEADLESS Rhino open forever, which reads as a hang (2026-08-14 diagnosis:
# two "wedged" 35-minute runs were exactly this).
#
# This script makes the prereq explicit, the bristle-22-seeder pattern:
# seed ONE full-res commit, wait done, then run the gate pair.
#
# ENCODING: pure ASCII on purpose (PS 5.1 ANSI-parse trap, see
# bristle-22-seed.ps1); middot via codepoint, body as explicit UTF-8 bytes.
#
# Prereq: the engine running on :8377.
param([switch]$SeedOnly)

$ErrorActionPreference = "Stop"
$base = "http://127.0.0.1:8377"
$market = "C:/Repos/Bristle/tests/fixtures/scenes/market-color.jpg"
$mid = [string][char]0x00B7

$json = @{ type = "edit_v1"; image_path = $market; origin = "app $mid gate-commit";
    params = @{ recipe = @{ ops = @(@{ op = "grayscale" }) }; canvas_mm = @(180, 120) } } |
    ConvertTo-Json -Depth 8
$r = Invoke-RestMethod -Uri "$base/jobs" -Method Post `
    -ContentType "application/json; charset=utf-8" `
    -Body ([System.Text.Encoding]::UTF8.GetBytes($json))
$jid = $r.job_id
for ($i = 0; $i -lt 60; $i++) {
    $s = (Invoke-RestMethod -Uri "$base/jobs/$jid").state
    if ($s -eq "done") { break }
    if ($s -eq "failed") { throw "gate seed job $jid failed" }
    Start-Sleep -Milliseconds 500
}
if ($s -ne "done") { throw "gate seed job $jid never finished" }
Write-Host "seeded gate commit $jid (app $mid gate-commit, full-res)"

if (-not $SeedOnly) {
    Set-Location C:\Repos\Canary   # canary resolves workloads relative to cwd
    $exe = "C:\Repos\Canary\src\Canary.Harness\bin\Debug\net8.0-windows\canary.exe"
    & $exe run --workload rhino --test bristle-17-edit-studio --headless
    $t17 = $LASTEXITCODE
    & $exe run --workload rhino --test bristle-20-grand-tour --headless
    $t20 = $LASTEXITCODE
    Write-Host "gates: bristle-17 exit $t17, bristle-20 exit $t20"
    exit [Math]::Max($t17, $t20)
}
