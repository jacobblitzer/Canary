# BRISTLE_WORKLOAD — engine round-trip tests

Canary suite `bristle`: exercises Bristle's **compiled** GH client (`Bristle.GH.gha` —
BR_Connect / BR_Submit / BR_Status / BR_FetchStrokes) on a Slop-built canvas against a
**LIVE local Bristle engine**, gating on panel asserts + file-source checkpoints (the
Pigture pattern — the meaningful artifacts are the StrokeSet JSON and the engine's pen
preview, never the viewport).

Peer contract: [`../../Bristle/spec/PEERS.md`](../../Bristle/spec/PEERS.md).
Authored 2026-08-01 (Bristle P1a); **first execution GREEN same day** — 1/1 PASS on the
first attempt (headless, ~37 s; 6/6 asserts; both checkpoints captured to candidates/).
Bless the captures + flip engine-preview to pixel-diff at the operator's convenience.

## Prerequisites (per machine)

1. **`Bristle.GH.gha` in GH Libraries** — ship via `MultiVerse/ship.ps1 bristle`, then copy
   from `G:\My Drive\builds\Bristle\`. A running Rhino file-locks the .gha — close Rhino
   before updating. (Do NOT copy Newtonsoft.Json.dll beside it; Rhino ships its own.)
2. **The engine running on this machine**: `python -m bristle.service` from
   `C:\Repos\Bristle` (spawns its detached worker; first run prints the bearer token).
   The suite does NOT start the engine — `ConnStatus` will read `OFFLINE:` and the suite
   fails loudly if it is absent. Mock-engine fixtures (engine-generated golden job dirs)
   are the planned alternative for engine-less runs — see the roadmap CC2 seam decision.
3. **Token discovery is automatic on the engine machine**: BR_Connect falls back
   input → `BRISTLE_TOKEN` env → `%LOCALAPPDATA%\Bristle\token.txt` →
   `C:\Repos\Bristle\config\service.local.toml`. No token appears in test JSONs.
4. Slop.gha installed (the loader builds the canvas live from
   `C:/Repos/Bristle/tests/slop/*.json`).
5. `fixtures/bristle_slop_loader.gh` is a byte-copy of the pigture loader (the Slop shell
   is suite-agnostic: JsonPath/Build/Cleanup + SlopSuccess/SlopLog nicknames). Replace
   with a purpose-built loader only if the shell ever diverges.

## The flow (bristle-01)

Build canvas from Slop JSON → `Run=true` fires BR_Submit ONCE (rising edge; uploads
`tests/fixtures/tiny-portrait.png`, ~1 s paint job) → BR_Status background-polls to
`done` (never blocks the canvas) → `Fetch=true` exports `strokes.v0.json` + `preview.png`
to `C:/Repos/Bristle/tests/out/bristle-01/` → asserts on BristleState/SubmitLog/FetchLog/
ConnStatus panels + file-source checkpoints capture both artifacts.

## Known limits (v1, honest)

- Engine lifecycle is out-of-band (prereq 2) — the suite validates the CLIENT, not
  engine boot. Engine internals are pytest-covered in the Bristle repo (23 tests).
- `BristleState == done` needs one poll cycle after completion; the 30 s post-Run wait
  covers a ~1 s job with huge margin. Slow machines: raise waits before blaming the seam.
- Checkpoints run in `capture` mode until first-run baselines exist; bless + flip
  `engine-preview` to pixel-diff once the suite has run green on this machine.

## Suite operations (learned 2026-08-02, I0)

- Tests use the **`WaitForGrasshopperPanel`** agent action (nickname/text/mode equals|contains/
  timeoutMs) after async submits — a plain solution-wait returns on the first quiescence gap,
  long before a watched job is terminal. Passing waits report their real latency (~5 s).
- **Kill Rhino+canary before any run that follows an agent-DLL or .gha change** — keepOpen-held
  Rhinos carry the OLD plugin and the stale-agent race produced two red herrings in one evening.
- **`dotnet build Canary.sln` does NOT build Canary.Agent.Rhino** (solution config excludes it;
  "0 errors" can lie) — build `src/Canary.Agent.Rhino/Canary.Agent.Rhino.csproj` directly, BOTH
  configs (Rhino loads Debug).
- Engine-side depth is enrichment: analyze jobs SKIP depth loudly on hub flakes rather than fail
  (Bristle-side fix, same date). Full suite wall time: ~45-60 s/run.

## bristle-04-style-draft (S1d, added 2026-08-02)
The fiddle-loop proof: Slop def `04_bristle_style_draft.json` builds TWO
BR_Style -> BR_Preview lanes differing only in `bands.coverage_scale` (0.5 vs 1.4).
BR_Preview auto-submits DRAFT jobs on first solve (no Run toggle - that is its design;
debounce 500 ms, supersede = instance GUID). Gate: `PanelsDiffer StrokesA vs StrokesB`
(new two-panel assert, runner-side: `nickname` = panel A, `text` = panel B's nickname,
both must be non-empty and differ) + both Reports contain "draft".
Prereq unchanged: live engine + fresh Bristle.GH.gha in %APPDATA%/Grasshopper/Libraries
(ship.ps1 copies to Drive; the Libraries copy is what Rhino actually loads - update BOTH).
