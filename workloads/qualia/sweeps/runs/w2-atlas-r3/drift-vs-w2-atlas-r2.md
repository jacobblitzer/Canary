# Drift report — w2-atlas-r3 vs reference w2-atlas-r2

**DRIFT DETECTED: 55 change(s).** The dossier (Qualia/spec/DISPLAY-BEHAVIOR.md) no longer matches measured behavior — fix the regression or deliberately update the dossier + REFERENCE-RUN.json.

## Activity flips (loudest drift — wiring appeared/died) (1)

- `minimal-ddv|profile-minimal` — ACTIVE → **INERT** (2 → 0 paths)

## Error flips (7)

- `cinematic-ddv|persona-fx-pencil-toon` — ERROR → **clean**
- `minimal-ddv|persona-fx-pencil-toon` — ERROR → **clean**
- `minimal-qnode-junction-alignment-test|persona-fx-pencil-toon` — ERROR → **clean**
- `minimal-workshop-palette|persona-fx-pencil-toon` — ERROR → **clean**
- `standard-ddv|persona-fx-pencil-toon` — ERROR → **clean**
- `standard-workshop-palette|persona-fx-pencil-toon` — ERROR → **clean**
- `workshop-ddv|persona-fx-pencil-toon` — ERROR → **clean**

## Leak changes (0055-class regressions) (11)

- `cinematic-ddv|persona-render-paper` — appeared: —; disappeared: `touched.0`
- `minimal-ddv|persona-render-paper` — appeared: —; disappeared: `touched.0`
- `minimal-qnode-junction-alignment-test|persona-render-paper` — appeared: —; disappeared: `touched.0`
- `minimal-workshop-palette|persona-render-paper` — appeared: —; disappeared: `touched.0`
- `standard-ddv|persona-render-paper` — appeared: —; disappeared: `touched.0`
- `standard-workshop-palette|persona-render-paper` — appeared: —; disappeared: `touched.0`
- `workshop-ddv|persona-render-paper` — appeared: —; disappeared: `touched.0`
- `minimal-ddv|theme-light` — appeared: —; disappeared: `viewer.emissiveIntensity`
- `minimal-ddv|profile-standard` — appeared: —; disappeared: `viewer.emissiveIntensity`
- `minimal-ddv|profile-workshop` — appeared: —; disappeared: `viewer.emissiveIntensity`
- `minimal-ddv|profile-cinematic` — appeared: —; disappeared: `viewer.emissiveIntensity`

## Effect-signature changes (same activity, different paths) (36)

- `minimal-ddv|persona-render-junction-bubble` — +[frame.junctions.*.target.intersection] −[]
- `cinematic-ddv|persona-render-paper` — +[] −[touched.0]
- `minimal-ddv|persona-render-paper` — +[] −[touched.0]
- `minimal-qnode-junction-alignment-test|persona-render-paper` — +[] −[touched.0]
- `minimal-workshop-palette|persona-render-paper` — +[] −[touched.0]
- `standard-ddv|persona-render-paper` — +[] −[touched.0]
- `standard-workshop-palette|persona-render-paper` — +[] −[touched.0]
- `workshop-ddv|persona-render-paper` — +[] −[touched.0]
- `minimal-ddv|junction-bubble` — +[frame.junctions.*.target.intersection] −[]
- `cinematic-ddv|profile-minimal` — +[] −[persona.enabled.compute.layout]
- `minimal-qnode-junction-alignment-test|profile-minimal` — +[] −[persona.enabled.compute.layout]
- `minimal-workshop-palette|profile-minimal` — +[] −[persona.enabled.compute.layout]
- `standard-ddv|profile-minimal` — +[] −[persona.enabled.compute.layout]
- `standard-workshop-palette|profile-minimal` — +[] −[persona.enabled.compute.layout]
- `workshop-ddv|profile-minimal` — +[] −[persona.enabled.compute.layout]
- `cinematic-ddv|profile-standard` — +[] −[persona.enabled.compute.layout]
- `minimal-ddv|profile-standard` — +[] −[persona.enabled.compute.layout, viewer.emissiveIntensity]
- `minimal-qnode-junction-alignment-test|profile-standard` — +[] −[persona.enabled.compute.layout]
- `minimal-workshop-palette|profile-standard` — +[] −[persona.enabled.compute.layout]
- `standard-ddv|profile-standard` — +[] −[persona.enabled.compute.layout]
- `standard-workshop-palette|profile-standard` — +[] −[persona.enabled.compute.layout]
- `workshop-ddv|profile-standard` — +[] −[persona.enabled.compute.layout]
- `cinematic-ddv|profile-workshop` — +[] −[persona.enabled.compute.layout]
- `minimal-ddv|profile-workshop` — +[] −[persona.enabled.compute.layout, viewer.emissiveIntensity]
- `minimal-qnode-junction-alignment-test|profile-workshop` — +[] −[persona.enabled.compute.layout]
- `minimal-workshop-palette|profile-workshop` — +[] −[persona.enabled.compute.layout]
- `standard-ddv|profile-workshop` — +[] −[persona.enabled.compute.layout]
- `standard-workshop-palette|profile-workshop` — +[] −[persona.enabled.compute.layout]
- `workshop-ddv|profile-workshop` — +[] −[persona.enabled.compute.layout]
- `cinematic-ddv|profile-cinematic` — +[] −[persona.enabled.compute.layout]
- `minimal-ddv|profile-cinematic` — +[] −[persona.enabled.compute.layout, viewer.emissiveIntensity]
- `minimal-qnode-junction-alignment-test|profile-cinematic` — +[] −[persona.enabled.compute.layout]
- `minimal-workshop-palette|profile-cinematic` — +[] −[persona.enabled.compute.layout]
- `standard-ddv|profile-cinematic` — +[] −[persona.enabled.compute.layout]
- `standard-workshop-palette|profile-cinematic` — +[] −[persona.enabled.compute.layout]
- `workshop-ddv|profile-cinematic` — +[] −[persona.enabled.compute.layout]

## INFO: states only in reference (removed levers?) (0)

_none_

## INFO: states only in candidate (new levers — extend the dossier) (7)

- `cinematic-ddv|persona-fx-pencil-toon`
- `minimal-ddv|persona-fx-pencil-toon`
- `minimal-qnode-junction-alignment-test|persona-fx-pencil-toon`
- `minimal-workshop-palette|persona-fx-pencil-toon`
- `standard-ddv|persona-fx-pencil-toon`
- `standard-workshop-palette|persona-fx-pencil-toon`
- `workshop-ddv|persona-fx-pencil-toon`
