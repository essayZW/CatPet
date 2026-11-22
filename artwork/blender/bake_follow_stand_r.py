"""站立头部跟随：镜像版本（相机在 -X 侧，猫朝画面右）。

与 bake_follow_stand.py 相同的网格（25 列 × 5 行），但：
- 相机 az=-25°
- 水平角度取镜像序列：+50 → -70（步长 -5），这样「画面右侧」仍落在最后一档，
  代码里 col 的计算公式不用变
索引：idx = row * 25 + col
"""

import math
import os

import bpy
from mathutils import Vector, Matrix

OUT = r"D:\code\my_pet\artwork\blender"
FRAMES = os.path.join(OUT, "frames_follow_stand_r")
PREFIX = os.environ.get("FOLLOW_PREFIX", "sfollow_r")
AZ = float(os.environ.get("FOLLOW_AZ", "-25"))
os.makedirs(FRAMES, exist_ok=True)
for f in os.listdir(FRAMES):
    if f.endswith(".png"):
        os.remove(os.path.join(FRAMES, f))

# 镜像：col 0 → +50°，col 24 → -70°
FOLLOW_STEP = float(os.environ.get("FOLLOW_STEP", "5"))
YAWS = [round(50 - FOLLOW_STEP * i, 3) for i in range(int(round((50 + 70) / FOLLOW_STEP)) + 1)]
PITCHES = [-20, -10, 0, 10, 20]

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
cd = bpy.data.cameras.new("cam_follow_r"); cd.type = 'ORTHO'; cd.ortho_scale = L
cam = bpy.data.objects.new("cam_follow_r", cd); sc.collection.objects.link(cam); sc.camera = cam
az, el = math.radians(AZ), math.radians(7.0)
d = Vector((math.sin(az) * math.cos(el), -math.cos(az) * math.cos(el), math.sin(el)))
cam.location = center + d * 6
cam.rotation_euler = (center - cam.location).to_track_quat('-Z', 'Y').to_euler()

P = {b.name: b for b in arm.pose.bones}
for pb in arm.pose.bones:
    pb.rotation_mode = 'XYZ'
R = math.radians
AXES = {'X': 0, 'Y': 1, 'Z': 2}


def clear():
    for pb in arm.pose.bones:
        pb.rotation_euler = (0, 0, 0)
        pb.location = (0, 0, 0)
        pb.scale = (1, 1, 1)
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


def rotate_world(bone, axis, deg):
    """绕世界坐标轴、以骨骼头为轴心旋转（与 bake_follow_stand.py 同一套做法）。"""
    pb = P[bone]
    bpy.context.view_layer.update()
    M = pb.matrix.copy()
    pivot = M.translation.copy()
    Rm = Matrix.Rotation(R(deg), 4, axis)
    pb.matrix = Matrix.Translation(pivot) @ Rm @ Matrix.Translation(-pivot) @ M
    bpy.context.view_layer.update()


idx = 0
for row, pitch in enumerate(PITCHES):
    for col, yaw in enumerate(YAWS):
        clear()
        if yaw != 0:
            rotate_world("neck", 'Z', yaw * 0.35)
            rotate_world("head", 'Z', yaw * 0.65)
        if pitch != 0:
            rotate_world("neck", 'X', pitch * 0.3)
            rotate_world("head", 'X', pitch * 0.7)
        # 耳朵跟着头微动，尾巴轻轻摆，避免完全僵死
        P["ear_L"].rotation_euler = (R(-4), 0, 0)
        P["ear_R"].rotation_euler = (R(-4), 0, 0)
        for j in range(1, 7):
            P[f"tail_{j}"].rotation_euler = (R(-3 - 0.6 * j), 0, R(2.0 * math.sin(0.3 * col)))
        ground_clamp()
        bpy.context.view_layer.update()
        sc.render.filepath = os.path.join(FRAMES, f"{PREFIX}_{idx:03d}_1000.png")
        bpy.ops.render.render(write_still=True)
        idx += 1
    print("row", row, "done", flush=True)

print("done", PREFIX, idx, "frames", flush=True)
