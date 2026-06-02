"""
Retrofit every cpig-kin-*.json canary test to the Phase 14.7 four-view
checkpoint pattern: replace the single 'post-build' checkpoint with four
viewport-tagged checkpoints (front / top / right / persp). Tests that had a
capture block (gif/scrub) get that block on the 'persp' checkpoint; the other
three are static captures.

One-shot script. Idempotent: re-running on already-retrofitted tests is a no-op
(detected by the presence of four checkpoints named front/top/right/persp).
"""
import json
import sys
from pathlib import Path


TESTS_DIR = Path(r"C:\Repos\Canary\workloads\rhino\tests")
DISPLAY_MODE = "Shaded"


def viewport(projection):
    return {"projection": projection, "displayMode": DISPLAY_MODE}


def build_checkpoint(name, projection, src_cp, capture=None):
    cp = {
        "name": name,
        "atTimeMs": src_cp.get("atTimeMs", 5000),
        "tolerance": src_cp.get("tolerance", 0.02),
        "viewport": viewport(projection),
    }
    if capture is not None:
        cp["capture"] = capture
    if "description" in src_cp and src_cp["description"]:
        cp["description"] = src_cp["description"]
    return cp


def retrofit_one(path):
    with open(path, "r", encoding="utf-8") as f:
        data = json.load(f)

    checkpoints = data.get("checkpoints", [])
    names = [cp.get("name") for cp in checkpoints]
    if names == ["front", "top", "right", "persp"]:
        return False  # already retrofitted

    if not checkpoints:
        print(f"  SKIP {path.name}: no checkpoints")
        return False

    src = checkpoints[0]
    capture = src.get("capture")  # preserved on persp checkpoint

    new_checkpoints = [
        build_checkpoint("front", "Front",       src, capture=None),
        build_checkpoint("top",   "Top",         src, capture=None),
        build_checkpoint("right", "Right",       src, capture=None),
        build_checkpoint("persp", "Perspective", src, capture=capture),
    ]
    data["checkpoints"] = new_checkpoints

    with open(path, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=4)
        f.write("\n")
    return True


def main():
    files = sorted(TESTS_DIR.glob("cpig-kin-*.json"))
    print(f"Found {len(files)} cpig-kin-*.json under {TESTS_DIR}")
    changed = 0
    for p in files:
        if retrofit_one(p):
            changed += 1
            print(f"  retrofit  {p.name}")
        else:
            print(f"  skip      {p.name}  (already 4-view)")
    print(f"Done. {changed} file(s) retrofitted.")


if __name__ == "__main__":
    main()
