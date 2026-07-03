#!/usr/bin/env python3
"""Assemble the cpig-display-matrix candidate contact sheet (R1.3 item 3).

Tiles the suite's candidate captures into one labeled grid PNG for the operator's
baseline-approval eyeball (STOP-POINT R1.3): 4 matrix rows (sphere/box/gyroid/mesh) x
6 rep columns (auto/tape/atlasBaked/mesh/pointCloud/companionTape), plus an extras row
(the D1 dense-rep guard + the 4 scriptable boolean cells).

Reads the SHARED results layout (workloads/rhino/results/<test>/candidates/<test>.png).
Missing cells render as a labeled grey placeholder — a missing cell means the test did
not produce a capture and must be investigated before approval.

Usage: python scripts/gen_contact_sheet.py [out.png]   (from C:/Repos/Canary)
"""
import os
import sys

from PIL import Image, ImageDraw

ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "workloads", "rhino", "results"))

TYPES = ["sphere", "box", "gyroid", "mesh"]
REPS = ["auto", "tape", "atlasbaked", "mesh", "pointcloud", "companiontape"]
REP_HEADERS = ["auto", "tape", "atlasBaked", "mesh", "pointCloud", "companionTape"]
EXTRAS = [
    ("cpig-fieldops-rep-dense-pointcloud-mesh", "cpig-fieldops-rep-dense-after-cycle", "D1 dense cycle"),
    ("cpig-booleans-00-union-sphere-sphere", "cpig-booleans-union-sphere-sphere", "bool union"),
    ("cpig-booleans-01-intersection-sphere-sphere", "cpig-booleans-intersection-sphere-sphere", "bool intersection"),
    ("cpig-booleans-03-smoothunion-sphere-sphere", "cpig-booleans-smoothunion-sphere-sphere", "bool smoothUnion"),
    ("cpig-booleans-04-union-sphere-gyroid", "cpig-booleans-union-sphere-gyroid", "bool union+gyroid"),
]

CELL_W, CELL_H = 372, 211        # ~1/3 of the 1116x632 viewport capture
LABEL_H = 22                     # per-cell caption strip
HEADER_H = 30                    # column-header row
ROWLABEL_W = 90                  # row-label gutter
GAP = 6
BG = (28, 28, 30)
FG = (235, 235, 235)
MISS_BG = (70, 40, 40)


def load_cell(test: str, checkpoint: str):
    p = os.path.join(ROOT, test, "candidates", f"{checkpoint}.png")
    if not os.path.exists(p):
        return None
    img = Image.open(p).convert("RGB")
    return img.resize((CELL_W, CELL_H))


def main() -> None:
    out_path = sys.argv[1] if len(sys.argv) > 1 else "cpig-display-matrix-contact-sheet.png"
    cols, rows = len(REPS), len(TYPES) + 1   # +1 extras row
    W = ROWLABEL_W + cols * (CELL_W + GAP) + GAP
    H = HEADER_H + rows * (CELL_H + LABEL_H + GAP) + GAP
    sheet = Image.new("RGB", (W, H), BG)
    d = ImageDraw.Draw(sheet)

    for c, h in enumerate(REP_HEADERS):
        x = ROWLABEL_W + GAP + c * (CELL_W + GAP)
        d.text((x + 4, 8), h, fill=FG)

    missing = []
    for r, t in enumerate(TYPES):
        y = HEADER_H + GAP + r * (CELL_H + LABEL_H + GAP)
        d.text((6, y + CELL_H // 2), t, fill=FG)
        for c, rep in enumerate(REPS):
            x = ROWLABEL_W + GAP + c * (CELL_W + GAP)
            test = f"cpig-repmatrix-{t}-{rep}"
            img = load_cell(test, test)
            if img is None:
                d.rectangle([x, y, x + CELL_W, y + CELL_H], fill=MISS_BG)
                d.text((x + 8, y + CELL_H // 2), "MISSING", fill=FG)
                missing.append(test)
            else:
                sheet.paste(img, (x, y))
            d.text((x + 4, y + CELL_H + 4), test, fill=FG)

    r = len(TYPES)
    y = HEADER_H + GAP + r * (CELL_H + LABEL_H + GAP)
    d.text((6, y + CELL_H // 2), "extras", fill=FG)
    for c, (test, checkpoint, short) in enumerate(EXTRAS):
        x = ROWLABEL_W + GAP + c * (CELL_W + GAP)
        img = load_cell(test, checkpoint)
        if img is None:
            d.rectangle([x, y, x + CELL_W, y + CELL_H], fill=MISS_BG)
            d.text((x + 8, y + CELL_H // 2), "MISSING", fill=FG)
            missing.append(test)
        else:
            sheet.paste(img, (x, y))
        d.text((x + 4, y + CELL_H + 4), short + "  (" + test[:34] + ")", fill=FG)

    sheet.save(out_path)
    print(f"wrote {out_path}  ({W}x{H})")
    if missing:
        print(f"WARNING - {len(missing)} missing cell(s):")
        for m in missing:
            print(f"  - {m}")


if __name__ == "__main__":
    main()
