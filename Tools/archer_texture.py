"""Builds the archer's colour map from the Solus knight texture (run with plain Python + Pillow/numpy).
Plates → dark oiled leather, filigree → tarnished bronze, navy cloth → forest green; the empty
256 px block at (1760, 688) becomes four swatches used by the hood/cape, leather kit, bow wood
and fletching/arrowheads (see archer_anim.py SWATCH)."""
import sys
import numpy as np
from PIL import Image

src, dst = sys.argv[1], sys.argv[2]
im = np.asarray(Image.open(src).convert('RGB')).astype(np.float32) / 255.0
r, g, b = im[..., 0], im[..., 1], im[..., 2]
L = 0.3 * r + 0.59 * g + 0.11 * b
empty = im.sum(2) < 0.04
cloth = (b > r + 0.025) & (L < 0.38)


def ramp(x, stops):
    x = np.clip(x, 0, 1)
    out = np.zeros(x.shape + (3,), np.float32)
    for (x0, c0), (x1, c1) in zip(stops[:-1], stops[1:]):
        m = (x >= x0) & (x <= x1)
        t = ((x - x0) / max(1e-6, x1 - x0))[m][:, None]
        out[m] = np.array(c0) * (1 - t) + np.array(c1) * t
    return out


leather = ramp(L, [(0.0, (0.05, 0.035, 0.025)), (0.35, (0.17, 0.105, 0.06)), (0.7, (0.33, 0.21, 0.12)),
                   (0.86, (0.38, 0.26, 0.15)), (1.0, (0.50, 0.39, 0.24))])
green = ramp(L / 0.38, [(0.0, (0.03, 0.045, 0.03)), (0.5, (0.08, 0.13, 0.075)), (1.0, (0.16, 0.24, 0.14))])
out = np.where(cloth[..., None], green, leather)
out = np.where(empty[..., None], im, out)

# ---- swatches (128 px each, 2x2 inside the empty block)
rng = np.random.default_rng(7)
def noise(h, w, scale):
    n = rng.random((h // scale + 2, w // scale + 2)).astype(np.float32)
    n = np.asarray(Image.fromarray((n * 255).astype(np.uint8)).resize((w + scale * 2, h + scale * 2), Image.BICUBIC)).astype(np.float32) / 255
    return n[:h, :w]

X0, Y0 = 1760, 688
def put(ix, iy, arr):
    out[Y0 + iy * 128: Y0 + iy * 128 + 128, X0 + ix * 128: X0 + ix * 128 + 128] = arr

yy, xx = np.mgrid[0:128, 0:128].astype(np.float32)
# 0: wool cloth (weave + mottling), dark forest green
weave = 0.5 + 0.5 * np.sin(xx * 1.6) * np.sin(yy * 1.6)
m = 0.6 * noise(128, 128, 16) + 0.25 * noise(128, 128, 4) + 0.15 * weave
put(0, 0, np.stack([0.06 + 0.07 * m, 0.10 + 0.10 * m, 0.06 + 0.06 * m], -1))
# 1: worn leather (brown, scuffs)
m = 0.7 * noise(128, 128, 12) + 0.3 * noise(128, 128, 3)
put(1, 0, np.stack([0.20 + 0.16 * m, 0.12 + 0.10 * m, 0.07 + 0.05 * m], -1))
# 2: yew wood (grain along v)
grain = 0.5 + 0.5 * np.sin(xx * 0.55 + 3.0 * noise(128, 128, 32) * 6.0)
m = 0.55 * grain + 0.45 * noise(128, 128, 6)
put(0, 1, np.stack([0.22 + 0.20 * m, 0.13 + 0.12 * m, 0.07 + 0.06 * m], -1))
# 3: top half feathers (bone white / ash grey), bottom half blackened iron
f = 0.75 + 0.25 * noise(128, 128, 5)
iron = 0.10 + 0.08 * noise(128, 128, 8)
sw3 = np.where((yy < 64)[..., None], np.stack([0.80 * f, 0.77 * f, 0.70 * f], -1), np.stack([iron, iron * 1.02, iron * 1.05], -1))
put(1, 1, sw3)

Image.fromarray((np.clip(out, 0, 1) * 255).astype(np.uint8)).save(dst)
print("ok", dst)
