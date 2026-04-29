---
title: "VLM Oracle"
date: 2026-04-28
tags:
  - feature
status: implemented
project: canary
phase: "8"
---

# VLM Oracle

## Description

A correctness-verification mode that complements visual regression. A Vision-Language Model evaluates a captured screenshot against a natural-language description of what it should depict, returning pass/fail + reasoning + confidence. No baseline image is needed.

VLM tests answer **"does this look like what it's supposed to look like?"** Pixel-diff tests answer **"is this the same as last time?"** The two modes have different jobs:

| Mode | Question | Cost | Use when |
|---|---|---|---|
| Visual regression (pixel-diff) | Same as last time? | ~1-2 s | Code-stability tripwire after refactors / dependency bumps. |
| VLM | Looks correct? | ~5-15 s (Ollama) / ~1-2 s (Claude) | Validating new components, post-bugfix verification, "does this make sense?" smoke. |

Pick the right mode for the job — see [`MultiVerse/CLAUDE.md` § Testing modes](../../../MultiVerse/CLAUDE.md#testing-modes--vlm-vs-visual-regression) for the canonical workflow guidance.

VLM mode unlocks scenarios that are impractical for pixel-diff:
- Verifying *what* a screenshot shows rather than matching a pixel layout.
- Testing that error dialogs are absent.
- Checking that geometry is visible and correctly oriented.
- Validating color / lighting expectations without pixel-perfect baselines.

## Mode selection at runtime — `--mode` flag

Every test definition is mode-agnostic. The flag picks how to evaluate at run time:

```bash
canary run --workload <w> --suite <s> --mode pixel-diff   # default — visual regression
canary run --workload <w> --suite <s> --mode vlm          # correctness via VLM
canary run --workload <w> --suite <s> --mode both         # both verdicts per checkpoint
```

Per-checkpoint `mode: "vlm"` in the test JSON still wins over the flag — this is how a test commits a particular checkpoint to VLM-only evaluation. Otherwise the flag applies, defaulting to pixel-diff for backwards compatibility.

When `--mode both` is selected, each checkpoint runs twice: once with pixel-diff (against the baseline), once with VLM (against the description). Both verdicts appear in the report; the test passes only if both pass.

## `setup.vlmDescription` — the test-level default prompt

A test can carry `setup.vlmDescription` (string, optional) — a default natural-language description of what the viewport should show when the test's components work correctly. The VLM evaluator uses this as the prompt when:
- A checkpoint has no `description` of its own and is being evaluated in VLM mode.
- `--mode vlm` (or `--mode both`) promotes the test's checkpoints to VLM evaluation.

Per-checkpoint `description` still wins when set. This lets you write one test JSON that supports both modes without per-checkpoint duplication.

```json
{
  "name": "cpig-39-quad-extract-instant",
  "setup": {
    "file": "fixtures/cpig_slop_loader.gh",
    "vlm": { "provider": "ollama", "model": "gemma4:e4b" },
    "vlmDescription": "A quad-dominant remesh of a sphere. Most faces should be quadrilaterals (visible as four-sided cells when wireframed). Some triangles may appear near the poles. The shape is recognizably spherical and is NOT the original triangulated mesh."
  },
  "checkpoints": [
    { "name": "post-build", "atTimeMs": 5000, "tolerance": 0.02 }
  ]
}
```

Run as regression: `canary run --suite cpig-retopo --mode pixel-diff` (uses baseline).
Run as correctness check: `canary run --suite cpig-retopo --mode vlm` (uses the description).

## Writing good VLM descriptions

- **Anchor on robustly distinguishable features** that any vision model can identify: geometry shape, dominant color, presence/absence of overlays. Avoid asking about subtle differences a small local model will miss ("the arcs are slightly smoother").
- **One clear claim per sentence**. Vision models follow specific assertions better than vague paragraphs.
- **State invariants, not aesthetics**. "Quad-dominant mesh of a sphere" not "the result looks good."
- **Anchor on the contrast**. If the test's purpose is "show that arcs were drawn," say *"thin colored polylines must be visible on the sphere surface"* up front. Don't bury the load-bearing visual feature.

## Acceptance Criteria

- [x] `TestCheckpoint` has a `mode` field with values `"pixel-diff"` (default) and `"vlm"`
- [x] `VlmConfig` type specifies provider, model, and max tokens
- [x] `ClaudeVlmProvider` sends screenshots to Anthropic Messages API and parses structured JSON verdicts
- [x] `OllamaVlmProvider` sends screenshots to local Ollama instance (no API key needed)
- [x] `VlmEvaluator` factory resolves providers ("claude", "ollama") and API keys from environment variables
- [x] `TestRunner` branches on checkpoint mode, skipping baseline lookup for VLM checkpoints
- [x] `CheckpointResult` carries `VlmReasoning`, `VlmConfidence`, `VlmDescription`
- [x] HTML report renders VLM-specific detail sections
- [x] Mixed-mode tests (pixel-diff + VLM checkpoints) are supported
- [x] Existing pixel-diff tests pass unchanged
- [x] 26 unit tests cover serialization, parsing, and configuration (19 Claude + 7 Ollama)

## Implementation Notes

### Key files

| File | Role |
|------|------|
| `src/Canary.Core/Config/VlmConfig.cs` | Configuration DTO |
| `src/Canary.Core/Config/TestDefinition.cs` | `Mode` on `TestCheckpoint`, `Vlm` on `TestSetup` |
| `src/Canary.Core/Comparison/IVlmProvider.cs` | Provider interface + `VlmVerdict` |
| `src/Canary.Core/Comparison/ClaudeVlmProvider.cs` | Anthropic API implementation |
| `src/Canary.Core/Comparison/OllamaVlmProvider.cs` | Local Ollama implementation |
| `src/Canary.Core/Comparison/VlmEvaluator.cs` | Factory + provider resolution |
| `src/Canary.Core/Orchestration/TestResult.cs` | VLM fields on `CheckpointResult` |
| `src/Canary.Core/Orchestration/TestRunner.cs` | Mode branching, `ProcessVlmCheckpointAsync` |
| `src/Canary.Core/Reporting/HtmlReportGenerator.cs` | VLM detail rendering |
| `tests/Canary.Tests/Comparison/VlmTests.cs` | 26 unit tests |

### Provider configuration

**Claude (cloud)**

Set one of these environment variables:
1. `CANARY_VLM_API_KEY` (preferred)
2. `ANTHROPIC_API_KEY` (fallback)

```json
"vlm": { "provider": "claude", "model": "claude-sonnet-4-20250514" }
```

**Ollama (local)**

Install [Ollama](https://ollama.com) and pull a vision-capable model. Gemma 4 ships in two sizes — pick by available bandwidth + disk:

```
ollama pull gemma4:e4b   # ~9.6 GB, higher accuracy (recommended default)
ollama pull gemma4:e2b   # ~3 GB, smaller variant — useful on slower connections
```

No API key needed. Optionally set `CANARY_OLLAMA_URL` (defaults to `http://localhost:11434`).

```json
"vlm": { "provider": "ollama", "model": "gemma4:e4b" }
```

### Test definition example (Ollama)

```json
{
  "name": "vlm-red-cube-visible",
  "workload": "penumbra",
  "setup": {
    "scene": { "sceneName": "basic-shapes" },
    "backend": "webgpu",
    "canvas": { "width": 960, "height": 540 },
    "vlm": {
      "provider": "ollama",
      "model": "gemma4:e4b"
    }
  },
  "checkpoints": [
    {
      "name": "red-cube-centered",
      "mode": "vlm",
      "description": "A red cube should be centered in the viewport against a dark background."
    }
  ]
}
```

## Penumbra VLM Test Suite

7 test definitions covering 6 scenes, exercising semantic verification of geometry, color, and material:

| Test | Scene | Checkpoints | What it verifies |
|------|-------|-------------|------------------|
| `vlm-tape-csg-geometry` | Box - Sphere | 2 (front, three-quarter) | Box with spherical cavity, orange material, sharp edges |
| `vlm-atlas-blob-organic` | 12-Sphere Blob | 2 (front, side) | Organic blob shape, smooth normals, blue material, no brick artifacts |
| `vlm-cornell-box-layout` | Cornell Box | 1 (front) | Red/green walls, white floor/ceiling, two interior boxes |
| `vlm-teapot-shape` | SDF Teapot | 2 (front, three-quarter) | Teapot silhouette: body, spout, handle, lid all present |
| `vlm-multi-field-separation` | Multi-Field | 1 (front) | Three separated primitives: red sphere, green box, blue torus |
| `vlm-terrain-landscape` | Procedural Terrain | 1 (front) | Green undulating terrain with blue water plane |
| `vlm-teapot-metal-mixed` | SDF Teapot + Metal | 2 (pixel-diff + VLM) | Mixed mode: baseline regression + metallic sheen verification |

Run the suite:
```
canary run --workload penumbra --suite vlm
```

The default test definitions use `ollama` with `gemma4:e4b`. Requires Ollama running locally with the model pulled. Alternatively, set `"provider": "claude"` in the test definitions and provide `CANARY_VLM_API_KEY` or `ANTHROPIC_API_KEY`.

## Related

- Spec: [PHASES.md Phase 8](../../spec/PHASES.md)
- Visual expectations: [workloads/penumbra/VISUAL_EXPECTATIONS.md](../../workloads/penumbra/VISUAL_EXPECTATIONS.md)
- Phase: 8
