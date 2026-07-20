# Drift report — w2-atlas-r6 vs reference w2-atlas-r5

**DRIFT DETECTED: 11 change(s).** The dossier (Qualia/spec/DISPLAY-BEHAVIOR.md) no longer matches measured behavior — fix the regression or deliberately update the dossier + REFERENCE-RUN.json.

## Activity flips (loudest drift — wiring appeared/died) (0)

_none_

## Error flips (0)

_none_

## Leak changes (0055-class regressions) (1)

- `workshop-ddv|persona-render-grid` — appeared: —; disappeared: `sceneGraph.byType.Mesh`, `sceneGraph.total`

## Effect-signature changes (same activity, different paths) (10)

- `workshop-ddv|persona-render-grid` — +[] −[sceneGraph.byType.Mesh, sceneGraph.total]
- `workshop-ddv|profile-minimal` — +[] −[persona.enabled.debug.overlay, persona.enabled.debug.snapshot, persona.enabled.interaction.box-select]
- `workshop-ddv|profile-standard` — +[] −[persona.enabled.debug.overlay, persona.enabled.debug.snapshot, persona.enabled.interaction.box-select]
- `cinematic-ddv|profile-workshop` — +[] −[persona.enabled.debug.overlay, persona.enabled.debug.snapshot, persona.enabled.interaction.box-select]
- `minimal-ddv|profile-workshop` — +[] −[persona.enabled.debug.overlay, persona.enabled.debug.snapshot, persona.enabled.interaction.box-select]
- `minimal-qnode-junction-alignment-test|profile-workshop` — +[] −[persona.enabled.debug.overlay, persona.enabled.debug.snapshot, persona.enabled.interaction.box-select]
- `minimal-workshop-palette|profile-workshop` — +[] −[persona.enabled.debug.overlay, persona.enabled.debug.snapshot, persona.enabled.interaction.box-select]
- `standard-ddv|profile-workshop` — +[] −[persona.enabled.debug.overlay, persona.enabled.debug.snapshot, persona.enabled.interaction.box-select]
- `standard-workshop-palette|profile-workshop` — +[] −[persona.enabled.debug.overlay, persona.enabled.debug.snapshot, persona.enabled.interaction.box-select]
- `workshop-ddv|profile-cinematic` — +[] −[persona.enabled.debug.overlay, persona.enabled.debug.snapshot, persona.enabled.interaction.box-select]

## INFO: states only in reference (removed levers?) (84)

- `cinematic-ddv|persona-render-nodes`
- `minimal-ddv|persona-render-nodes`
- `minimal-qnode-junction-alignment-test|persona-render-nodes`
- `minimal-workshop-palette|persona-render-nodes`
- `standard-ddv|persona-render-nodes`
- `standard-workshop-palette|persona-render-nodes`
- `workshop-ddv|persona-render-nodes`
- `cinematic-ddv|persona-render-edges`
- `minimal-ddv|persona-render-edges`
- `minimal-qnode-junction-alignment-test|persona-render-edges`
- `minimal-workshop-palette|persona-render-edges`
- `standard-ddv|persona-render-edges`
- `standard-workshop-palette|persona-render-edges`
- `workshop-ddv|persona-render-edges`
- `cinematic-ddv|persona-render-labels`
- `minimal-ddv|persona-render-labels`
- `minimal-qnode-junction-alignment-test|persona-render-labels`
- `minimal-workshop-palette|persona-render-labels`
- `standard-ddv|persona-render-labels`
- `standard-workshop-palette|persona-render-labels`
- `workshop-ddv|persona-render-labels`
- `cinematic-ddv|persona-compute-layout`
- `minimal-ddv|persona-compute-layout`
- `minimal-qnode-junction-alignment-test|persona-compute-layout`
- `minimal-workshop-palette|persona-compute-layout`
- `standard-ddv|persona-compute-layout`
- `standard-workshop-palette|persona-compute-layout`
- `workshop-ddv|persona-compute-layout`
- `cinematic-ddv|persona-compute-lod`
- `minimal-ddv|persona-compute-lod`
- `minimal-qnode-junction-alignment-test|persona-compute-lod`
- `minimal-workshop-palette|persona-compute-lod`
- `standard-ddv|persona-compute-lod`
- `standard-workshop-palette|persona-compute-lod`
- `workshop-ddv|persona-compute-lod`
- `cinematic-ddv|persona-compute-edge-routing`
- `minimal-ddv|persona-compute-edge-routing`
- `minimal-qnode-junction-alignment-test|persona-compute-edge-routing`
- `minimal-workshop-palette|persona-compute-edge-routing`
- `standard-ddv|persona-compute-edge-routing`
- `standard-workshop-palette|persona-compute-edge-routing`
- `workshop-ddv|persona-compute-edge-routing`
- `cinematic-ddv|persona-interaction-hover`
- `minimal-ddv|persona-interaction-hover`
- `minimal-qnode-junction-alignment-test|persona-interaction-hover`
- `minimal-workshop-palette|persona-interaction-hover`
- `standard-ddv|persona-interaction-hover`
- `standard-workshop-palette|persona-interaction-hover`
- `workshop-ddv|persona-interaction-hover`
- `cinematic-ddv|persona-interaction-drag`
- `minimal-ddv|persona-interaction-drag`
- `minimal-qnode-junction-alignment-test|persona-interaction-drag`
- `minimal-workshop-palette|persona-interaction-drag`
- `standard-ddv|persona-interaction-drag`
- `standard-workshop-palette|persona-interaction-drag`
- `workshop-ddv|persona-interaction-drag`
- `cinematic-ddv|persona-interaction-box-select`
- `minimal-ddv|persona-interaction-box-select`
- `minimal-qnode-junction-alignment-test|persona-interaction-box-select`
- `minimal-workshop-palette|persona-interaction-box-select`
- `standard-ddv|persona-interaction-box-select`
- `standard-workshop-palette|persona-interaction-box-select`
- `workshop-ddv|persona-interaction-box-select`
- `cinematic-ddv|persona-interaction-fly-to`
- `minimal-ddv|persona-interaction-fly-to`
- `minimal-qnode-junction-alignment-test|persona-interaction-fly-to`
- `minimal-workshop-palette|persona-interaction-fly-to`
- `standard-ddv|persona-interaction-fly-to`
- `standard-workshop-palette|persona-interaction-fly-to`
- `workshop-ddv|persona-interaction-fly-to`
- `cinematic-ddv|persona-debug-snapshot`
- `minimal-ddv|persona-debug-snapshot`
- `minimal-qnode-junction-alignment-test|persona-debug-snapshot`
- `minimal-workshop-palette|persona-debug-snapshot`
- `standard-ddv|persona-debug-snapshot`
- `standard-workshop-palette|persona-debug-snapshot`
- `workshop-ddv|persona-debug-snapshot`
- `cinematic-ddv|persona-debug-overlay`
- `minimal-ddv|persona-debug-overlay`
- `minimal-qnode-junction-alignment-test|persona-debug-overlay`
- `minimal-workshop-palette|persona-debug-overlay`
- `standard-ddv|persona-debug-overlay`
- `standard-workshop-palette|persona-debug-overlay`
- `workshop-ddv|persona-debug-overlay`

## INFO: states only in candidate (new levers — extend the dossier) (0)

_none_
