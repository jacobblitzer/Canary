---
date: 2026-07-03
tags: [feature, session, flight-recorder, telemetry, mcp, rhino, penumbra, cpig]
status: in-progress
project: canary
component: "session + mcp-server"
---

# Session flight recorder — one folder that debugs itself

The end-state (campaign: `MultiVerse/prompts/canary-session-flight-recorder-2026-07-02.md`):
the operator types `canary session start --workload rhino --file <doc>.3dm`, plays normally,
closes Rhino — and Claude, cold, reconstructs the whole session from MCP tools alone (§5
black-box acceptance). Phases A (Canary), B (Penumbra), C (CPig) and D (this doc + MCP) are
SHIPPED as of 2026-07-03; the §5 acceptance run with the operator is the open gate.

## The session folder (`workloads/<w>/sessions/<yyyyMMdd-HHmmss-xxxx>/`)

| Artifact | What it holds |
|---|---|
| `manifest.json` | Session identity: opened file + SHA256, machine, Canary version, app path + PID, applied env (incl. `PENUMBRA_SESSION_REF`), start/end, capture count, exit record (`diedUnexpectedly` on kill/native crash), `harvested` Penumbra identity (plugin/bundle SHAs, skew verdict, renderer/GPU) |
| `telemetry.ndjson` | The merged stream: Canary session records (captures etc.) + the TAILED Penumbra/CPig NDJSON (re-wrapped `Kind=Log`, `Source="penumbra"`, domain kind at `Data.event`, original payload at `Data.payload`, session ref at `ref`) |
| `telemetry-prior.ndjson` | The PREVIOUS session's Penumbra log, rescued before the new spawn archives it |
| `captures/` | PNGs + notes; each capture also records before/after frame state, active view, and (R1.6) whether the scene/registry snapshots fired |
| `SESSION_REPORT.md` / `session.json` | Human-readable close-out + machine summary |

## Reading a session from Claude Code (MCP)

Registered in `Canary.McpServer` (12 tools; see `docs/mcp-server.md` for `.mcp.json` setup):

- `list_sessions` / `get_session_report` — index + human report.
- `get_session_manifest(sessionId)` — the manifest verbatim (identity + exit record + SHAs).
- `get_session_telemetry(sessionId, eventPrefix?, tail?, prior?)` — raw NDJSON lines,
  prefix-filtered on `Data.event` (fallback: record `Kind`), last-N tail (default 200,
  max 2000). Examples: `eventPrefix="cpig.push"` → every push with seq + content hash;
  `"gl.scene.snapshot"` → render-side field inventory + per-view cameras at capture points;
  `"gl.fsm.plan-hash"` → FSM decision change-points; `"rhino.command"` → the operator's
  command timeline; `"penumbra.startup"` → banner + diagnostics.

## Snapshot-on-capture (R1.6)

Every session capture (REPL `c` / UI hotkey) now fires, best-effort, right before the pixels:
- **Penumbra** `Bridge.DumpSceneState()` → `gl.scene.snapshot` (atoms/reps/selection/cascade
  depth/per-view cameras + framing verdicts) — via the new `DumpPenumbraSceneState` agent action;
- **CPig** `-_CPigDumpState` → `cpig.session.snapshot` (per field: id, recipe op tree, rep,
  translation, box, live-handle flag).
The capture's telemetry record carries `sceneSnapshot`/`cpigSnapshot` flags saying what fired.

## Push identity (R1.6)

`cpig.push.start`/`cpig.push.done` carry a monotonic per-session `seq` and (on done) a
`contentSha256` (of the .3mf bytes, or of the voxel payload on the direct-grid path) — the
push's identity, since `display.3mf` is a single-slot overwrite. Correlate with
`gl.compile3mf.*` (which logs its own seq + drop reasons) to account for EVERY push:
rendered, or dropped-with-reason.

## Event catalog

<!-- BEGIN GENERATED EVENT CATALOG (scripts/gen_event_catalog.py) -->

_186 distinct NDJSON kinds, grep-generated from the sibling Penumbra + CPig checkouts. Regenerate: `python scripts/gen_event_catalog.py`. Filter any of these through `get_session_telemetry(eventPrefix=…)` — tailed records carry the kind at `Data.event`._

### `cpig.*` (60)

- `cpig.bake-resolution` (CPig)
- `cpig.build` (CPig)
- `cpig.copy` (CPig)
- `cpig.create.box.cancelled` (CPig)
- `cpig.create.box.done` (CPig)
- `cpig.create.box.failed` (CPig)
- `cpig.create.box.invoked` (CPig)
- `cpig.create.duplicate.invoked` (CPig)
- `cpig.create.frommesh.cancelled` (CPig)
- `cpig.create.frommesh.done` (CPig)
- `cpig.create.frommesh.failed` (CPig)
- `cpig.create.frommesh.invoked` (CPig)
- `cpig.create.gyroid.cancelled` (CPig)
- `cpig.create.gyroid.done` (CPig)
- `cpig.create.gyroid.failed` (CPig)
- `cpig.create.gyroid.invoked` (CPig)
- `cpig.create.offset.cancelled` (CPig)
- `cpig.create.offset.invoked` (CPig)
- `cpig.create.smooth.cancelled` (CPig)
- `cpig.create.smooth.invoked` (CPig)
- `cpig.create.sphere.cancelled` (CPig)
- `cpig.create.sphere.done` (CPig)
- `cpig.create.sphere.failed` (CPig)
- `cpig.create.sphere.invoked` (CPig)
- `cpig.create.tpms.cancelled` (CPig)
- `cpig.create.tpms.done` (CPig)
- `cpig.create.tpms.failed` (CPig)
- `cpig.create.tpms.invoked` (CPig)
- `cpig.delete` (CPig)
- `cpig.display.cleared` (CPig)
- `cpig.display.invoked` (CPig)
- `cpig.field.added` (CPig)
- `cpig.field.bake-stats` (CPig)
- `cpig.field.disposed` (CPig)
- `cpig.field.redisplay` (CPig)
- `cpig.field.removed` (CPig)
- `cpig.field.setworld` (CPig)
- `cpig.move.applied` (CPig)
- `cpig.move.bail` (CPig)
- `cpig.move.exception` (CPig)
- `cpig.push.done` (CPig)
- `cpig.push.start` (CPig)
- `cpig.reconcile` (CPig)
- `cpig.rep.live` (CPig)
- `cpig.restore.applied` (CPig)
- `cpig.restore.completed` (CPig)
- `cpig.restore.dropped` (CPig)
- `cpig.restore.promoted` (CPig)
- `cpig.restore.started` (CPig)
- `cpig.rotate.applied` (CPig)
- `cpig.scale.dropped` (CPig)
- `cpig.select.synced` (CPig)
- `cpig.session.snapshot` (CPig)
- `cpig.setworld.enter` (CPig)
- `cpig.setworld.missed` (CPig)
- `cpig.undo.build-failed` (CPig)
- `cpig.undo.exception` (CPig)
- `cpig.undo.no-op` (CPig)
- `cpig.undo.no-recipe` (CPig)
- `cpig.undo.rebuilt` (CPig)

### `fit.*` (2)

- `fit` (Penumbra)
- `fit.auto` (Penumbra)

### `frame.*` (2)

- `frame.real` (Penumbra)
- `frame.tick` (Penumbra)

### `gl.*` (99)

- `gl.build-once.done` (Penumbra)
- `gl.build-once.start` (Penumbra)
- `gl.build.cache-hit` (Penumbra)
- `gl.build.cache-miss` (Penumbra)
- `gl.build.compile-link.composite` (Penumbra)
- `gl.build.compile-link.pass1` (Penumbra)
- `gl.build.delete-programs` (Penumbra)
- `gl.build.probe` (Penumbra)
- `gl.build.summary` (Penumbra)
- `gl.build.uniforms.composite` (Penumbra)
- `gl.build.uniforms.grid` (Penumbra)
- `gl.build.uniforms.scene` (Penumbra)
- `gl.build.uniforms.tape` (Penumbra)
- `gl.build.upload-atlas` (Penumbra)
- `gl.build.upload-cascade` (Penumbra)
- `gl.build.upload-dense-atlas` (Penumbra)
- `gl.build.upload-grid` (Penumbra)
- `gl.build.upload-optree-tape` (Penumbra)
- `gl.build.upload-scene-grid` (Penumbra)
- `gl.build.upload-scene-tape` (Penumbra)
- `gl.build.upload-tape` (Penumbra)
- `gl.build.upload-tris` (Penumbra)
- `gl.build.vao-idle` (Penumbra)
- `gl.cache.disk-clear` (Penumbra)
- `gl.cache.disk-hit` (Penumbra)
- `gl.cache.disk-miss` (Penumbra)
- `gl.cache.disk-probe` (Penumbra)
- `gl.cache.disk-save` (Penumbra)
- `gl.cascade.atom-degraded` (Penumbra)
- `gl.cascade.bake-cancel` (Penumbra)
- `gl.cascade.bake-done` (Penumbra)
- `gl.cascade.bake-error` (Penumbra)
- `gl.cascade.bake-start` (Penumbra)
- `gl.cascade.budget-cap` (Penumbra)
- `gl.cascade.clamp` (Penumbra)
- `gl.cascade.plan-error` (Penumbra)
- `gl.cascade.plan-seen` (Penumbra)
- `gl.cascade.push-to-display` (Penumbra)
- `gl.cascade.skip-over-cap` (Penumbra)
- `gl.clear` (Penumbra)
- `gl.compile` (Penumbra)
- `gl.compile-bench.cancelled` (Penumbra)
- `gl.compile-bench.error` (Penumbra)
- `gl.compile-bench.sample` (Penumbra)
- `gl.compile-bench.summary` (Penumbra)
- `gl.compile3mf` (Penumbra)
- `gl.compile3mf.dropped` (Penumbra)
- `gl.compile3mf.start` (Penumbra)
- `gl.compileHost.spawn-detail` (Penumbra)
- `gl.compileHost.start` (Penumbra)
- `gl.debugReps` (Penumbra)
- `gl.field.transform` (Penumbra)
- `gl.fieldRep` (Penumbra)
- `gl.frame.entry` (Penumbra)
- `gl.frame.skip-ramp` (Penumbra)
- `gl.frame.storm` (Penumbra)
- `gl.fsm.plan-hash` (Penumbra)
- `gl.grid.upload` (Penumbra)
- `gl.load.done` (Penumbra)
- `gl.load.fit` (Penumbra)
- `gl.load.grid-mc` (Penumbra)
- `gl.load.grid-mc.error` (Penumbra)
- `gl.load.parse.atlas` (Penumbra)
- `gl.load.parse.atoms` (Penumbra)
- `gl.load.parse.cascades` (Penumbra)
- `gl.load.parse.dense-atlas` (Penumbra)
- `gl.load.parse.geom` (Penumbra)
- `gl.load.parse.optree-tape` (Penumbra)
- `gl.load.post.deselect` (Penumbra)
- `gl.load.post.doc-modified` (Penumbra)
- `gl.load.post.enable-conduit` (Penumbra)
- `gl.load.post.hide-oop` (Penumbra)
- `gl.load.post.id-mismatch` (Penumbra)
- `gl.load.post.panel-refresh` (Penumbra)
- `gl.load.post.redraw` (Penumbra)
- `gl.load.post.rep-call` (Penumbra)
- `gl.load.post.rep-loop` (Penumbra)
- `gl.load.post.selection-watcher` (Penumbra)
- `gl.load.post.set-fieldids` (Penumbra)
- `gl.load.post.set-labels` (Penumbra)
- `gl.load.post.write-status` (Penumbra)
- `gl.load.post.write-summary` (Penumbra)
- `gl.load.proxies` (Penumbra)
- `gl.load.proxies.atom` (Penumbra)
- `gl.load.proxies.clear` (Penumbra)
- `gl.load.proxies.sync` (Penumbra)
- `gl.load.scene-parse` (Penumbra)
- `gl.load.start` (Penumbra)
- `gl.meshDisplay` (Penumbra)
- `gl.previewBackend` (Penumbra)
- `gl.rep` (Penumbra)
- `gl.restore` (Penumbra)
- `gl.scene-advance.error` (Penumbra)
- `gl.scene-advance.perf` (Penumbra)
- `gl.scene.loaded` (Penumbra)
- `gl.scene.snapshot` (Penumbra)
- `gl.scene.snapshot.error` (Penumbra)
- `gl.select` (Penumbra)
- `gl.selection.overflow` (Penumbra)

### `hide.*` (5)

- `hide.done` (Penumbra)
- `hide.enabled-false` (Penumbra)
- `hide.start` (Penumbra)
- `hide.views-redraw` (Penumbra)
- `hide.views-redraw-skipped` (Penumbra)

### `host.*` (7)

- `host.connected` (Penumbra)
- `host.disabled` (Penumbra)
- `host.failed` (Penumbra)
- `host.retry` (Penumbra)
- `host.rpc.desync-drop` (Penumbra)
- `host.start` (Penumbra)
- `host.warn.stray` (Penumbra)

### `penumbra.*` (4)

- `penumbra.crash` (Penumbra)
- `penumbra.skew-block` (Penumbra)
- `penumbra.startup-banner` (Penumbra)
- `penumbra.startup-diagnostics` (Penumbra)

### `render.*` (1)

- `render.error` (Penumbra)

### `rep.*` (2)

- `rep.*` (Penumbra)
- `rep.live` (Penumbra)

### `rhino.*` (3)

- `rhino.command` (Penumbra)
- `rhino.doc.open` (Penumbra)
- `rhino.doc.save` (Penumbra)

### `scene.*` (1)

- `scene.loaded` (Penumbra)

<!-- END GENERATED EVENT CATALOG -->
