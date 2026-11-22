"""站立跳跃：蓄力 → 蹬地 → 空中收腿 → 伸腿落地 → 缓冲（10 帧，单次播放，不循环）。

窗口的纵向位移由程序用抛物线驱动，这里只负责姿势：
- 第 0~2 帧、第 7~9 帧"脚贴地"（做地面钳制）
- 第 3~6 帧是腾空，root 高度固定在第 2 帧的位置，靠收腿表现离地

环境变量：JUMP_PREFIX（默认 sjump）、JUMP_AZ（25 = 朝左，-25 = 朝右）
"""

import math
import os

import bpy
from mathutils import Vector

import sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from jump_keys import sample_frames

OUT = r"D:\code\my_pet\artwork\blender"
FRAMES = os.path.join(OUT, "frames_stand3")
PREFIX = os.environ.get("JUMP_PREFIX", "sjump")
AZ = float(os.environ.get("JUMP_AZ", "25"))
MS = int(os.environ.get("JUMP_MS", "60"))

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
    w = bpy.data.worlds.new("W"); sc.world = w; w.use_nodes = True
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
center = Vector(((mn.x + mx.x) * 0.5, (mn.y + mx.y) * 0.5, mn.z + 0.444 * L))

for _o in [o for o in sc.objects if o.type == 'CAMERA']:
    bpy.data.objects.remove(_o, do_unlink=True)
cd = bpy.data.cameras.new("cam_jump"); cd.type = 'ORTHO'; cd.ortho_scale = L
cam = bpy.data.objects.new("cam_jump", cd); sc.collection.objects.link(cam); sc.camera = cam
az, el = math.radians(AZ), math.radians(7.0)
d = Vector((math.sin(az) * math.cos(el), -math.cos(az) * math.cos(el), math.sin(el)))
cam.location = center + d * 6
cam.rotation_euler = (center - cam.location).to_track_quat('-Z', 'Y').to_euler()

P = {b.name: b for b in arm.pose.bones}
for pb in arm.pose.bones:
    pb.rotation_mode = 'XYZ'
R = math.radians
# 相机在 -X 侧时，同一个 pitch 在画面里是镜像的：朝右那套要反号
PITCH_SIGN = 1.0   # 实测：绕 hips 的 pitch 在两侧画面里方向一致，不需要按机位反号


def clear():
    for pb in arm.pose.bones:
        pb.rotation_euler = (0, 0, 0); pb.location = (0, 0, 0); pb.scale = (1, 1, 1)
    arm.location = (0, 0, 0)


def lowest_z():
    bpy.context.view_layer.update()
    dg2 = bpy.context.evaluated_depsgraph_get()
    ev2 = obj.evaluated_get(dg2)
    me2 = ev2.to_mesh()
    mw2 = ev2.matrix_world
    m = min((mw2 @ v.co).z for v in me2.vertices)
    ev2.to_mesh_clear()
    return m


# 关键姿势：fu/fl/fp 前腿，hu/hl/hp 后腿，neck/head 抬头，tail 抬尾
# 方向约定（probe 校准过）：前腿 upper 正=向后、负=向前；lower 正=收/屈、负=伸
#                       后腿 upper 正=蹬、负=收；lower 正=后、负=伸直
KEYS = [
    # 0 准备：开始低头收身（蓄力第一段，放慢）
    dict(fu=12, fl=46, fp=-10, hu=-16, hl=-44, hp=10, neck=9, head=11, tail=-10, contact=True, ms=100),
    # 1 深蹲到底：明显压低身体、尾巴压低（蓄力第二段，最长）
    dict(fu=24, fl=62, fp=-6, hu=-28, hl=-58, hp=14, neck=14, head=16, tail=-20, contact=True, ms=140),
    # 2 蹬地：后腿猛蹬、前腿前伸
    dict(fu=8, fl=38, fp=-8, hu=40, hl=-26, hp=6, neck=8, head=10, tail=2, contact=True, ms=70),
    # 3 离地伸展（gallop 腾空姿势）
    dict(fu=-42, fl=-10, fp=6, hu=32, hl=-26, hp=8, neck=6, head=6, tail=10, pitch=22, lift=0.06, contact=False, ms=70),
    # 4 空中收腿：前腿折到胸前、后腿往后收
    dict(fu=-40, fl=80, fp=10, hu=10, hl=16, hp=6, neck=4, head=3, tail=16, pitch=28, lift=0.13, contact=False, ms=70),
    # 5 最高点：四肢收拢、尾巴翘起
    dict(fu=-20, fl=88, fp=12, hu=-34, hl=30, hp=4, neck=2, head=0, tail=22, pitch=16, lift=0.17, contact=False, ms=80),
    # 6 下落：开始伸腿
    dict(fu=-36, fl=44, fp=8, hu=2, hl=-10, hp=8, neck=4, head=5, tail=12, pitch=-6, lift=0.09, contact=False, ms=80),
    # 7 触地前伸腿（gallop 落地姿势）
    dict(fu=-32, fl=-20, fp=10, hu=-6, hl=-34, hp=12, neck=7, head=9, tail=2, pitch=-8, contact=True, ms=100),
    # 8 触地缓冲
    dict(fu=-18, fl=26, fp=4, hu=-10, hl=-44, hp=12, neck=9, head=11, tail=-6, contact=True, ms=110),
    # 9 收势回到接近待机
    dict(fu=6, fl=44, fp=-8, hu=-12, hl=-40, hp=9, neck=8, head=9, tail=-6, contact=True, ms=140),
]

# 关键帧 → 每 50ms 一帧（帧数更密，动作更顺）
POSE = sample_frames(KEYS, int(os.environ.get("JUMP_STEP_MS", "50")))

def apply(p, i):
    for side, off in (("L", 1.0), ("R", 0.94)):
        P[f"front_upper_{side}"].rotation_euler = (R(p["fu"] * off), 0, 0)
        P[f"front_lower_{side}"].rotation_euler = (R(p["fl"] * off), 0, 0)
        P[f"front_paw_{side}"].rotation_euler = (R(p["fp"] * off), 0, 0)
        P[f"hind_upper_{side}"].rotation_euler = (R(p["hu"] * off), 0, 0)
        P[f"hind_lower_{side}"].rotation_euler = (R(p["hl"] * off), 0, 0)
        P[f"hind_paw_{side}"].rotation_euler = (R(p["hp"] * off), 0, 0)

    P["hips"].rotation_euler = (R(p.get("pitch", 0.0) * PITCH_SIGN), 0, 0)
    P["neck"].rotation_euler = (R(p["neck"]), 0, 0)
    P["head"].rotation_euler = (R(p["head"]), 0, 0)
    P["ear_L"].rotation_euler = (R(-10), 0, 0)
    P["ear_R"].rotation_euler = (R(-10), 0, 0)
    for j in range(1, 7):
        P[f"tail_{j}"].rotation_euler = (R(p["tail"] * (0.6 + 0.1 * j)), 0, 0)


os.makedirs(FRAMES, exist_ok=True)
for f in os.listdir(FRAMES):
    if f.startswith(PREFIX + "_") and f.endswith(".png") and f[len(PREFIX) + 1:len(PREFIX) + 4].isdigit():
        os.remove(os.path.join(FRAMES, f))

airborne_z = None
for i, p in enumerate(POSE):
    clear()
    apply(p, i)
    if p["contact"]:
        z = -lowest_z()                            # 脚贴地
    else:
        z = p.get("lift", 0.0) * h - lowest_z()     # 腾空：整体抬高，脚离地
    arm.location.z = z
    bpy.context.view_layer.update()
    sc.render.filepath = os.path.join(FRAMES, f"{PREFIX}_{i:03d}_{p.get('ms', MS)}.png")
    bpy.ops.render.render(write_still=True)
    print(f"frame {i}: z={z:.4f}", flush=True)

print("done", PREFIX, len(POSE), "frames", flush=True)
