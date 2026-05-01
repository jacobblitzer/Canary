# Penumbra Visual Regression — Expected Output Reference

This document describes the expected visual output for each Canary test JSON.
Use it to verify that baseline screenshots match the intended rendering.

## Scene Expectations (Default Eval Mode)

| Scene | Index | Test File | Expected Visual |
|-------|-------|-----------|-----------------|
| Box - Sphere (Tape) | 0 | `tape-csg-orbit.json` | Sharp-edged box with spherical cavity, warm orange material, dark background. Crisp boolean difference edges. |
| 12-Sphere Blob (Atlas) | 1 | `atlas-blob-orbit.json` | Organic blobby shape from 12 smooth-unioned spheres, blue material, smooth normals. Soft organic silhouette. |
| Multi-Field (3 primitives) | 2 | `multi-field-orbit.json` | Red sphere, green box, blue torus — separated ~7 units apart. Animation paused by Canary hooks. |
| Hybrid (Tape + Atlas) | 3 | `hybrid-default.json` | Blue blob (atlas) surrounded by orange torus ring (tape). Both eval paths active simultaneously. |
| Cornell Box (7 fields) | 4 | `cornell-box-default.json` | Classic Cornell box: red left wall, green right wall, white floor/ceiling/back, two white boxes inside. |
| SDF Teapot (atlas CSG) | 5 | `teapot-default.json` | Utah-teapot-shaped organic form: body, spout, handle, lid, knob — cream/beige material, ~20 smooth-union parts. |
| Procedural Terrain | 6 | `terrain-default.json` | Green procedural terrain (displaced box) with blue translucent water plane below. Rolling hills. |
| Mechanical Assembly (hybrid) | 7 | `assembly-default.json` | Metal base plate with bolt holes, shaft, bearing housing, gear with teeth, bolts. Hybrid tape+atlas. |
| 64-Sphere Stress Test | 8 | `stress-test-orbit.json` | Large organic blob from 64 smooth-unioned spheres, blue material, fills most of the viewport. |
| Repeated Columns | 9 | `columns-default.json` | 3x3 grid of grooved columns on a stone slab, warm stone color. Repeat warp + difference grooves. |

## Display Mode Expectations

| Mode | Kind String | Visual Effect |
|------|-------------|---------------|
| Tape Eval | `eval-tape` | Analytical ray marching — sharper edges, no voxel artifacts, potentially slower on complex scenes. |
| Atlas Eval | `eval-atlas` | Trilinear atlas sampling — smoother gradients, possible brick-edge artifacts on complex shapes. |
| Normal Viz | `viz-normal` | Surface painted with normal direction: R=X, G=Y, B=Z. Rainbow-like color mapping over the surface. |
| Field ID Viz | `viz-field-id` | Each SDF field gets a unique false-color (1st=red, 2nd=green, 3rd=blue, etc.). |
| Cascade Viz | `viz-cascades` | Cascade LOD bands painted in distinct colors — shows spatial coverage of each cascade level. |
| Centroid Viz | `viz-centroids` | Field centroid points visualized on surface. |
| AABB Viz | `viz-aabbs` | Axis-aligned bounding boxes visualized as shaded regions. |
| Wire: Field AABBs | `wire-fieldAABBs` | Wireframe AABB per field, overlaid on normal rendering. |
| Wire: Atom AABBs | `wire-atomAABBs` | Wireframe AABB per atom (finer granularity than field AABBs). |
| Wire: Cascades | `wire-cascades` | Wireframe showing cascade grid boundaries overlaid on normal rendering. |
| Wire: Bricks | `wire-bricks` | Green wireframe boxes over populated atlas bricks. |
| Point Cloud | `point-cloud` | Fibonacci lattice sample points rendered as colored dots over the scene. |

## Display Mode Test Expectations

| Test File | Scene + Mode | What To Look For |
|-----------|-------------|------------------|
| `atlas-blob-eval-tape.json` | Blob + tape | Same blob shape as default, but rendered via analytical tape eval — compare silhouette with atlas version. |
| `atlas-blob-viz-normal.json` | Blob + normal | Smooth rainbow gradient over the blob surface. No sharp color discontinuities (smooth normals). |
| `atlas-blob-viz-cascades.json` | Blob + cascades | Blob painted in cascade-band colors. Inner cascades (fine resolution) near surface, outer cascades at edges. |
| `teapot-viz-normal.json` | Teapot + normal | Rainbow normal map on teapot. Key: smooth-union seams between body/spout/handle should have smooth color transitions. |
| `teapot-eval-tape.json` | Teapot + tape | Teapot via tape eval. Sharper edges vs atlas, but ~20 smooth-union parts may be slow. Compare quality. |
| `assembly-viz-field-id.json` | Assembly + field-id | 8 distinct colors — one per assembly part: base, shaft, housing, gear, 4 bolts. |
| `multi-field-viz-field-id.json` | Multi-Field + field-id | 3 distinct colors: sphere=red, box=green, torus=blue (matching field order). |
| `columns-viz-normal.json` | Columns + normal | Repeating pattern of identical normal-mapped columns. All columns should have identical coloring. |

## Overlay Test Expectations

| Test File | Scene + Overlay | What To Look For |
|-----------|----------------|------------------|
| `atlas-blob-bricks-overlay.json` | Blob + bricks | Green wireframe brick boxes concentrated around the blob surface. Dense where surface detail is highest. |
| `atlas-blob-cascades-overlay.json` | Blob + cascades | Cascade grid wireframe boxes. Multiple nested grids (one per cascade level) around the blob. |
| `assembly-fieldAABBs-overlay.json` | Assembly + field AABBs | 8 wireframe boxes (one per field) outlining each assembly component. |
| `assembly-atomAABBs-overlay.json` | Assembly + atom AABBs | Many small wireframe boxes (one per atom). Finer than field AABBs. |
| `teapot-bricks-overlay.json` | Teapot + bricks | Dense brick wireframes around the organic teapot surface. High brick count due to smooth-union complexity. |
| `multi-field-point-cloud.json` | Multi-Field + point cloud | Colored dots (Fibonacci lattice) sampled on sphere, box, and torus surfaces. |
| `stress-test-cascades-overlay.json` | Stress + cascades | Large cascade wireframes covering the 64-sphere blob. Multiple cascade levels visible. |

## Camera Angle Reference

All tests use standardized camera angles:

| Angle Name | Azimuth | Elevation | Distance Modifier | Purpose |
|------------|---------|-----------|-------------------|---------|
| front | 0 | 15 | d | Primary face-on view |
| three-quarter | 45 | 30 | d | Shows depth and perspective |
| top-down | 0 | 80 | d x 1.3 | Overhead — catches top-surface artifacts |
| back | 180 | 15 | d | Rear view — catches back-face issues |
| side | 90 | 15 | d | Profile view (used in some existing tests) |
| wide | 30 | 20 | d x 1.75 | Distant view for context (overlays) |

## VLM Oracle Test Expectations

VLM tests use natural-language descriptions instead of pixel baselines. The VLM evaluates *what* the screenshot shows rather than whether it matches a reference image. These tests are useful for catching semantic regressions that pixel-diff might miss (e.g., a scene renders at the correct resolution but shows the wrong geometry).

| Test File | Scene | What the VLM Checks |
|-----------|-------|---------------------|
| `vlm-tape-csg-geometry.json` | Box - Sphere | Box with spherical cavity visible, warm orange material, sharp boolean edges |
| `vlm-atlas-blob-organic.json` | 12-Sphere Blob | Organic blobby shape, smooth normals, no brick artifacts, blue material |
| `vlm-cornell-box-layout.json` | Cornell Box | Red left wall, green right wall, white floor/ceiling, two interior boxes |
| `vlm-teapot-shape.json` | SDF Teapot | Recognizable teapot silhouette — body, spout, handle, lid all present |
| `vlm-multi-field-separation.json` | Multi-Field | Three separated primitives: red sphere, green box, blue torus |
| `vlm-terrain-landscape.json` | Procedural Terrain | Green undulating terrain with blue water plane below |
| `vlm-teapot-metal-mixed.json` | SDF Teapot + Metal | Mixed mode: pixel-diff baseline + VLM verifies metallic sheen/reflections |

### Writing effective VLM descriptions

- **Be specific about geometry**: name shapes, count objects, describe spatial layout.
- **Mention colors**: the VLM can distinguish material colors and wall colors.
- **Describe what should NOT be present**: "no error dialogs", "no black holes", "no grid artifacts".
- **Reference material properties**: "shiny/metallic", "matte", "translucent", "smooth normals".
- **Avoid pixel-level precision**: don't say "the sphere is at pixel (480, 270)"; say "the sphere is roughly centered".

## Common Regression Indicators

- **Brick-edge artifacts**: Visible grid lines in atlas eval mode — usually a trilinear sampling boundary issue.
- **Missing geometry**: Part of a scene doesn't render — tape compiler register overflow or constant pool error.
- **Normal discontinuities**: Sharp color boundaries in viz-normal mode where smooth normals are expected.
- **Cascade gaps**: Visible holes in cascade-viz mode — cascade parameter miscalculation.
- **Overlay misalignment**: Wireframe boxes don't match rendered geometry — world-space transform mismatch.
- **Black screen**: Shader compilation failure or GPU timeout — check console logs.
