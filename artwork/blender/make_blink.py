"""用闭眼 GLB 的渲染结果，给现有帧合成「闭眼」版本，生成眨眼序列。

输出：
  artwork/blender/closed_frames/blink_{i}_{ms}.png   眨眼 4 帧（500×500，和 mod 一致）
  artwork/blender/eyes_glb_preview.png               预览图
"""
import os

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = r"D:\code\my_pet"
MOD = os.path.join(ROOT, r"CatPet\src\CatPet\mod\MyCat\pet\cat")
OPEN = os.path.join(MOD, r"idle\idle_000_100.png")
CLOSED = os.path.join(ROOT, r"artwork\blender\closed_glb_1000.png")
OUTDIR = os.path.join(ROOT, r"artwork\blender\closed_frames")
os.makedirs(OUTDIR, exist_ok=True)


def find_eyes(im, thresh=300):
    """在脸框里找眼睛（暗色连通域），返回 [(cx, cy, w, h)]。"""
    W, H = im.size
    rgb = np.asarray(im).astype(np.int16)[:, :, :3]
    a = np.asarray(im).astype(np.int16)[:, :, 3]
    x0, x1 = int(W * 0.34), int(W * 0.66)
    y0, y1 = int(H * 0.24), int(H * 0.42)
    lum = rgb[y0:y1, x0:x1].sum(axis=2)
    dark = (lum < thresh) & (a[y0:y1, x0:x1] > 200)

    h, w = dark.shape
    seen = np.zeros_like(dark, bool)
    comps = []
    for yy in range(h):
        for xx in range(w):
            if not dark[yy, xx] or seen[yy, xx]:
                continue
            st = [(yy, xx)]
            seen[yy, xx] = True
            pix = []
            while st:
                cy, cx = st.pop()
                pix.append((cy, cx))
                for dy in (-1, 0, 1):
                    for dx in (-1, 0, 1):
                        ny, nx = cy + dy, cx + dx
                        if 0 <= ny < h and 0 <= nx < w and dark[ny, nx] and not seen[ny, nx]:
                            seen[ny, nx] = True
                            st.append((ny, nx))
            ys = [p[0] for p in pix]
            xs = [p[1] for p in pix]
            comps.append((len(pix), x0 + min(xs), y0 + min(ys),
                          max(xs) - min(xs) + 1, max(ys) - min(ys) + 1))
    comps.sort(reverse=True)
    eyes = [(c[1] + c[3] / 2, c[2] + c[4] / 2, c[3], c[4]) for c in comps[:2]]
    eyes.sort(key=lambda e: e[0])
    return eyes


def alpha_bbox(im):
    a = np.asarray(im)[:, :, 3]
    ys, xs = np.nonzero(a > 12)
    return xs.min(), ys.min(), xs.max(), ys.max()


open_img = Image.open(OPEN).convert("RGBA")
closed_img = Image.open(CLOSED).convert("RGBA")
eyes = find_eyes(open_img)
print("眼睛:", [(round(x), round(y), w, h) for x, y, w, h in eyes], flush=True)

# 闭眼模型按“高度 + 底部中心”对齐到睁眼帧
ab = alpha_bbox(open_img)
bb = alpha_bbox(closed_img)
scale = (ab[3] - ab[1]) / (bb[3] - bb[1])
warped = closed_img.resize((int(round(closed_img.size[0] * scale)),
                            int(round(closed_img.size[1] * scale))), Image.LANCZOS)
want_cx = (ab[0] + ab[2]) / 2
have_cx = (bb[0] + bb[2]) / 2 * scale
canvas = Image.new("RGBA", open_img.size, (0, 0, 0, 0))
canvas.alpha_composite(warped, (int(round(want_cx - have_cx)), int(round(ab[3] - bb[3] * scale))))


def patch(base, strength):
    out = base.copy()
    for (cx, cy, w, h) in eyes:
        rw, rh = w * 1.18, h * 1.15
        mask = Image.new("L", base.size, 0)
        ImageDraw.Draw(mask).ellipse([cx - rw, cy - rh, cx + rw, cy + rh], fill=int(255 * strength))
        mask = mask.filter(ImageFilter.GaussianBlur(max(1.4, w * 0.13)))
        out = Image.composite(canvas, out, mask)
    return out


# 眨眼：睁 → 半闭 → 闭 → 半闭 → 睁
seq = [(0.0, 90), (0.55, 70), (1.0, 130), (0.55, 70), (0.0, 90)]
frames = []
for i, (strength, ms) in enumerate(seq):
    im = patch(open_img, strength) if strength > 0 else open_img.copy()
    path = os.path.join(OUTDIR, f"blink_{i:03d}_{ms}.png")
    im.save(path)
    frames.append(im)
print("眨眼帧数:", len(frames), flush=True)

# ---------------- 预览图 ----------------
cw, ch = 300, 300
final = Image.new("RGB", (cw * 5, ch + 240 + 40), (248, 246, 240))
d = ImageDraw.Draw(final)
try:
    font = ImageFont.truetype(r"C:\Windows\Fonts\msyh.ttc", 14)
except Exception:
    font = None

labels = ["睁眼", "半闭", "闭眼", "半闭", "睁眼"]
for i, (im, label) in enumerate(zip(frames, labels)):
    card = Image.new("RGBA", (cw - 10, ch - 10), (248, 246, 240, 255))
    card.alpha_composite(im.resize((cw - 10, ch - 10), Image.LANCZOS))
    final.paste(card.convert("RGB"), (i * cw + 5, 26))
    d.text((i * cw + 8, 6), label, fill=(20, 20, 20), font=font)
    W, H = im.size
    face = im.crop((int(W * 0.32), int(H * 0.24), int(W * 0.68), int(H * 0.50))).resize((cw - 10, 220), Image.LANCZOS)
    fc = Image.new("RGBA", face.size, (248, 246, 240, 255))
    fc.alpha_composite(face)
    final.paste(fc.convert("RGB"), (i * cw + 5, ch + 20))

final.save(os.path.join(ROOT, r"docs\img\blink-preview.png"), quality=95)
print("写出 preview", flush=True)
