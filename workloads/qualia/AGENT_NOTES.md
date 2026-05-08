# Qualia — Canary Agent Notes

## Architecture

Qualia is a **browser-based React + Vite app** (not a desktop app — the
April-stub of this file assumed otherwise). The agent at
`src/Canary.Agent.Qualia/` follows the same pattern as
`Canary.Agent.Penumbra`: spawn `npm run dev`, launch Chrome with CDP,
navigate to the dev URL, drive everything through `Runtime.evaluate`
against `window.__canary*` hooks installed by the app.

## Hook surface (Qualia side)

Lives in `Qualia/packages/ui/src/canary-hooks.ts`, mounted at App boot.
Coverage today is focused on the LandingScreen + ModuleRegistry:

- `__canaryHooksReady` — boolean marker set true after install.
- `__canaryWaitForReady(timeoutMs)` — resolves once demo data has loaded.
- `__canaryGetAppInfo()` — `{ ready, theme, moduleCount, profile, landingOpen }`.
- `__canaryHideUI(hidden)` — hides toolbar/sidebar/panels for canvas-only screenshots.
- `__canaryGetModuleConfig()` / `__canaryListModules()` — registry inspection.
- `__canarySetModuleEnabled(id, enabled)` / `__canaryApplyProfile(name)` — mutation.
- `__canaryShowLandingScreen()` / `__canaryCloseLandingScreen()`.
- `__canaryGetLandingState()` — DOM-driven inspection of the modal.
- `__canaryClickProfilePill(name)` / `__canaryToggleLandingModule(id)`.
- `__canaryClickLandingApply()` / `__canaryClickLandingCancel()`.

All hooks return `{ ok, value | reason }` envelopes for failure paths.

## Agent action mapping

| `ICanaryAgent.ExecuteAsync` action | What it does |
|---|---|
| `RunCommand` | Evaluate an arbitrary JS expression — the catch-all for anything not covered by a named action. |
| `WaitForReady` | Poll `__canaryWaitForReady` until app reports ready or timeout. |
| `WaitForStable` | `Task.Delay(ms)`. |
| `SetCanvasSize` | Set `documentElement` size — used to control screenshot dimensions. |
| `HideUI` | Toggle the chrome-hide CSS class via `__canaryHideUI`. |
| `ApplyProfile` | `__canaryApplyProfile(name)`. |
| `SetModuleEnabled` | `__canarySetModuleEnabled(id, enabled)`. |
| `ShowLandingScreen` / `CloseLandingScreen` | Open / close the modal. |
| `ClickProfilePill` | Click a pill by name (minimal/standard/cinematic/workshop). |
| `ToggleLandingModule` | Toggle a module checkbox by id inside the modal. |
| `ClickLandingApply` / `ClickLandingCancel` | Footer buttons. |
| `ClearStorage` | `localStorage.clear() + sessionStorage.clear()`. |

## Configuration

`workload.json` → `qualiaConfig`:

- `projectDir` — Qualia repo root (default `C:\Repos\Qualia`).
- `vitePort` — default 5173.
- `cdpPort` — default 9223 (Penumbra uses 9222; co-existence by design).
- `defaultCanvasWidth/Height` — 1280×720 default (LandingScreen needs more
  vertical space than Penumbra's 960×540 to avoid scroll).
- `readyTimeoutSec` — default 30.
- `clearLocalStorageOnInit` — default true; ensures first-launch behavior
  (LandingScreen visible, default profile) is reproducible.

## Running tests

```bash
cd C:\Repos\Canary

# Pixel-diff (default) — visual regression vs baseline.
canary run --workload qualia --suite landing-screen

# VLM oracle — Gemma 4 vision via Ollama. Requires `ollama pull gemma4:e4b`.
canary run --workload qualia --suite landing-screen --mode vlm

# Both modes per checkpoint.
canary run --workload qualia --suite landing-screen --mode both
```

## Caveats

- Tests boot a fresh Vite + Chrome per suite. ~5–10s startup overhead;
  the agent doesn't currently support `runMode: shared` (single-launch
  for an entire suite — that's a Canary-Core orchestration concern).
- `__canaryHideUI(true)` doesn't kill the LandingScreen; the modal sits
  on top with its own z-index (200). Use `__canaryCloseLandingScreen`
  first if you want a chrome-free viewport screenshot.
- Penumbra-specific actions (`LoadScene`, `SetCamera`, `LoadDisplayPreset`)
  are NOT wired here. If a future Qualia test needs camera control, add
  a `__canarySetCamera` hook on the Qualia side and a corresponding
  agent action.

## Status

Initial implementation — May 8, 2026. LandingScreen + module registry
fixtures are the first batch. fx.* visual tests will follow once D1–D6
have polished implementations beyond the Phase D scaffolds.
