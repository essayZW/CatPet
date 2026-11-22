"""站立猫：飞奔（gallop）——4 个关键姿势 + 循环 Catmull-Rom 插值。

相比第一版做了两件事来消除"颤抖"：
1. 关键姿势之间用 Catmull-Rom（速度连续），而不是每段 ease-in/out（会在每个关键帧打顿）
2. 落地高度先逐帧算出，再沿循环做 3 抽头平滑，避免"哪只脚最低"切换时的高度跳变
"""

import math
import os

import bpy
from mathutils import Vector

OUT = os.path.dirname(os.path.abspath(__file__))
FRAMES = os.path.join(OUT, "frames_stand3")
PREFIX = os.environ.get("RUN_PREFIX", "sgal_l")
MS = int(os.environ.get("RUN_MS", "36"))
N = int(os.environ.get("RUN_N", "12"))

bpy.ops.wm.open_mainfile(filepath=os.path.join(OUT, "plan_a_rig.blend"))
arm = bpy.data.objects["cat_rig"]
obj = [o for o in bpy.context.scene.objects if o.type == 'MESH' and o.name != "orig_target"][0]
sc = bpy.context.scene
sc.render.engine = 'BLENDER_EEVEE'
sc.render.resolution_x = 500
sc.render.resolution_y = 500
sc.render.film_transparent = True
sc.render.image_settings.file_format = 'PNG'
sc.render.image_settings.color_mode = 'RGBA'
try:
    sc.eevee.taa_render_samples = 24
except Exception:
    pass
try:
    sc.view_settings.view_transform = 'Standard'
except Exception:
    pass

if sc.world is None:
    w = bpy.data.worlds.new("W")
    sc.world = w
    w.use_nodes = True
    bg = w.node_tree.nodes.get("Background")
    if bg:
        bg.inputs[0].default_value = (0.55, 0.58, 0.62, 1)
        bg.inputs[1].default_value = 0.6
if not [o for o in sc.objects if o.type == 'LIGHT']:
    for nm, rot, e in [("key", (math.radians(52), 0, math.radians(30)), 3.2),
                       ("fill", (math.radians(70), 0, math.radians(225)), 1.0)]:
        l = bpy.data.lights.new(nm, 'SUN'); l.energy = e
        lo = bpy.data.objects.new(nm, l); lo.rotation_euler = rot
        sc.collection.objects.link(lo)

bpy.context.view_layer.update()
dg = bpy.context.evaluated_depsgraph_get()
ev = obj.evaluated_get(dg)
me = ev.to_mesh(); mwe = ev.matrix_world
mn = Vector((1e9,) * 3); mx = Vector((-1e9,) * 3)
for v in me.vertices:
    wv = mwe @ v.co
    for i in range(3):
        mn[i] = min(mn[i], wv[i]); mx[i] = max(mx[i], wv[i])
ev.to_mesh_clear()
h = mx.z - mn.z
L = h / 0.82

for o in [o for o in sc.objects if o.type == 'CAMERA']:
    bpy.data.objects.remove(o, do_unlink=True)
cd = bpy.data.cameras.new("cam_gal"); cd.type = 'ORTHO'; cd.ortho_scale = L
cam = bpy.data.objects.new("cam_gal", cd); sc.collection.objects.link(cam); sc.camera = cam
center = Vector(((mn.x + mx.x) * 0.5, (mn.y + mx.y) * 0.5, mn.z + 0.444 * L))
# 前缀 _l → 向左（相机在 +X）；_r → 向右（相机在 -X）
AZ = 270.0 if PREFIX.endswith("_r") else 90.0
az, el = math.radians(AZ), math.radians(6.0)
d = Vector((math.sin(az) * math.cos(el), -math.cos(az) * math.cos(el), math.sin(el)))
cam.location = center + d * 6
cam.rotation_euler = (center - cam.location).to_track_quat('-Z', 'Y').to_euler()

P = {b.name: b for b in arm.pose.bones}
for pb in arm.pose.bones:
    pb.rotation_mode = 'XYZ'
R = math.radians

FOOT_IDX = None


def build_foot_indices():
    global FOOT_IDX
    bpy.context.view_layer.update()
    dg3 = bpy.context.evaluated_depsgraph_get()
    ev3 = obj.evaluated_get(dg3)
    me3 = ev3.to_mesh()
    mw3 = ev3.matrix_world
    FOOT_IDX = {v.index for v in me3.vertices if (mw3 @ v.co).z < 0.16}
    ev3.to_mesh_clear()
    print("foot vertices", len(FOOT_IDX), flush=True)


def lowest_foot_z():
    bpy.context.view_layer.update()
    dg2 = bpy.context.evaluated_depsgraph_get()
    ev2 = obj.evaluated_get(dg2)
    me2 = ev2.to_mesh()
    mw2 = ev2.matrix_world
    z = min((mw2 @ v.co).z for v in me2.vertices if (not FOOT_IDX) or v.index in FOOT_IDX)
    ev2.to_mesh_clear()
    return z


def clear():
    for pb in arm.pose.bones:
        pb.rotation_euler = (0, 0, 0)
        pb.location = (0, 0, 0)
        pb.scale = (1, 1, 1)
    arm.location = (0, 0, 0)


# 4 个关键姿势（循环）。方向已用 probe_leg_direction.py 校准：
#   前腿 upper 负 = 向前伸；前腿 lower 正 = 收拢
#   后腿 upper 正 = 向后蹬；后腿 lower 负 = 伸直
KEYS = [
    # K0 蜷缩（躯干尽量保持直，避免中段下凹）
    dict(fu=10, fl=42, fp=-10, hu=-14, hl=-42, hp=10,
         hips=0, s1=-2, s2=-2, chest=-1, neck=6, head=8, lift=0.012),
    # K1 蹬地
    dict(fu=8, fl=38, fp=-8, hu=38, hl=-26, hp=6,
         hips=0, s1=-2, s2=-2, chest=-1, neck=8, head=10, lift=0.032),
    # K2 腾空伸展（前腿角度收小；躯干只留极小的前倾）
    dict(fu=-42, fl=-10, fp=6, hu=32, hl=-26, hp=8,
         hips=0, s1=-1.5, s2=-1.5, chest=-0.5, neck=8, head=10, lift=0.090),
    # K3 落地
    dict(fu=-32, fl=-20, fp=10, hu=-6, hl=-34, hp=12,
         hips=0, s1=-2, s2=-2, chest=-1, neck=7, head=9, lift=0.014),
]
KEYS_N = len(KEYS)

# 试调：给躯干加一个整体弧度，抵消四肢拉扯造成的"中段凹"
SPINE = float(os.environ.get("RUN_SPINE", "0"))
if SPINE:
    for _k in KEYS:
        _k["s1"] += SPINE
        _k["s2"] += SPINE
        _k["chest"] += SPINE * 0.5


def catmull(p0, p1, p2, p3, t):
    """标准 Catmull-Rom：经过 p1/p2，速度在关键帧处连续。"""
    t2 = t * t
    t3 = t2 * t
    return 0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (-p0 + 3 * p1 - 3 * p2 + p3) * t3)


def sample(key):
    vals = []
    for i in range(N):
        phase = i * KEYS_N / N
        k = int(phase) % KEYS_N
        u = phase - int(phase)
        p0 = KEYS[(k - 1) % KEYS_N][key]
        p1 = KEYS[k][key]
        p2 = KEYS[(k + 1) % KEYS_N][key]
        p3 = KEYS[(k + 2) % KEYS_N][key]
        vals.append(catmull(p0, p1, p2, p3, u))
    return vals


CH = ["fu", "fl", "fp", "hu", "hl", "hp", "hips", "s1", "s2", "chest", "neck", "head", "lift"]
CURVES = {c: sample(c) for c in CH}


def pose_at(i):
    v = {c: CURVES[c][i] for c in CH}
    clear()
    for side, off in (("L", 1.0), ("R", 0.94)):
        P[f"front_upper_{side}"].rotation_euler = (R(v["fu"] * off), 0, 0)
        P[f"front_lower_{side}"].rotation_euler = (R(v["fl"] * off), 0, 0)
        P[f"front_paw_{side}"].rotation_euler = (R(v["fp"] * off), 0, 0)
        P[f"hind_upper_{side}"].rotation_euler = (R(v["hu"] * off), 0, 0)
        P[f"hind_lower_{side}"].rotation_euler = (R(v["hl"] * off), 0, 0)
        P[f"hind_paw_{side}"].rotation_euler = (R(v["hp"] * off), 0, 0)

    P["hips"].rotation_euler = (R(v["hips"]), 0, 0)
    P["spine_1"].rotation_euler = (R(v["s1"]), 0, 0)
    P["spine_2"].rotation_euler = (R(v["s2"]), 0, 0)
    P["chest"].rotation_euler = (R(v["chest"]), 0, 0)
    P["neck"].rotation_euler = (R(v["neck"]), 0, 0)
    P["head"].rotation_euler = (R(v["head"]), 0, 0)
    P["ear_L"].rotation_euler = (R(-14), 0, 0)
    P["ear_R"].rotation_euler = (R(-14), 0, 0)

    for j in range(1, 7):
        P[f"tail_{j}"].rotation_euler = (R(-5 - 1.0 * j), 0, R(3 * math.sin(2 * math.pi * i / N - j * 0.3)))
    return max(0.0, v["lift"])


def smooth_cyclic(vals, passes=2):
    """沿循环做 3 抽头平滑，去掉逐帧高度里的小跳变。"""
    out = list(vals)
    n = len(out)
    for _ in range(passes):
        prev = list(out)
        for i in range(n):
            out[i] = 0.25 * prev[(i - 1) % n] + 0.5 * prev[i] + 0.25 * prev[(i + 1) % n]
    return out


build_foot_indices()
os.makedirs(FRAMES, exist_ok=True)

# 逐帧算"脚落地需要的 z"，再平滑，避免最低脚切换时抖动
raw = []
for i in range(N):
    lift = pose_at(i)
    raw.append(lift * h - lowest_foot_z())
zs = smooth_cyclic(raw, passes=2)
zs = smooth_cyclic(zs, passes=1)

for i in range(N):
    pose_at(i)
    arm.location.z = zs[i]
    bpy.context.view_layer.update()
    sc.render.filepath = os.path.join(FRAMES, f"{PREFIX}_{i:03d}_{MS}.png")
    bpy.ops.render.render(write_still=True)
    print(f"frame {i}: z={zs[i]:.4f}", flush=True)

print("done", PREFIX, N, "frames", flush=True)
