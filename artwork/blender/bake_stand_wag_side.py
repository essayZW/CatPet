"""站立摇尾巴：右向版本（相机在 -X 侧）。

与 bake_stand3.py 的 clip_wag 完全同一套动作，只把相机换到 az=-25°，
这样"向右追到"之后可以直接播朝右的摇尾巴，不会突然翻个身。

输出：frames_stand3/swag_r_XXX_90.png
"""

import math
import os

import bpy
from mathutils import Vector

OUT = r"D:\code\my_pet\artwork\blender"
FRAMES = os.path.join(OUT, "frames_stand3")
PREFIX = os.environ.get("WAG_PREFIX", "swag_r")
AZ = float(os.environ.get("WAG_AZ", "-25"))
N = int(os.environ.get("WAG_N", "16"))
MS = int(os.environ.get("WAG_MS", "90"))

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
cd = bpy.data.cameras.new("cam_wag"); cd.type = 'ORTHO'; cd.ortho_scale = L
cam = bpy.data.objects.new("cam_wag", cd); sc.collection.objects.link(cam); sc.camera = cam
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


def breath(phase, amp=1.0):
    s = math.sin(2 * math.pi * phase)
    P["chest"].scale = (1 + 0.012 * amp * s, 1 + 0.004 * amp * s, 1 + 0.008 * amp * s)
    P["spine_2"].scale = (1 + 0.005 * amp * s, 1.0, 1 + 0.005 * amp * s)


def head(yaw=0.0, pitch=0.0, roll=0.0):
    P["head"].rotation_euler = (R(pitch), R(roll), R(yaw))


os.makedirs(FRAMES, exist_ok=True)
for f in os.listdir(FRAMES):
    if f.startswith(PREFIX + "_") and f.endswith(".png"):
        os.remove(os.path.join(FRAMES, f))

for i in range(N):
    clear()
    t = i / N
    breath(t, 0.7)
    s = math.sin(2 * math.pi * t)
    for j in range(1, 7):
        ph = s * math.sin(2 * math.pi * t - j * 0.45)
        P[f"tail_{j}"].rotation_euler = (R(4), 0, R(16 * ph))
    head(yaw=-6 * s, pitch=1.5 * math.sin(2 * math.pi * t + 0.4))
    if 6 <= i <= 8:
        k = 1.0 - abs(i - 7) / 2.0
        P["ear_R"].rotation_euler = (R(-16 * k), 0, 0)
    ground_clamp()
    bpy.context.view_layer.update()
    sc.render.filepath = os.path.join(FRAMES, f"{PREFIX}_{i:03d}_{MS}.png")
    bpy.ops.render.render(write_still=True)

print("done", PREFIX, N, "frames", flush=True)
