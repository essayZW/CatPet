"""头部跟随鼠标：11 个水平角度 × 3 个俯仰角度 = 33 张静态帧。

水平：-50° ~ +50°，步长 10°（左/右）
俯仰：上 15° / 平视 / 下 15°
索引：idx = row * 25 + col，row 0=抬头 1=平视 2=低头
"""

import bpy
import math
import os
import math as _m
from mathutils import Vector, Matrix

OUT = r"D:\code\my_pet\artwork\blender"
FRAMES = os.path.join(OUT, "frames_follow_stand")
os.makedirs(FRAMES, exist_ok=True)
for f in os.listdir(FRAMES):
    if f.endswith(".png"):
        os.remove(os.path.join(FRAMES, f))

FOLLOW_STEP = float(os.environ.get("FOLLOW_STEP", "5"))
YAWS = [round(-50 + FOLLOW_STEP * i, 3) for i in range(int(round((50 + 70) / FOLLOW_STEP)) + 1)]
PITCHES = [-20, -10, 0, 10, 20]   # 抬头20 / 抬头10 / 平视 / 低头10 / 低头20

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
        bg.inputs[0].default_value = (0.55, 0.58, 0.62, 1); bg.inputs[1].default_value = 0.6
if not [o for o in sc.objects if o.type == 'LIGHT']:
    for nm, rot, e in [("key", (math.radians(50), 0, math.radians(25)), 3.2),
                       ("fill", (math.radians(70), 0, math.radians(230)), 1.0)]:
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
L = h / 0.82   # 和坐姿完全一致的取景，避免站/坐切换时画面跳动

for o in [o for o in sc.objects if o.type == 'CAMERA']:
    bpy.data.objects.remove(o, do_unlink=True)
cd = bpy.data.cameras.new("cam_sfollow"); cd.type = 'ORTHO'; cd.ortho_scale = L
cam = bpy.data.objects.new("cam_follow", cd); sc.collection.objects.link(cam); sc.camera = cam
center = Vector(((mn.x + mx.x) * 0.5, (mn.y + mx.y) * 0.5, mn.z + 0.444 * L))
az, el = math.radians(25.0), math.radians(7.0)
d = Vector((math.sin(az) * math.cos(el), -math.cos(az) * math.cos(el), math.sin(el)))
cam.location = center + d * 6
cam.rotation_euler = (center - cam.location).to_track_quat('-Z', 'Y').to_euler()

P = {b.name: b for b in arm.pose.bones}
for pb in arm.pose.bones:
    pb.rotation_mode = 'XYZ'
R = math.radians


def rotate_world(bone, axis, deg):
    """绕世界坐标轴、以骨骼头为轴心旋转。"""
    pb = P[bone]
    bpy.context.view_layer.update()
    M = pb.matrix.copy()
    pivot = M.translation.copy()
    Rm = Matrix.Rotation(R(deg), 4, axis)
    pb.matrix = Matrix.Translation(pivot) @ Rm @ Matrix.Translation(-pivot) @ M
    bpy.context.view_layer.update()


def clear():
    for pb in arm.pose.bones:
        pb.rotation_euler = (0, 0, 0); pb.location = (0, 0, 0); pb.scale = (1, 1, 1)


def render(idx, ms=120):
    bpy.context.view_layer.update()
    sc.render.filepath = os.path.join(FRAMES, f"sfollow_{idx:03d}_{ms}.png")
    bpy.ops.render.render(write_still=True)


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
        k = min(1.0, (abs(yaw) / 50.0 + abs(pitch) / 15.0 * 0.4))
        P["ear_L"].rotation_euler = (R(-4 * k), 0, 0)
        P["ear_R"].rotation_euler = (R(-4 * k), 0, 0)
        render(idx)
        idx += 1
    print("row done", row, flush=True)

print("done", idx, flush=True)
