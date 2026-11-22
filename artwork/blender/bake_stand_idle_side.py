"""站立待机：镜像版本（相机在 -X 侧，猫朝画面右）。

与 bake_stand3.py 的 clip_idle 完全同一套动作，只换相机方位到 az=-25°，
所以「向左站」和「向右站」是同一只猫、同一套呼吸/耳朵抖动，只是朝向相反。

输出：frames_stand3/sidle_r_XXX_110.png
"""

import math
import os

import bpy
from mathutils import Vector

OUT = os.path.dirname(os.path.abspath(__file__))
FRAMES = os.path.join(OUT, "frames_stand3")
PREFIX = os.environ.get("IDLE_PREFIX", "sidle_r")
AZ = float(os.environ.get("IDLE_AZ", "-25"))
N = int(os.environ.get("IDLE_N", "20"))
MS = int(os.environ.get("IDLE_MS", "110"))

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
cd = bpy.data.cameras.new("cam_idle"); cd.type = 'ORTHO'; cd.ortho_scale = L
cam = bpy.data.objects.new("cam_idle", cd); sc.collection.objects.link(cam); sc.camera = cam
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


def breath(phase, amp=1.0):
    s = math.sin(2 * math.pi * phase)
    P["chest"].scale = (1 + 0.012 * amp * s, 1 + 0.004 * amp * s, 1 + 0.008 * amp * s)
    P["spine_2"].scale = (1 + 0.005 * amp * s, 1.0, 1 + 0.005 * amp * s)


def head(yaw=0.0, pitch=0.0, roll=0.0):
    P["head"].rotation_euler = (R(pitch), R(roll), R(yaw))


def tail(sway=0.0, lift=0.0, phase_decay=0.75):
    amp = 1.0
    for i in range(1, 7):
        P[f"tail_{i}"].rotation_euler = (R(lift * amp), 0, R(sway * amp))
        amp *= phase_decay


os.makedirs(FRAMES, exist_ok=True)
for f in os.listdir(FRAMES):
    if f.startswith(PREFIX + "_") and f.endswith(".png"):
        os.remove(os.path.join(FRAMES, f))

for i in range(N):
    clear()
    t = i / N
    breath(t)
    s = math.sin(2 * math.pi * t)
    head(yaw=1.2 * math.sin(2 * math.pi * t + 0.5), pitch=0.8 * s)
    tail(sway=7 * math.sin(2 * math.pi * t * 0.5), lift=3 + 1.5 * s)
    if 8 <= i <= 10:
        k = 1.0 - abs(i - 9) / 2.0
        P["ear_L"].rotation_euler = (R(-14 * k), 0, 0)
        P["ear_R"].rotation_euler = (R(-6 * k), 0, 0)
    ground_clamp()
    bpy.context.view_layer.update()
    sc.render.filepath = os.path.join(FRAMES, f"{PREFIX}_{i:03d}_{MS}.png")
    bpy.ops.render.render(write_still=True)

print("done", PREFIX, N, "frames", flush=True)
