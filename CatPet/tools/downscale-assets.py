"""把 500×500 的素材统一降到 330×330（= 默认窗口 275px 的 1.2 倍）。

背景：默认缩放 0.55 时窗口只有 275px，500px 的素材是 1.82 倍过采样，纯浪费；
降到 330 仍是 1.2 倍过采样，肉眼几乎无差，体积按面积降到约 43%。

用法：
  python downscale-assets.py [--dry-run]
会跳过已经是 330 的文件（幂等），以及 250 的运镜素材。
"""
import glob
import os
import sys

from PIL import Image

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
MOD = os.path.join(REPO, "CatPet", "src", "CatPet", "mod", "MyCat", "pet", "cat")
TARGET = 330

dry = "--dry-run" in sys.argv

files = sorted(glob.glob(os.path.join(MOD, "**", "*.png"), recursive=True))
done = 0
skipped = 0
before = 0
after = 0

for path in files:
    with Image.open(path) as im:
        w, h = im.size
        if w <= TARGET and h <= TARGET:
            skipped += 1          # 已经够小（含 250 的运镜帧），不放大
            continue
        if w != h:
            skipped += 1
            continue
        before += os.path.getsize(path)
        if dry:
            after += int(os.path.getsize(path) * (TARGET / w) ** 2)
            done += 1
            continue

        small = im.convert("RGBA").resize((TARGET, TARGET), Image.LANCZOS)
        small.save(path)
        after += os.path.getsize(path)
        done += 1

print(f"{'[dry] ' if dry else ''}缩放 {done} 个文件，跳过 {skipped} 个")
print(f"体积：{before / 1024 / 1024:.1f} MB → {after / 1024 / 1024:.1f} MB")
