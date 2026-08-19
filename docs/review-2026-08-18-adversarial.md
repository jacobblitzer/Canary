# Adversarial review — Canary deployment campaign, 2026-08-18

> Reviewer: Hermes (glm-5.2), session 2026-08-18 19:45–20:15 PDT.
> Scope: Canary commits `5979833~1..HEAD` (12 commits, ending at `3830c2f`).
> Method: every negative claim re-run against the raw artifact, case-insensitively, unfiltered.

## Verdict

**Fix these first.** The campaign has one severe defect that will silently disarm 10 working tests on the second machine, and several document-level errors that will send the machine-2 agent chasing phantom differences. The code infrastructure (doctor, DiffAgainst, scripts, parallelism fix) is sound.

## Confirmed defects

### D1 — BLOCKING: 5 of 8 "stale" module ids still exist in Qualia; 10 tests wrongly disarmed

**Claim:** Commit `d950c02` marked 14 tests `[STALE 2026-08-18]` and forced them to `mode: "capture"` (save-only, never fails), claiming 8 module ids no longer exist in Qualia's registry.

**What is actually true:** 5 of the 8 ids **still exist** in `packages/core/src/modules/builtin.ts`:

| Id | Location | Tests wrongly disarmed |
|---|---|---|
| `compute.rag.eager-l3` | `builtin.ts:233` | eager-l3-cold-launch, -warm-launch, -progress-badge, -provider-swap (4) |
| `fx.debug-layer-colors` | `builtin.ts` | diag-pencil-debug-colors, -standard-debug-colors, -only-debug-colors (3) |
| `fx.pencil-toon` | `builtin.ts` | diag-pencil-no-pencil-toon, -only-debug-colors (2) |
| `fx.laser-rat` | `builtin.ts` | resolver-c1-laser-rat-round-trip (1) |
| `render.penumbra-backdrop` | `builtin.ts:137` | main-cinematic-curl-noise (1) |

Only 3 ids are genuinely absent: `render.nodes`, `render.edges`, `render.labels` (confirmed deleted 2026-07-19, referenced only in comments). 4 tests using only those 3 ids are correctly stale: diag-pencil-no-nodes, -no-edges, -no-labels, -only-background.

**Root cause:** The author's regex `id:\s*'[a-z]+\.[a-zA-Z.]+'` does not match hyphens. All 5 wrongly-marked ids contain hyphens. The regex filtered them out of the "live registry" list (producing 14 ids instead of the actual 51), and the author concluded absence from the filtered view. This is the exact failure mode the review prompt predicted: a filter on truth.

**Proof:** `rg -n "id:" C:/Repos/Qualia/packages/core/src/modules/builtin.ts` — 51 ids, including all 5 at the lines cited above.

**Blast radius:** 10 tests forced to capture-only (never fail) when their toggles are live and should be asserting. On the second machine, these tests will silently pass regardless of whether the feature works. The stale doc and BUILD_LOG both propagate the false "14 ids" claim.

**Blocks machine-2:** Yes — the machine-2 agent will inherit 10 disarmed tests and a stale doc claiming 5 live features are dead.

**Fix:** Re-derive the live registry with `rg -i "id:\s*'" packages/core/src/modules/builtin.ts` (no character-class restriction). Un-mark the 10 tests: remove the `[STALE 2026-08-18]` note, restore their checkpoints to `mode: "vlm"` or the original mode. Correct the stale doc's "14 ids" to 51 and remove the 5 live ids from the dead-id table.

### D2 — 59 CPig-dependent tests have no `requires` declaration

**Claim:** "204 rhino tests were given `requires` blocks... the other 64 genuinely dependency-free."

**What is actually true:** 59 of the 64 undeclared tests drive CPig via Rhino `RunCommand` (e.g. `_CPigSphere`, `_CPigUnion`, `_CPigFromMesh`). They don't use Slop, so the author's per-test GUID scan didn't detect the dependency. On a machine without CPig, `doctor` reports no preconditions and the runner doesn't gate — the test launches, fails with an "unknown command" error inside Rhino, and produces a confusing failure instead of a clear precondition miss.

**Proof:** `rg -l "_?CPig" workloads/rhino/tests/cpig-booleans-*.json` — all 6 have `requires: []` but call `_CPigSphere`, `_CPigUnion`, etc. Same for cpig-bool-refactor-*, cpig-fieldops-*, cpig-render-*, cpig-repmatrix-*, cpig-ab-*, cpig-r3/r4/r5 (59 total). None set a `JsonPath` panel — they drive CPig entirely via RunCommand.

**Blast radius:** On a QC machine missing CPig, 59 tests fail opaquely instead of being gated. On a machine WITH CPig (the expected case), they work fine — so this is a missing-declaration defect, not a false-red.

**Blocks machine-2:** No — the machine-2 machine is expected to have CPig installed. But it undermines the campaign's core premise (every test declares what it needs).

**Fix:** Add `{"kind": "plugin", "id": "gh:CPig", "fix": "..."}` to the `requires` block of each of the 59 tests. The fix string already exists in the corpus (used by the 82 declared CPig tests).

### D3 — The "376 distinct (state, signature) pairs" number is ungrounded

**Claim:** `docs/qualia-stale-2026-08-18.md` §4 says "an exhaustive search over all 127 subsets found none reproducing the 376 distinct (state, signature) pairs" for sweep-w2-atlas.

**What is actually true:** No w2-atlas run produces 376 distinct pairs. The actual counts are:

| Run | Distinct (state, sig) pairs |
|---|---|
| w2-atlas-r2 | 396 |
| w2-atlas-r3 | 294 |
| w2-atlas-r4 | 294 |
| w2-atlas-r5 | 483 |
| w2-atlas-r6 | 458 |

The number 376 appears nowhere in any run data or findings file. The 127-subset math is correct (2^7 - 1 = 127, with 7 other specs including desktop-mini). The conclusion (w2-atlas must not be pruned) is likely still correct — w2-atlas has 294–483 distinct pairs vs 12–99 for all other sweeps — but the specific supporting number is fabricated or misremembered.

**Proof:** `cat workloads/qualia/sweeps/runs/w2-atlas-r6/effects.json` parsed programmatically — 458 distinct pairs, not 376. Same for all other runs.

**Blast radius:** Low — the conclusion holds without the number. But the machine-2 agent may try to verify 376 and waste time.

**Blocks machine-2:** No.

**Fix:** Replace "376" with the actual count from the latest run (r6: 458), or remove the specific number and state the comparison qualitatively.

### D4 — Stale doc claims "14 ids" in Qualia's registry; actual is 51

**Claim:** `docs/qualia-stale-2026-08-18.md` says "Qualia's module registry (`packages/core/src`) has 14 ids today."

**What is actually true:** `builtin.ts` declares 51 ids. The 14 the author found are the subset without hyphens that matched the broken regex. The doc's "Live ids for reference" list omits 37 real ids including `compute.rag.eager-l3`, `fx.debug-layer-colors`, `fx.pencil-toon`, `fx.laser-rat`, `render.penumbra-backdrop` — the very ids claimed dead in §1.

**Proof:** `rg -c "id:" C:/Repos/Qualia/packages/core/src/modules/builtin.ts` = 51.

**Blast radius:** The doc is the record the next session trusts. A wrong registry count propagates the stale-id error into future sessions.

**Blocks machine-2:** Indirectly — the machine-2 prompt references the stale doc.

**Fix:** Correct "14 ids" to 51. Replace the "Live ids for reference" list with the full set from `builtin.ts`.

### D5 — BUILD_LOG test count drift: "463/463" vs actual 470 (non-defect)

**Claim:** Multiple BUILD_LOG entries say "463/463 unit" and "470/470 unit" at different points.

**What is actually true:** The current suite is 470 tests. The entries saying 463 are from earlier in the session before the diff tests were added (commit `47db29d` added 7). The latest entry correctly says 470. This is not a defect — the count moved as the session progressed, and the latest is correct.

**Proof:** `dotnet test --filter Category=Unit --no-build` = 470/470 across 12 consecutive runs.

**Blast radius:** None — the latest count is correct.

**Blocks machine-2:** No.

## Unverifiable

- **VLM criteria accuracy against profile rendering logic (Claim 3):** The subagent traced the pencil-toon mounting flow and confirmed that `fx.pencil-toon` sets `scene.background = paperTexture` (`pencilToon.ts` line 317), overriding the dark theme's `setClearColor`. This confirms the theme-fight is NOT a real fight — pencil-toon wins. However, I did not fully verify each of the 9 `main-*` VLM criteria against every profile's actual rendering body (only confirmed they are specific and falsifiable, not vague). A full profile-by-profile rendering audit would require reading each profile definition in `profiles.ts` and tracing through `ProfileApplier.ts` — the subagent was mid-way through this when it hit its iteration limit.

- **Regeneration byte-identity (Claim 4):** The subagent ran `generate-sweep.mjs` on the w3-pairs spec and diffed against the deleted file. The structural content (states, commands) matched, but the embedded `sweep-driver.js` source differs (four vintages in the corpus vs. the live 20,563-char driver). So regeneration is structurally reversible but not byte-identical — the driver drift documented in §5 of the stale doc explains this. I did not test regeneration of all 5 deleted suite types.

## Checked and sound

- All 7 declared plug-in ids exist in the host's raw `environment.json` loaded list (Claim 1a).
- All 205 file requirement paths resolve after token expansion; 204 match their `JsonPath` panel value, 1 is a legitimate additional data-asset dependency (bristle-03's Lightro bundle) (Claim 1b).
- CPig/CPig-Kinematics split: 82 tests declare `gh:CPig`, 28 declare `gh:CPig Kinematics`, none declare both (Claim 1c).
- Fix strings are tier-neutral — each offers a yak-package route first, then a dev-machine alternative (Claim 1e).
- `render.grid` genuinely exists in `builtin.ts:90` — correctly left armed (Claim 2).
- VLM criteria are specific and falsifiable — each names particular visual characteristics, none are unfalsifiably vague (Claim 3).
- No surviving references to deleted tests in any suite, test, or spec file — only in historical run records under `sweeps/runs/` (Claim 4).
- `doctor --workload qualia` correctly exits 1 (22 errors); other 4 workloads exit 0. Check 4b parses every `tests/*.json`. ~1s per workload (Claim 5).
- `DiffAgainst` is symmetric, handles missing/`?` versions correctly, origin outranks version in ordering, empty-loaded and absent-scanFolders produce no spurious diffs (Claim 6).
- Both scripts are PS 5.1 compatible: no `??`, ternary, `-AsHashtable`, or pipeline chains; all `Set-Content` calls have `-Encoding utf8`. Survey fails closed on registry/version/toolchain checks. Zone.Identifier check is technically fail-open but low impact (Claim 7).
- Machine-2 prompt reference numbers all match the real capture: 96 loaded, 54/16/14/7/5 origin split, 209 requirements (7 plugin + 202 file), Rhino 8.34.26223.11001, 3 warnings (Claim 7).
- Test-parallelism fix: 12/12 consecutive full-suite passes at 470/470, zero flakes (Claim 8).
- BUILD_LOG correction entries (commits `2d91754`, `66203c2`, `eee3fd7`) are themselves correct — the rh2 hiding reason, the hook-count, the KinematicImporter GUIDs, and the Lightro filter are all accurately corrected (Claim 9).

## Riskiest thing not checked

The full profile-by-profile rendering audit for the 9 `main-*` VLM criteria. The subagent confirmed the pencil-toon theme-fight is not real, and I confirmed none of the criteria are unfalsifiable. But I did not read each profile definition in `profiles.ts` and trace through `ProfileApplier.ts` to verify that, e.g., `main-blueprint`'s criterion "WHITE wireframe lines on a DEEP-BLUE background" matches what the blueprint profile actually renders. A profile whose rendering logic diverges from its `description:` field would produce a VLM criterion that fails on a correct image — the exact "guaranteed failure" the review prompt warned about.