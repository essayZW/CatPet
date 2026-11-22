"""把 stand/sfollow_r 的猫放大到和 stand/sfollow 一样大。

右向的头部跟随图集是单独一批烘的，取景比左向小约 4.4%
（0.8364 vs 0.8727），所以「扭头看人 → 退场动画」切换时会突然长个儿。
这里按脚底中心为锚点等比放大，贴回同样的 330 画布。

幂等：已经对齐过（比例接近）就跳过。
"""
import glob
import os

import numpy as np
from PIL import Image

MOD = r"D:\code\my_pet\CatPet\src\CatPet\mod\MyCat\pet\cat\stand"
REF = os.path.join(MOD, "sfollow")
TARGET_DIR = os.path.join(MOD, "sfollow_r")
CANVAS = 330


def solid_bbox(img):
    a = np.asarray(img.convert("RGBA"))
    mask = a[:, :, 3] >= 128
    ys, xs = np.nonzero(mask)
    if not len(ys):
        return None
    return xs.min(), ys.min(), xs.max(), ys.max()


def height_ratio(path):
    img = Image.open(path).convert("RGBA")
    b = solid_bbox(img)
    return (b[3] - b[1] + 1) / img.height if b else 0.0


ref_files = sorted(glob.glob(os.path.join(REF, "*.png")))
tgt_files = sorted(glob.glob(os.path.join(TARGET_DIR, "*.png")))
if not ref_files or not tgt_files:
    raise SystemExit("找不到 sfollow / sfollow_r 素材")

ref_ratio = float(np.mean([height_ratio(f) for f in ref_files[:20]]))
tgt_ratio = float(np.mean([height_ratio(f) for f in tgt_files[:20]]))
k = ref_ratio / tgt_ratio
print(f"sfollow {ref_ratio:.4f}  sfollow_r {tgt_ratio:.4f}  → 缩放 x{k:.4f}")

if abs(k - 1.0) < 0.005:
    print("已经对齐，跳过")
    raise SystemExit(0)

for f in tgt_files:
    img = Image.open(f).convert("RGBA")
    b = solid_bbox(img)
    if not b:
        continue
    foot_x = (b[0] + b[2]) / 2.0          # 以脚底中心为锚点，头的摆动不会被拉伸放大
    foot_y = b[3]

    w, h = int(round(img.width * k)), int(round(img.height * k))
    big = img.resize((w, h), Image.LANCZOS)

    canvas = Image.new("RGBA", (CANVAS, CANVAS), (0, 0, 0, 0))
    dx = int(round(foot_x * k - foot_x))
    dy = int(round(foot_y * k - foot_y))
    canvas.paste(big, (-dx, -dy), big)
    canvas.save(f)

print(f"已处理 {len(tgt_files)} 帧")
