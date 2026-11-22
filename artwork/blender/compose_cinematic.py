"""把行走素材 + 落定过渡帧合成成「由远及近 / 走远」的运镜帧。

要点：
- 缩放和横向偏移烘进帧，窗口全程不动（否则气泡文字会跟着抖）
- 输出 250×250（原素材一半）
- 首尾各带一段"落定"过渡，避免从"迈步中"硬切到站立待机
- **对齐待机**：脚底线、整体大小、亮度都按 sidle / sidle_r 归一化，
  否则切换待机时会看到"跳一下 / 突然变亮 / 突然变大"

出现 cin_intro：30 帧行走（0.45→1.0，横向 170→0，45ms）+ 14 帧落定（45ms）≈ 1.98s
离开 cin_outro：14 帧落定 + 30 帧行走（1.0→0.40，横向 0→200，45ms）≈ 1.98s
"""
import glob
import os

import numpy as np
from PIL import Image

MOD = r"D:\code\my_pet\CatPet\src\CatPet\mod\MyCat\pet\cat\stand"
BAKE = r"D:\code\my_pet\artwork\blender\frames_stand3"
OUT_SCALE = 0.5
CANVAS = int(500 * OUT_SCALE)


def load_dir(path):
    return [Image.open(p).convert("RGBA") for p in sorted(glob.glob(os.path.join(path, "*.png")))]


def pick(frames, n):
    """从长序列里均匀取 n 帧（含首尾），保持插值范围不变。"""
    if len(frames) <= n:
        return frames
    return [frames[round(i * (len(frames) - 1) / (n - 1))] for i in range(n)]


def load_prefix(prefix):
    files = sorted(p for p in glob.glob(os.path.join(BAKE, prefix + "_*.png"))
                   if os.path.basename(p)[len(prefix) + 1:len(prefix) + 4].isdigit())
    return [Image.open(p).convert("RGBA") for p in files]


# ---------------------------------------------------------------- 量待机基准
def measure(img):
    """实体像素（alpha≥128）的高度、脚底相对位置、平均亮度。"""
    a = np.asarray(img.convert("RGBA")).astype(np.float32)
    solid = a[:, :, 3] >= 128
    ys, _ = np.nonzero(solid)
    if not len(ys):
        return None
    h = a.shape[0]
    return {
        "h": h,
        "height": (ys.max() - ys.min() + 1) / h,
        "foot": ys.max() / h,
        "bright": float(a[:, :, :3][solid].mean()),
    }


def idle_ref(name):
    files = sorted(glob.glob(os.path.join(MOD, name, "*.png")))
    return measure(Image.open(files[0]))


REF_R = idle_ref("sidle")      # 朝左的站立待机
REF_L = idle_ref("sidle_r")    # 朝右的站立待机
print(f"待机基准：sidle 高{REF_R['height']:.4f} 脚{REF_R['foot']:.4f} 亮度{REF_R['bright']:.1f}")
print(f"          sidle_r 高{REF_L['height']:.4f} 脚{REF_L['foot']:.4f} 亮度{REF_L['bright']:.1f}")


def brightness_gain(imgs, target):
    vals = []
    for im in imgs:
        m = measure(im)
        if m:
            vals.append(m["bright"])
    if not vals:
        return 1.0
    cur = float(np.mean(vals))
    return max(0.8, min(1.45, target / max(1.0, cur)))


def apply_gain(im, gain):
    if abs(gain - 1.0) < 0.005:
        return im
    a = np.asarray(im.convert("RGBA")).astype(np.float32)
    a[:, :, :3] = np.clip(a[:, :, :3] * gain, 0, 255)
    return Image.fromarray(a.astype(np.uint8), "RGBA")


def h_px(img, scale):
    """把 img 按 scale*OUT_SCALE 缩放后，量实体像素高度（px）。"""
    s = scale * OUT_SCALE
    small = img.resize((max(1, int(img.width * s)), max(1, int(img.height * s))), Image.LANCZOS)
    m = measure(small)
    return (m["height"] * small.height) if m else 0.0


def seg_fix(items, target_px, which):
    """把 items 里指定段（"walk" / "settle"）整体缩放到目标像素高度。

    行走和落定是两套烘焙，取景差 ~11%（同一姿势 0.728 vs 0.816），
    不各自归一的话，落定→行走那一下会"突然变小"。
    """
    idx = [i for i, it in enumerate(items) if it[3] == which]
    if not idx:
        return items, 1.0

    # 参考帧：落定取"待机姿势"那端；行走取最高的一帧（腿并拢 ≈ 站立高度）
    if which == "settle":
        ref = idx[0] if items[idx[0]][4] == "settle_first" else idx[-1]
        ref = idx[-1] if items[idx[-1]][4] != "settle_first" else idx[0]
        meas = h_px(items[ref][0], items[ref][1])
    else:
        meas = max(h_px(items[i][0], items[i][1]) for i in idx)
    if meas <= 0:
        return items, 1.0

    k = max(0.85, min(1.2, target_px / meas))
    out = []
    for i, (im, sc, dx, ms, tag) in enumerate(items):
        out.append((im, sc * k if i in idx else sc, dx, ms, tag))
    return out, k


def render(items, out_dir, foot_rel, gain):
    """items: [(Image, scale, dx, ms, tag), ...]"""
    os.makedirs(out_dir, exist_ok=True)
    for f in glob.glob(os.path.join(out_dir, "*.png")):
        os.remove(f)

    target_foot = foot_rel * CANVAS

    for i, (src, scale, dx, ms, _tag) in enumerate(items):
        s = scale * OUT_SCALE
        w = max(1, int(src.width * s))
        h = max(1, int(src.height * s))
        small = src.resize((w, h), Image.LANCZOS)
        if abs(gain - 1.0) >= 0.005:
            small = apply_gain(small, gain)

        m = measure(small)
        foot = m["foot"] * small.height if m else small.height

        canvas = Image.new("RGBA", (CANVAS, CANVAS), (0, 0, 0, 0))
        # 用 paste（带蒙版）：alpha_composite 不接受负偏移，而放大的帧顶部会在画布外
        canvas.paste(small, (int(CANVAS / 2 - w / 2 + dx * OUT_SCALE), int(round(target_foot - foot))), small)
        canvas.save(os.path.join(out_dir, f"{os.path.basename(out_dir)}_{i:03d}_{ms}.png"))

    total = sum(it[3] for it in items)
    print(f"{os.path.basename(out_dir)}: {len(items)} frames = {total}ms, gain {gain:.3f}")


# ---- 右侧（猫靠右边界时用）：从右边进来 / 往右边走掉
walk_in_R = load_prefix("swalk45_l")        # 正面偏左，朝左走（500px，来自烘焙目录）
walk_out_R = load_prefix("swalk45back_r")     # 背面偏右，朝右走
settle_in_R = pick(load_prefix("sstand_in"), 14)
settle_out_R = pick(load_prefix("sstand_out"), 14)

# ---- 左侧（猫靠左边界时用）：从左边进来 / 往左边走掉
walk_in_L = load_prefix("swalk45_r")        # 正面偏右，朝右走
walk_out_L = load_prefix("swalk45back_l")     # 背面偏左，朝左走
settle_in_L = pick(load_prefix("sstand_in_r"), 14)
settle_out_L = pick(load_prefix("sstand_out_r"), 14)


def build_intro(walk, settle, dx0):
    items = []
    for i, src in enumerate(walk):
        t = i / (len(walk) - 1)                    # 线性逼近：末尾不减速，免得看着"停一下"
        items.append((src, 0.45 + 0.55 * t, dx0 * (1 - t), 45, "walk"))
    for src in settle:
        items.append((src, 1.0, 0.0, 45, "settle"))
    return items


def build_outro(settle, walk, dx1):
    items = []
    for src in settle:
        items.append((src, 1.0, 0.0, 45, "settle"))
    for i, src in enumerate(walk):
        t = i / (len(walk) - 1)                    # 线性离开
        items.append((src, 1.0 - 0.60 * t, dx1 * t, 45, "walk"))
    return items


# 目标像素高度：待机 289px/330 → 250 画布上 ≈ 219px（四舍五入，避免 1px 误差）
TARGET_PX = round(REF_R["height"] * CANVAS)


for name, build, walk, settle, dx, side in (
    ("cin_intro", build_intro, walk_in_R, settle_in_R, 170, "R"),
    ("cin_outro", build_outro, walk_out_R, settle_out_R, 200, "R"),
    ("cin_intro_l", build_intro, walk_in_L, settle_in_L, -170, "L"),
    ("cin_outro_l", build_outro, walk_out_L, settle_out_L, -200, "L"),
):
    items = build(walk, settle, dx) if build is build_intro else build(settle, walk, dx)
    ref = REF_L if side == "L" else REF_R

    walk_i = [i for i, it in enumerate(items) if it[4] == "walk"]
    settle_i = [i for i, it in enumerate(items) if it[4] == "settle"]

    # 1) 落定段：把"待机姿势"那端对齐到待机像素高度
    #    出现 → 落定末帧是待机；离开 → 落定首帧是待机
    settle_end = settle_i[-1] if build is build_intro else settle_i[0]
    h_end = max(1.0, h_px(items[settle_end][0], items[settle_end][1]))
    k_settle = max(0.85, min(1.25, TARGET_PX / h_end))

    # 2) 行走段：把"与落定相接的那帧"对到落定同侧的交界帧
    #    （两套烘焙取景差 ~3~11%，直接对齐待机会在交界处跳一下）
    settle_join = settle_i[0] if build is build_intro else settle_i[-1]
    walk_join = walk_i[-1] if build is build_intro else walk_i[0]
    h_join_settle = h_px(items[settle_join][0], items[settle_join][1]) * k_settle
    h_join_walk = max(1.0, h_px(items[walk_join][0], items[walk_join][1]))
    k_walk = max(0.85, min(1.25, h_join_settle / h_join_walk))

    items = [(im,
              sc * (k_walk if tag == "walk" else k_settle),
              dx2, ms, tag)
             for im, sc, dx2, ms, tag in items]

    gain = brightness_gain([im for im, _, _, _, _ in items], ref["bright"])
    render(items, os.path.join(MOD, name), ref["foot"], gain)
    print(f"   settle x{k_settle:.3f}  walk x{k_walk:.3f}  target {TARGET_PX}px")

