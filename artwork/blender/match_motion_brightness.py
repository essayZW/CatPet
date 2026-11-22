"""把站姿移动类素材的亮度/对比度对齐到站立待机，消掉"一走起来就变暗 / 变灰"。

背景：同一模型不同机位的光照不同——3/4 待机（az=±25）平均亮度 ~171、对比度 ~35，
正侧（az=90/270）只有 155（左）/ 142（右），对比度也低（右侧仅 25.7）。

做法（每个目录一组常数参数，整段共用，不会逐帧闪）：

    1) gamma 对齐平均亮度：   v = 255 * (x / 255) ** g      （0/255 不动，高光不糊）
    2) 线性对齐对比度：       v = m + (v - m) * s           （m 为目标均值，s 限制在 0.75~1.35）

只改颜色，不动 alpha / 几何 / 脚底。

用法：
    python match_motion_brightness.py [--dry-run]
幂等：均值与对比度都已在容差内的目录跳过。
"""
import glob
import os
import sys

import numpy as np
from PIL import Image

MOD = os.path.join(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))), "CatPet", "src", "CatPet", "mod", "MyCat", "pet", "cat", "stand")

# 待改的移动类目录 → 亮度/对比度基准（同侧的站立待机）
TARGETS = {
    "swalk_l": "sidle", "swalk_r": "sidle_r",
    "srun_l": "sidle", "srun_r": "sidle_r",
    "sgal_l": "sidle", "sgal_r": "sidle_r",
    "sjump": "sidle", "sjump_r": "sidle_r",
    "sjump_diag": "sidle", "sjump_diag_r": "sidle_r",
}

dry = "--dry-run" in sys.argv


def files(name):
    return sorted(glob.glob(os.path.join(MOD, name, "*.png")))


def solid_luma(path):
    a = np.asarray(Image.open(path).convert("RGBA")).astype(np.float32)
    m = a[:, :, 3] >= 128
    return a[:, :, :3][m].mean(axis=1)


def stats(name, sample=6):
    fs = files(name)
    if not fs:
        return None
    step = max(1, len(fs) // sample)
    v = np.concatenate([solid_luma(f) for f in fs[::step][:sample]])
    return float(v.mean()), float(v.std())


def solve_gamma(v, target_mean, lo=0.5, hi=2.0):
    a, b = lo, hi
    for _ in range(30):
        g = (a + b) / 2
        m = float(np.mean(255.0 * np.power(v / 255.0, g)))
        if m < target_mean:
            b = g            # 太暗 → 需要更亮 → 更小的 g
        else:
            a = g
    return (a + b) / 2


def solve_scale(v, g, target_mean, target_std):
    after = 255.0 * np.power(v / 255.0, g)
    s = target_std / max(1e-3, float(after.std()))
    return max(0.75, min(1.35, s))


def apply_params(path, g, s, m):
    a = np.asarray(Image.open(path).convert("RGBA")).astype(np.float32)
    v = 255.0 * np.power(a[:, :, :3] / 255.0, g)
    v = m + (v - m) * s
    a[:, :, :3] = np.clip(v, 0, 255)
    Image.fromarray(a.astype(np.uint8), "RGBA").save(path)


ref = {}
for name in sorted(set(TARGETS.values())):
    st = stats(name, sample=1)
    ref[name] = st
    print(f"基准 {name}: 均值 {st[0]:.1f} 对比度 {st[1]:.1f}")

for dir_name, ref_name in TARGETS.items():
    cur = stats(dir_name)
    if cur is None:
        print(f"{dir_name:16s} 缺失，跳过")
        continue
    tm, ts = ref[ref_name]
    if abs(cur[0] - tm) <= 1.5 and abs(cur[1] - ts) <= 1.5:
        print(f"{dir_name:16s} 均值 {cur[0]:.1f} 对比度 {cur[1]:.1f} 已达标，跳过")
        continue

    fs = files(dir_name)
    sample = fs[:: max(1, len(fs) // 6)][:6]
    v = np.concatenate([solid_luma(f) for f in sample])
    g = solve_gamma(v, tm)
    s = solve_scale(v, g, tm, ts)

    print(f"{dir_name:16s} 均值 {cur[0]:5.1f}→{tm:5.1f} 对比度 {cur[1]:5.1f}→{ts:5.1f}  gamma={g:.3f} scale={s:.3f}  ({len(fs)} 帧)")
    if dry:
        continue
    for f in fs:
        apply_params(f, g, s, tm)

if not dry:
    print("\n复查：")
    for dir_name, ref_name in TARGETS.items():
        cur = stats(dir_name)
        if cur is not None:
            tm, ts = ref[ref_name]
            print(f"  {dir_name:16s} 均值 {cur[0]:6.1f}(目标 {tm:.1f})  对比度 {cur[1]:5.1f}(目标 {ts:.1f})")
