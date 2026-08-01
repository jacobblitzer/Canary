# LIGHTRO_WORKLOAD — light-field component tests

Canary suite `lightro`: exercises Lightro's **compiled** Grasshopper
components (`Lightro.Components.gha`) on Slop-built canvases, comparing the
**computed light-field images** the components save to disk (file-source
checkpoints — the Pigture pattern; the meaningful artifact is never the
viewport).

Peer contract: [`../../Lightro/spec/PEERS.md`](../../Lightro/spec/PEERS.md)
(component GUIDs + bundle prerequisites). Established 2026-07-26.

## Prerequisites (per machine)

1. **`Lightro.Components.gha` in GH Libraries** — ship via
   `MultiVerse/ship.ps1 lightro`, then copy from
   `G:\My Drive\builds\Lightro\` (or `%APPDATA%\Grasshopper\Libraries`
   direct). A running Rhino file-locks the .gha — close Rhino before updating.
2. **A decoded bundle at `C:/Repos/Lightro/decoded/bundles/IMG_0007`** —
   produce with the Lightro Decoder app. Tests read it via mmap (instant).
3. Slop.gha installed (the loader fixture builds test canvases live).
4. **Pigture.gha installed** — `04_combined` places Render Viewer components
   (GUID `E2F3A4B5-…`) to show LF images on the canvas.
5. **Grasshopper Preview Mesh Edges OFF (Ctrl+M)** — only affects the
   `combined-viewport` checkpoint's looks: with edges ON, GH draws a wire per
   image tile and the preview meshes read as red grids instead of photos
   (measured 2026-07-26). It is a per-machine GH preference, not something the
   harness sets; bless that baseline with edges off.

## The fixture

> **Fixture saves are harmless now**: the runner opens a TEMP COPY
> (`%TEMP%\canary-fixtures\`), so Ctrl+S in an exploration session can never
> poison the repo fixture (it happened twice on 2026-07-26/27 when the runner
> still opened the file directly; the ambiguity guard on panel nicknames
> catches any residual duplicate-build state loudly). If the fixture is ever
> corrupted another way, regenerate via `gh/make_loader_fixture.py`.

`fixtures/lightro_slop_loader.gh` — standard Slop loader (JsonPath panel →
Slop Files, Build/Cleanup toggles, SlopLog/SlopSuccess/SlopCount +
CleanupLog/CleanupCount panels, Log Hub + Crash Guard). Generated from
`fixtures/lightro_slop_loader_generator.json` by
`C:/Repos/Lightro/gh/make_loader_fixture.py` (run via
`RhinoCode.exe --rhino <id> script …` in a Rhino with GH open — automated
equivalent of the PIGTURE_WORKLOAD manual bootstrap; it deletes its bootstrap
objects so the fixture holds ONLY the loader).

## Tests

| Test | Slop definition (Lightro repo) | Checkpoints | What it locks down |
|---|---|---|---|
| `lightro-01-refocus` | `tests/slop/01_load_refocus.json` | `refocus-a020`, `refocus-a050` (literal file paths) | bundle load + verify metadata, shift-and-add refocus at two alphas |
| `lightro-02-viewpoints` | `tests/slop/02_viewpoints.json` | `view-corner-neg`, `view-corner-pos` | **the axis-flip tripwire**: corner views (-1,-1) vs (+1,+1) each match their own baseline; a (v,u) flip swaps them |
| `lightro-03-aperture` | `tests/slop/03_authored_aperture.json` | `aperture-refocus` (via `RefocusFilePath` panel) | curve-authored aperture weights → weighted refocus |
| `lightro-05-rays` | `tests/slop/05_rays.json` | `rays-depth`, `rays-view-dependence`, `rays-contact-sheet`, `rays-ray-fan` | the **LF Rays** component: modes driven by writing the `RayMode` panel; each writes `<bundle>/_gh/rays_<mode>.png`. Asserts the published mode list and the measurement text |
| `lightro-06-effects` | `tests/slop/06_effects.json` | `effects-dof`, `effects-anaglyph`, `effects-viewport` | **LF Depth of Field / Stereo / Layers / Depth Relief** together, each on its own placement rectangle (x = 0/12/24/36) so the viewport shows them side by side |
| `lightro-08-sculpt` | `tests/slop/08_sculpt.json` | `sculpt-viewport` | **EXPERIMENTAL**: gradient-domain bas relief (LF Sculpt) beside the pointwise LF Depth Relief on IMG_0011, plus its measured normal map. Asserts `gradient-reconstructed` and the colour contract |
| `lightro-09-autopilot` | `tests/slop/09_autopilot.json` | `auto-dof-A/B`, `auto-ana-A/B`, `auto-viewport` | **LF Autopilot drives DOF + Stereo + Layers LIVE** on IMG_0007 and IMG_0011; asserts prove scene-adaptivity (computed subjects −0.278 vs −2.190, two-layer detection, 4 bands) with checkpoint filenames derived from the prescribed values |
| `lightro-10-fields` | `tests/slop/10_fields.json` | `fields-viewport` | **Fields & line-work**: depth contours, gradient streamlines, occlusion silhouettes and 3-point field sampling on both canonical bundles — all plain GH curves/numbers |
| `lightro-07-everything` | `tests/slop/07_everything.json` | `ev-refocus`, `ev-bokeh`, `ev-view`, `ev-rays-depth`, `ev-dof`, `ev-anaglyph`, `ev-viewport` | **THE OMNIBUS GATE, run LAST**: all ten components in ONE canvas, two Rhino rows (IMG_0007 at y=0, IMG_0011 at y=14), self-contained values so it can be explored by hand. Asserts the relief `colour: sampled` contract and the `block-dominant` depth-display caption |
| `lightro-04-combined` | `tests/slop/04_combined.json` | `combined-refocus-a020`, `combined-view-neg`, `combined-aperture-a035`, `combined-canvas`, `combined-viewport` | ALL THREE scenarios in one Slop-built canvas: per-section placement rects (side-by-side viewport meshes), on-canvas Pigture Render Viewers, and a Slop **Canvas Image** render of the whole canvas as a layout-regression net |

All `runMode: shared` — run with
`canary run --workload rhino --suite lightro`, never per-test `--test`.

## Conventions this suite depends on (break at your peril)

- **Compiled LF components have NO console `out` output** — outputs are
  0-based from the first declared output. Script-component (pasted) wiring is
  +1 relative to this. The Slop definitions in Lightro are written for the
  COMPILED components.
- Component GUIDs are pinned (`c0de1f01..04-…`, canonical list:
  `C:/Repos/Lightro/gh/component_guids.json`). Slop resolves by GUID;
  changing them breaks every definition + this suite.
- Literal-path checkpoints (01, 02) read PNGs the components write under
  `<bundle>/_gh/` with value-stamped names (`refocus_a+0.200.png`). **Delete
  `<bundle>/_gh/` before a strict run** — a stale file could satisfy a
  checkpoint if the current run silently failed to write (no delete-file
  action exists in the harness vocabulary yet).
- Checkpoints start `mode: "capture"` (never fail). After inspecting
  candidates under `results/lightro-*/candidates/`, bless with
  `canary approve` and flip modes to `pixel-diff` to arm the regression net.

## Run record

- 2026-07-26 first execution: **3 passed / 0 failed / 0 crashed** (headless).
- 2026-07-26 after Phase 3.5: **4/4** with the combined canvas + layout net.
- 2026-07-26 after Phase 5 (plugin grown 4 → 10 components, bundle format 2):
  **6 passed / 0 failed / 0 crashed**, ~4 min including Rhino launch. Note the
  4 original tests passed unchanged across three plugin rebuilds — new outputs
  were APPENDED to each component, so no Slop wire index shifted.

The CANONICAL BUNDLE PAIR (operator-standardized 2026-07-28) is
IMG_0007 + IMG_0011 — every lightro test uses one or both. Re-decoding
or refreshing either bundle changes value-derived checkpoint filenames in
06/07/09 (Autopilot computes live in GH; the test JSON pins the expected
filenames/values as static snapshots of the same math).
