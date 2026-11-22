"""给跳跃烘焙用的关键帧 → 加密采样工具（两个 bake 脚本共用写法）。"""

NUM_FIELDS = ("fu", "fl", "fp", "hu", "hl", "hp", "neck", "head", "tail", "pitch", "lift")


def _lerp(a, b, t):
    return a + (b - a) * t


def sample_frames(keys, step_ms):
    """把关键帧按约 step_ms 的间隔均匀采样，返回每帧姿势（含 "ms" 时长）。

    keys 里每项带 "ms"（这一关键帧持续多久）。总时长保持不变，
    帧数 = round(总时长 / step_ms)，每帧时长近似相等。
    """
    cum = []
    acc = 0
    for k in keys:
        acc += k["ms"]
        cum.append(acc)
    total = cum[-1]

    n = max(1, int(round(total / step_ms)))
    base = total / n

    frames = []
    for i in range(n):
        t = min(total - 1e-6, i * base)

        idx = len(cum) - 1
        for j, c in enumerate(cum):
            if t < c:
                idx = j
                break

        prev_c = cum[idx - 1] if idx > 0 else 0.0
        a = keys[idx - 1] if idx > 0 else keys[idx]
        b = keys[idx]
        span = max(1e-6, cum[idx] - prev_c)
        u = min(1.0, max(0.0, (t - prev_c) / span))

        p = {name: _lerp(a.get(name, 0.0), b.get(name, 0.0), u) for name in NUM_FIELDS}
        p["contact"] = a.get("contact", False) if u < 0.5 else b.get("contact", False)
        p["ms"] = int(round(base))
        frames.append(p)

    # 让总时长精确对上
    frames[-1]["ms"] += int(round(total - sum(f["ms"] for f in frames)))
    return frames
