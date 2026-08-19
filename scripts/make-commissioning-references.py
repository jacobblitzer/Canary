"""Regenerate the synthetic commissioning reference images.

The three comparer images encode answers that are known EXACTLY, which is what lets layer 1
assert numbers rather than merely "it ran": 64x64 = 4096 px, a 16x16 patch = 256 changed, and
a 2-per-channel nudge that must read as 0 changed at the default colour threshold of 3.

Deliberately synthetic. A photograph or a screenshot would make the expected counts depend on
an encoder version, and layer 1 has to be the one thing that still works when nothing else on
the machine does.

    python scripts/make-commissioning-references.py
"""
import pathlib

from PIL import Image

OUT = pathlib.Path(__file__).resolve().parent.parent / "workloads" / "commissioning" / "references"
OUT.mkdir(parents=True, exist_ok=True)
W = H = 64

a = Image.new("RGBA", (W, H), (18, 20, 24, 255))

b = a.copy()
for y in range(16, 32):
    for x in range(16, 32):
        b.putpixel((x, y), (240, 240, 240, 255))

# +2 on every channel: strictly below the comparer's default threshold of 3, so a correct
# comparer reports ZERO differences here. This is the control that catches a comparer which
# ignores its threshold - one that would flag every anti-aliasing difference as a regression.
nudged = Image.new("RGBA", (W, H), (20, 22, 26, 255))

a.save(OUT / "comparer-a.png")
b.save(OUT / "comparer-b.png")
nudged.save(OUT / "comparer-a-nudged.png")

print("wrote 3 images to " + str(OUT))
print("  A vs B        -> {} changed of {} ({:.4f}%)".format(16 * 16, W * H, 16 * 16 / (W * H) * 100))
print("  A vs A-nudged -> 0 changed (delta 2 <= threshold 3)")
print("  NOTE: rhino-reference.png is NOT regenerated here. It is a real capture from a real")
print("        machine, and has to stay one or layer 3 becomes tautological.")
