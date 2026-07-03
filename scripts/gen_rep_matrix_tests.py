#!/usr/bin/env python3
"""Generate the cpig-repmatrix-* test JSONs + the cpig-display-matrix aggregate suite (R1.3 / D7).

Matrix: (sphere | box | gyroid | mesh) x (auto | tape | atlasBaked | mesh | pointCloud | companionTape)
= 24 tests. The `procedural` rep column is PRUNED (4 cells): it renders a per-atom-seeded fbm
noise blob that is independent of the field's content — lowest regression value of the 7 reps,
and the dispatch itself is already locked once by cpig-fieldops-04's full 7-rep cycle.
"Invalid" combos that DEGRADE by design (e.g. tape rep on the dense mesh field -> companion
stand-in) are KEPT per the D7 plan: the documented fallback is itself the behavior to lock.

Rep targeting: _CPigDisplayRep has no direct-set argument — it CYCLES the selected field's
per-field rep (auto -> tape -> atlasBaked -> procedural -> mesh -> pointCloud -> companionTape).
A fresh field always starts at the stored default "auto", so rep k is reached deterministically
by selecting the field and invoking the command k times.

Camera: the cpig-booleans deterministic solo-perspective recipe (decoy sphere r=45 at origin ->
_Zoom _Selected -> lens 35->50 pump -> delete decoy), cloned verbatim.

Regenerate: python scripts/gen_rep_matrix_tests.py [--mode capture|pixel-diff|vlm]
(from C:/Repos/Canary). Idempotent — rewrites all 24 test files + the suite file in place.
Default mode is capture (pre-baseline). After the operator approves baselines
(STOP-POINT R1.3), re-run with --mode pixel-diff to flip every checkpoint.
"""
import argparse
import json
import os

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "workloads", "rhino")
TESTS_DIR = os.path.normpath(os.path.join(ROOT, "tests"))
SUITES_DIR = os.path.normpath(os.path.join(ROOT, "suites"))

# Per-field rep cycle order in CPigDisplayRepCommand.FieldReps (CPig.Rhino). Index = number of
# _CPigDisplayRep invocations needed from a fresh field (stored rep defaults to "auto").
REP_CYCLE = ["auto", "tape", "atlasBaked", "procedural", "mesh", "pointCloud", "companionTape"]
REPS = [r for r in REP_CYCLE if r != "procedural"]  # pruned column, see module docstring

TYPES = {
    "sphere": {
        "create": ["_CPigSphere 0,0,0 10"],
        "post_wait": [],
        "base_desc": "structural tape sphere (r=10 at origin)",
        "base_vlm": "a smooth ray-marched SDF ball",
    },
    "box": {
        "create": ["_CPigBox -12,-12,-12 12,12,-12 12,12,12"],
        "post_wait": [],
        "base_desc": "structural tape box ([-12..12]^3)",
        "base_vlm": "a solid ray-marched SDF cube with flat faces and sharp edges",
    },
    "gyroid": {
        "create": ["_CPigGyroid 20 1 -25,-25,-25 0,0,-25 0,0,0"],
        "post_wait": [],
        "base_desc": "structural tape gyroid (period 20, thickness 1, clipped to the [-25..0]^3 box)",
        "base_vlm": "a periodic gyroid lattice (interwoven smooth curved channels) filling a cube region",
    },
    "mesh": {
        "create": ["_-MeshSphere 0,0,0 10", "_SelLast _CPigFromMesh"],
        "post_wait": ["_SelLast _Invert _Hide"],
        "base_desc": "DENSE mesh-derived field (_CPigFromMesh of a MeshSphere r=10; voxel-bakes on export)",
        "base_vlm": "a smooth ray-marched SDF ball derived from a mesh sphere",
    },
}

# Cloned verbatim from cpig-booleans-00 (deterministic solo-perspective framing; the decoy
# sphere gives _Zoom _Selected a scene-scale target, then dies before capture).
CAMERA_RECIPE = [
    "_SelNone",
    "-_4View _Enter",
    "-_MaxViewport",
    "-_SetView _World _Perspective",
    "-_Sphere 0,0,0 45",
    "_SelNone",
    "_SelLast",
    "-_Zoom _Selected",
    "-_ViewportProperties _Lens 35 _EnterEnd",
    "-_ViewportProperties _Lens 50 _EnterEnd",
    "_Delete",
]

REP_VLM = {
    "auto": "drawn as a solid surface (the default representation)",
    "tape": "drawn as a solid, analytically crisp surface",
    "atlasBaked": "drawn from a voxel atlas: slightly softened or eroded features are the accepted look",
    "mesh": "drawn as a coarse faceted marching-cubes shell, visibly polygonal",
    "pointCloud": "drawn as a cloud of small dots tracing the surface, NOT a solid surface",
    "companionTape": "drawn as a plain bounding-sphere stand-in ball enclosing the field extents, NOT the detailed shape",
}


def vlm_description(type_key: str, rep: str) -> str:
    base = TYPES[type_key]["base_vlm"]
    rep_part = REP_VLM[rep]
    notes = []
    if rep == "companionTape":
        # The stand-in is a ball for every type — the TYPE identity is intentionally lost.
        return ("A plain sphere/ball stand-in (the companion representation) roughly enclosing where the "
                f"{type_key} field sits. It is EXPECTED to look like a simple ball, not the detailed shape. "
                "NOT an empty grey viewport.")
    if type_key == "mesh" and rep == "tape":
        notes.append("A dense field asked for the tape rep DEGRADES to the companion bounding-sphere "
                     "stand-in by design — a plain ball here is the EXPECTED, correct fallback")
    if type_key == "gyroid" and rep == "atlasBaked":
        notes.append("thin lattice walls may look eroded/thinner than the tape rep — the accepted "
                     "atlas voxelization look (R5 territory)")
    tail = (". " + "; ".join(notes)) if notes else ""
    return f"{base}, {rep_part}{tail}. NOT an empty grey viewport."


def build_test(type_key: str, rep: str, mode: str = "capture") -> dict:
    t = TYPES[type_key]
    k = REP_CYCLE.index(rep)
    name = f"cpig-repmatrix-{type_key}-{rep.lower()}"

    actions = [{"type": "RunCommand", "command": "_SelAll _Delete"}]
    for c in t["create"]:
        actions.append({"type": "RunCommand", "command": c})
    actions.append({"type": "WaitForPenumbraFrame", "requireReal": True, "timeoutMs": 110000})
    for c in t["post_wait"]:
        actions.append({"type": "RunCommand", "command": c})
    for c in CAMERA_RECIPE:
        actions.append({"type": "RunCommand", "command": c})
    if k > 0:
        actions.append({"type": "RunCommand", "command": "_SelAll"})
        for _ in range(k):
            actions.append({"type": "RunCommand", "command": "_CPigDisplayRep"})
            actions.append({"type": "WaitForPenumbraFrame", "requireReal": True, "timeoutMs": 60000})
    actions.append({"type": "RunCommand", "command": "_SelNone"})
    actions.append({"type": "WaitForPenumbraFrame", "requireReal": True, "requireSteady": True, "timeoutMs": 90000})

    cycles = (f"select + {k}x _CPigDisplayRep (cycle reaches '{rep}' from the fresh-field 'auto' default)"
              if k > 0 else "no rep change ('auto' is the fresh-field default)")
    return {
        "name": name,
        "workload": "rhino",
        "description": (
            f"D7 rep-matrix cell ({type_key} x {rep}), R1.3 2026-07-03. Creates a {t['base_desc']}, "
            f"applies the deterministic solo-perspective camera recipe (decoy-sphere framing, cloned from "
            f"cpig-booleans), then {cycles}, gates on steady+bakes-drained, captures. "
            "mode:capture until the operator approves baselines (STOP-POINT R1.3), then flips to pixel-diff. "
            "Degrade-by-design cells (dense field asked as tape -> companion stand-in) are kept deliberately: "
            "the documented fallback is the behavior to lock. Regenerate: scripts/gen_rep_matrix_tests.py."
        ),
        "runMode": "shared",
        "setup": {
            "viewport": {"projection": "Perspective", "displayMode": "Shaded", "width": 800, "height": 600},
            "vlm": {"provider": "ollama", "model": "qwen2.5vl:7b"},
            "vlmDescription": vlm_description(type_key, rep),
        },
        "actions": actions,
        "checkpoints": [{
            "name": name,
            "atTimeMs": 0,
            "source": "viewport",
            "mode": mode,
            "stabilizeMs": 5000,
            "tolerance": 0.05,
            "description": f"{type_key} field rendered with the '{rep}' per-field representation.",
        }],
    }


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--mode", default="capture", choices=["capture", "pixel-diff", "vlm"],
                    help="checkpoint mode for all 24 tests (capture pre-baseline; pixel-diff after approval)")
    args = ap.parse_args()

    written = []
    for type_key in TYPES:
        for rep in REPS:
            test = build_test(type_key, rep, args.mode)
            path = os.path.join(TESTS_DIR, test["name"] + ".json")
            with open(path, "w", encoding="utf-8", newline="\n") as f:
                json.dump(test, f, indent=2)
                f.write("\n")
            written.append(test["name"])

    suite = {
        "name": "cpig-display-matrix",
        "description": (
            "D7 display-correctness aggregate (R1.3, 2026-07-03): the 24-cell rep matrix "
            "(sphere|box|gyroid|mesh) x (auto|tape|atlasBaked|mesh|pointCloud|companionTape) — the "
            "'procedural' rep column is pruned (field-independent fbm debug rep; dispatch already locked "
            "once by cpig-fieldops-04's full cycle) — plus the D1 dense-rep guard and the 4 scriptable "
            "boolean cells. All shared-runMode (ONE Rhino). Capture-mode until the operator approves "
            "baselines (canary approve --workload rhino --suite cpig-display-matrix), then checkpoints "
            "flip capture->pixel-diff. NOTE: the D5 deliverable cpig-fieldops-persistence-boolean-with-mesh "
            "does not exist yet — add it here when it lands. Regenerate the repmatrix tests: "
            "python scripts/gen_rep_matrix_tests.py."
        ),
        "tests": written + [
            "cpig-fieldops-rep-dense-pointcloud-mesh",
            "cpig-booleans-00-union-sphere-sphere",
            "cpig-booleans-01-intersection-sphere-sphere",
            "cpig-booleans-03-smoothunion-sphere-sphere",
            "cpig-booleans-04-union-sphere-gyroid",
        ],
    }
    suite_path = os.path.join(SUITES_DIR, "cpig-display-matrix.json")
    with open(suite_path, "w", encoding="utf-8", newline="\n") as f:
        json.dump(suite, f, indent=2)
        f.write("\n")

    print(f"wrote {len(written)} tests + {os.path.basename(suite_path)} ({len(suite['tests'])} suite entries)")


if __name__ == "__main__":
    main()
