"""站立"落定"过渡帧：把行走姿势平滑收成站立待机姿势。

为什么需要：cin_intro / cin_outro 是行走循环，最后一帧多半停在"迈步中"，
直接切到 sidle（站立待机）会看到腿和尾巴"啪"一下跳过去。
这里烘一小段过渡（腿从迈步收回、尾巴从低垂抬到待机角度）。

环境变量：
  SET_PREFIX  输出前缀（默认 sstand_in）
  SET_AZ      方位角（45 = 正面偏左，225 = 背面偏右）
  SET_DIR     in = 行走→待机；out = 待机→行走
  SET_N       帧数（默认 8）
  SET_MS      每帧时长（默认 78）
"""

import math
import os

import bpy
from mathutils import Vector

OUT = os.path.dirname(os.path.abspath(__file__))
FRAMES = os.path.join(OUT, "frames_stand3")
PREFIX = os.environ.get("SET_PREFIX", "sstand_in")
AZ = float(os.environ.get("SET_AZ", "45"))
DIR = os.environ.get("SET_DIR", "in")
N = int(os.environ.get("SET_N", "8"))
MS = int(os.environ.get("SET_MS", "78"))

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
cd = bpy.data.cameras.new("cam_set"); cd.type = 'ORTHO'; cd.ortho_scale = L
cam = bpy.data.objects.new("cam_set", cd); sc.collection.objects.link(cam); sc.camera = cam
az, el = math.radians(AZ), math.radians(7.0)
d = Vector((math.sin(az) * math.cos(el), -math.cos(az) * math.cos(el), math.sin(el)))
cam.location = center + d * 6
cam.rotation_euler = (center - cam.location).to_track_quat('-Z', 'Y').to_euler()

P = {b.name: b for b in arm.pose.bones}
for pb in arm.pose.bones:
    pb.rotation_mode = 'XYZ'
R = math.radians


def clear():
    for pb in arm.pose.bones:
        pb.rotation_euler = (0, 0, 0); pb.location = (0, 0, 0); pb.scale = (1, 1, 1)
    arm.location = (0, 0, 0)


def ground_clamp():
    bpy.context.view_layer.update()
    dg2 = bpy.context.evaluated_depsgraph_get()
    ev2 = obj.evaluated_get(dg2)
    me2 = ev2.to_mesh()
    mw2 = ev2.matrix_world
    minz = min((mw2 @ v.co).z for v in me2.vertices)
    ev2.to_mesh_clear()
    arm.location.z -= minz


# 两个端点姿势（都是"腿伸直"的站立，只有脖子/头/尾巴不同）
WALK = dict(neck=0.96, head_pitch=-0.72, head_yaw=0.0,
            tail_lift=-6.0, tail_sway_var=0.0, tail_bone_var=2.0)   # 行走循环第 0 帧
IDLE = dict(neck=0.0, head_pitch=0.0, head_yaw=0.58,
            tail_lift=3.0, tail_sway_var=0.0, tail_bone_var=0.0)    # 站立待机第 0 帧


def lerp(a, b, t):
    return a + (b - a) * t


def pose(mix):
    """mix=0 → WALK，mix=1 → IDLE"""
    a, b = (WALK, IDLE) if DIR == "in" else (IDLE, WALK)
    clear()
    P["neck"].rotation_euler = (R(lerp(a["neck"], b["neck"], mix)), 0, 0)
    P["head"].rotation_euler = (R(lerp(a["head_pitch"], b["head_pitch"], mix)),
                                R(0),
                                R(lerp(a["head_yaw"], b["head_yaw"], mix)))

    # 尾巴：行走是"低垂 + 每节相位差"，待机是"抬起 + 逐节衰减"
    lift0, lift1 = a["tail_lift"], b["tail_lift"]
    decay = 0.75
    for j in range(1, 7):
        if DIR == "in":
            var = a["tail_bone_var"] * math.sin(-j * 0.3)
            lift = lerp(lift0 + var, lift1 * (decay ** (j - 1)), mix)
        else:
            lift = lerp(lift0 * (decay ** (j - 1)), lift1 + b["tail_bone_var"] * math.sin(-j * 0.3), mix)
        P[f"tail_{j}"].rotation_euler = (R(lift), 0, 0)

    # 站立待机第 0 帧耳朵是 0°，这里也归零，保证首尾帧完全一致
    P["ear_L"].rotation_euler = (0, 0, 0)
    P["ear_R"].rotation_euler = (0, 0, 0)


os.makedirs(FRAMES, exist_ok=True)
for f in os.listdir(FRAMES):
    if f.startswith(PREFIX + "_") and f.endswith(".png") and f[len(PREFIX) + 1:len(PREFIX) + 4].isdigit():
        os.remove(os.path.join(FRAMES, f))

for i in range(N):
    t = i / max(1, N - 1)
    mix = t * t * (3 - 2 * t)          # smoothstep
    pose(mix)
    ground_clamp()
    bpy.context.view_layer.update()
    sc.render.filepath = os.path.join(FRAMES, f"{PREFIX}_{i:03d}_{MS}.png")
    bpy.ops.render.render(write_still=True)
    print(f"frame {i} mix={mix:.2f}", flush=True)

print("done", PREFIX, N, "frames", flush=True)
