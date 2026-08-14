# bristle-show: seed + run ONE bristle showcase test UI-VISIBLE (2026-08-14).
#
# For the operator's own terminal - unlike bristle-gate-seed.ps1 (headless CI
# gates), this runs canary WITHOUT --headless so Rhino + the canary console
# are watchable, and a --test run keeps the app open for inspection when done.
#
#   powershell -File C:\Repos\Canary\scripts\bristle-show.ps1
#   powershell -File C:\Repos\Canary\scripts\bristle-show.ps1 -Test bristle-22-commit-purity
#
# Seeds are re-posted every run (idempotent; the engine cache makes repeats
# cheap) so keep_last pruning can never rot the prereqs out from under you.
# ENCODING: pure ASCII per the bristle-22-seed canon.
param([string]$Test = "bristle-20-grand-tour")

$ErrorActionPreference = "Stop"
$base = "http://127.0.0.1:8377"
$market = "C:/Repos/Bristle/tests/fixtures/scenes/market-color.jpg"
$mid = [string][char]0x00B7

try { $null = Invoke-RestMethod -Uri "$base/health" -TimeoutSec 5 }
catch { throw "the Bristle engine is not answering on :8377 - start it first" }

function Submit($origin, $ops, $extra) {
    $params = @{ recipe = @{ ops = $ops }; canvas_mm = @(180, 120) }
    if ($extra) { foreach ($k in $extra.Keys) { $params[$k] = $extra[$k] } }
    $json = @{ type = "edit_v1"; image_path = $market; origin = $origin;
               params = $params } | ConvertTo-Json -Depth 8
    $r = Invoke-RestMethod -Uri "$base/jobs" -Method Post `
        -ContentType "application/json; charset=utf-8" `
        -Body ([System.Text.Encoding]::UTF8.GetBytes($json))
    for ($i = 0; $i -lt 60; $i++) {
        $s = (Invoke-RestMethod -Uri "$base/jobs/$($r.job_id)").state
        if ($s -eq "done") { return $r.job_id }
        if ($s -eq "failed") { throw "seed job $($r.job_id) failed" }
        Start-Sleep -Milliseconds 500
    }
    throw "seed job $($r.job_id) never finished"
}

if ($Test -eq "bristle-22-commit-purity") {
    # oldest -> newest; every trap NEWER than the job Watch must pick
    $null = Submit "app $mid e7a-purity" @(@{ op = "grayscale" }) $null
    $commit = Submit "app $mid final draft" @(@{ op = "grayscale" }) $null
    $null = Submit "app $mid draft" @(@{ op = "invert" }) @{ preview_px = 480 }
    $null = Submit "app $mid sneaky-proxy" @(@{ op = "invert" }) @{ preview_px = 480 }
    Write-Host "seeded the 4-job purity scenario - Watch must pick $commit (final draft)"
} else {
    $commit = Submit "app $mid gate-commit" @(@{ op = "grayscale" }) $null
    Write-Host "seeded app commit $commit for the Watch pickup"
}

Set-Location C:\Repos\Canary   # canary resolves workloads relative to cwd
& "C:\Repos\Canary\src\Canary.Harness\bin\Debug\net8.0-windows\canary.exe" `
    run --workload rhino --test $Test
exit $LASTEXITCODE
