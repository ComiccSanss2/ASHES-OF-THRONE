"""Builds the reaper's colour map (+ emission map) from the Solus knight texture (plain Python + Pillow/numpy).
Plates → blackened steel (cold, a violet sheen), filigree → tarnished silver, navy cloth → black-violet;
the empty 256 px block at (1760, 688) holds the swatches used by reaper_anim.py (same layout as the
bard's SWATCH): black wool, dark leather, (unused), silver, hood shadow, eye-light, bone mask.
    python reaper_texture.py <knight_base_color.png> <out_color.png> <out_emission.png>"""
import sys
import numpy as np
from PIL import Image

src, dst, dst_em = sys.argv[1], sys.argv[2], sys.argv[3]
im = np.asarray(Image.open(src).convert('RGB')).astype(np.float32) / 255.0
r, g, b = im[..., 0], im[..., 1], im[..., 2]
L = 0.3 * r + 0.59 * g + 0.11 * b
empty = im.sum(2) < 0.04
cloth = (b > r + 0.025) & (L < 0.38)
warm = (r > b + 0.16) & (L > 0.55)


def ramp(x, stops):
    x = np.clip(x, 0, 1)
    out = np.zeros(x.shape + (3,), np.float32)
    for (x0, c0), (x1, c1) in zip(stops[:-1], stops[1:]):
        m = (x >= x0) & (x <= x1)
        t = ((x - x0) / max(1e-6, x1 - x0))[m][:, None]
        out[m] = np.array(c0) * (1 - t) + np.array(c1) * t
    return out


leather = ramp(L, [(0.0, (0.02, 0.02, 0.028)), (0.35, (0.07, 0.07, 0.09)), (0.7, (0.17, 0.17, 0.21)), (1.0, (0.34, 0.33, 0.40))])
brass = ramp(L, [(0.0, (0.06, 0.06, 0.07)), (0.6, (0.40, 0.40, 0.44)), (1.0, (0.72, 0.72, 0.76))])
brown = ramp(L / 0.38, [(0.0, (0.02, 0.012, 0.03)), (0.5, (0.07, 0.04, 0.10)), (1.0, (0.14, 0.08, 0.19))])
out = np.where(cloth[..., None], brown, np.where(warm[..., None], brass, leather))
out = np.where(empty[..., None], im, out)
em = np.zeros_like(out)

rng = np.random.default_rng(11)
def noise(h, w, scale):
    n = rng.random((h // scale + 2, w // scale + 2)).astype(np.float32)
    n = np.asarray(Image.fromarray((n * 255).astype(np.uint8)).resize((w + scale * 2, h + scale * 2), Image.BICUBIC)).astype(np.float32) / 255
    return n[:h, :w]

X0, Y0 = 1760, 688
def put(x0, y0, arr, target=None):
    t = out if target is None else target
    h, w = arr.shape[:2]
    t[y0:y0 + h, x0:x0 + w] = arr

yy, xx = np.mgrid[0:128, 0:128].astype(np.float32)
# black-violet ragged wool
weave = 0.5 + 0.5 * np.sin(xx * 1.7) * np.sin(yy * 1.7)
m = 0.55 * noise(128, 128, 16) + 0.25 * noise(128, 128, 4) + 0.2 * weave
put(X0, Y0, np.stack([0.045 + 0.06 * m, 0.03 + 0.035 * m, 0.07 + 0.08 * m], -1))
# dark leather
m = 0.7 * noise(128, 128, 12) + 0.3 * noise(128, 128, 3)
put(X0 + 128, Y0, np.stack([0.06 + 0.06 * m, 0.05 + 0.05 * m, 0.06 + 0.06 * m], -1))
# staff wood: pale ash with dark grain
grain = 0.5 + 0.5 * np.sin(xx * 0.5 + 3.0 * noise(128, 128, 32) * 6.0)
m = 0.55 * grain + 0.45 * noise(128, 128, 6)
put(X0, Y0 + 128, np.stack([0.30 + 0.20 * m, 0.21 + 0.14 * m, 0.12 + 0.08 * m], -1))
# quadrant (1,1): gold | shadow / crystal | beard (64 px each)
y4, x4 = np.mgrid[0:64, 0:64].astype(np.float32)
n = noise(64, 64, 6)
put(X0 + 128, Y0 + 128, np.stack([0.70 + 0.18 * n, 0.70 + 0.18 * n, 0.76 + 0.18 * n], -1))           # silver
put(X0 + 192, Y0 + 128, np.full((64, 64, 3), 0.012, np.float32))                                        # shadow
cr = 0.6 + 0.4 * noise(64, 64, 8)
crystal = np.stack([0.85 * cr, 0.45 * cr, 1.0 * cr], -1)
put(X0 + 128, Y0 + 192, crystal)                                                                         # crystal
bone = 0.78 + 0.16 * noise(64, 64, 8) - 0.18 * (noise(64, 64, 3) > 0.82)
put(X0 + 192, Y0 + 192, np.stack([0.86 * bone, 0.82 * bone, 0.72 * bone], -1))                           # bone mask
put(X0 + 128, Y0 + 192, crystal * 1.0, em)                                                               # glow only on the eyes

Image.fromarray((np.clip(out, 0, 1) * 255).astype(np.uint8)).save(dst)
Image.fromarray((np.clip(em, 0, 1) * 255).astype(np.uint8)).save(dst_em)
print("ok", dst, dst_em)
