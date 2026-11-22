"""站立斜向扑（移动中起跳）：基于奔跑姿态，整体倾斜后烘焙。

和 bake_stand_jump.py 的区别：
- 不缩腿扑腾，而是"奔跑中一跃而起"的姿态（前腿略加大前伸）
- 整只猫按抛物线方向倾斜（起飞抬头 → 顶点放平 → 下落低头）
- 帧数少（5 帧），窗口运动由程序用抛物线 + 水平位移合成

环境变量：DJ_PREFIX（默认 sjump_diag）、DJ_AZ（90 = 朝左，270 = 朝右）
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
PREFIX = os.environ.get("DJ_PREFIX", "sjump_diag")
AZ = float(os.environ.get("DJ_AZ", "90"))

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

# 取景：留出余量，倾斜后的猫不会被裁掉
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
L = h / 0.82 * 1.18          # 比常规站立镜头拉远 18%，给倾斜留空间
center = Vector(((mn.x + mx.x) * 0.5, (mn.y + mx.y) * 0.5, mn.z + 0.444 * L))

for _o in [o for o in sc.objects if o.type == 'CAMERA']:
    bpy.data.objects.remove(_o, do_unlink=True)
cd = bpy.data.cameras.new("cam_dj"); cd.type = 'ORTHO'; cd.ortho_scale = L
cam = bpy.data.objects.new("cam_dj", cd); sc.collection.objects.link(cam); sc.camera = cam
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


# 以"奔跑第一帧"（gallop K0/K1 之间）为基底，前腿角度略加大，整体按抛物线方向倾斜
KEYS = [
    # 0 蓄力：跑动中先压低重心（这一帧让"要起跳了"更明显）
    dict(fu=14, fl=48, fp=-8, hu=-18, hl=-50, hp=12, neck=10, head=12, tail=-12, pitch=6, lift=0.0, ms=100),
    # 1 起跳：蹬地抬头
    dict(fu=4, fl=36, fp=-6, hu=36, hl=-28, hp=6, neck=8, head=10, tail=2, pitch=24, lift=0.03, ms=110),
    # 2 上升：前腿前伸抓、后腿蹬直，抬头明显
    dict(fu=-34, fl=-6, fp=8, hu=26, hl=-24, hp=10, neck=6, head=6, tail=12, pitch=36, lift=0.12, ms=140),
    # 3 顶点：身体放平，四肢舒展
    dict(fu=-44, fl=-2, fp=10, hu=12, hl=-16, hp=10, neck=4, head=4, tail=8, pitch=0, lift=0.16, ms=180),
    # 4 下落：低头，前腿准备落地
    dict(fu=-38, fl=8, fp=10, hu=0, hl=-22, hp=12, neck=5, head=7, tail=2, pitch=-18, lift=0.10, ms=150),
    # 5 落地：四肢着地缓冲
    dict(fu=-30, fl=-16, fp=10, hu=-6, hl=-34, hp=12, neck=7, head=9, tail=-4, pitch=-4, lift=0.0, ms=120),
]

# 关键帧 → 每 50ms 一帧（帧数更密，动作更顺）
POSE = sample_frames(KEYS, int(os.environ.get("JUMP_STEP_MS", "50")))


def apply(p):
    for side, off in (("L", 1.0), ("R", 0.94)):
        P[f"front_upper_{side}"].rotation_euler = (R(p["fu"] * off), 0, 0)
        P[f"front_lower_{side}"].rotation_euler = (R(p["fl"] * off), 0, 0)
        P[f"front_paw_{side}"].rotation_euler = (R(p["fp"] * off), 0, 0)
        P[f"hind_upper_{side}"].rotation_euler = (R(p["hu"] * off), 0, 0)
        P[f"hind_lower_{side}"].rotation_euler = (R(p["hl"] * off), 0, 0)
        P[f"hind_paw_{side}"].rotation_euler = (R(p["hp"] * off), 0, 0)

    P["hips"].rotation_euler = (R(p["pitch"] * PITCH_SIGN), 0, 0)
    P["neck"].rotation_euler = (R(p["neck"]), 0, 0)
    P["head"].rotation_euler = (R(p["head"]), 0, 0)
    P["ear_L"].rotation_euler = (R(-8), 0, 0)
    P["ear_R"].rotation_euler = (R(-8), 0, 0)
    for j in range(1, 7):
        P[f"tail_{j}"].rotation_euler = (R(p["tail"] * (0.6 + 0.1 * j)), 0, 0)


os.makedirs(FRAMES, exist_ok=True)
for f in os.listdir(FRAMES):
    if f.startswith(PREFIX + "_") and f.endswith(".png") and f[len(PREFIX) + 1:len(PREFIX) + 4].isdigit():
        os.remove(os.path.join(FRAMES, f))

for i, p in enumerate(POSE):
    clear()
    apply(p)
    # 倾斜后脚可能穿地：统一按 lift 抬起来，不做地面钳制（飞行姿态）
    arm.location.z = p["lift"] * h - lowest_z()
    bpy.context.view_layer.update()
    sc.render.filepath = os.path.join(FRAMES, f"{PREFIX}_{i:03d}_{p['ms']}.png")
    bpy.ops.render.render(write_still=True)
    print(f"frame {i}: z={arm.location.z:.4f}", flush=True)

print("done", PREFIX, len(POSE), "frames", flush=True)
