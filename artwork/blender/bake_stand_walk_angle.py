"""站立猫：向左 / 向右 行走（纯侧视图）。

在 plan_a_rig.blend 上烘焙，只新增这两个动作，不动已有的 sidle / swag。
侧视：相机在 ±X 方向（az=90 / 270），猫面朝 -Y，所以 90° 看到的是向左走、270° 是向右走。
"""

import bpy
import math
import os
from mathutils import Vector

OUT = r"D:\code\my_pet\artwork\blender"
FRAMES = os.path.join(OUT, "frames_stand3")

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

# 取景：和现有素材一致（变形后包围盒，ortho = h/0.82）
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
cd = bpy.data.cameras.new("cam_side"); cd.type = 'ORTHO'; cd.ortho_scale = L
cam = bpy.data.objects.new("cam_side", cd); sc.collection.objects.link(cam); sc.camera = cam
center = Vector(((mn.x + mx.x) * 0.5, (mn.y + mx.y) * 0.5, mn.z + 0.444 * L))

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


def place_camera(az_deg):
    az, el = math.radians(az_deg), math.radians(6.0)
    d = Vector((math.sin(az) * math.cos(el), -math.cos(az) * math.cos(el), math.sin(el)))
    cam.location = center + d * 6
    cam.rotation_euler = (center - cam.location).to_track_quat('-Z', 'Y').to_euler()


def render(prefix, idx, ms):
    ground_clamp()
    bpy.context.view_layer.update()
    sc.render.filepath = os.path.join(FRAMES, f"{prefix}_{idx:03d}_{ms}.png")
    bpy.ops.render.render(write_still=True)


def leg(side, up, lo, front=True, paw=0.0):
    pre = "front" if front else "hind"
    P[f"{pre}_upper_{side}"].rotation_euler = (R(up), 0, 0)
    P[f"{pre}_lower_{side}"].rotation_euler = (R(lo), 0, 0)
    P[f"{pre}_paw_{side}"].rotation_euler = (R(paw), 0, 0)


def walk(prefix, n=12, ms=90):
    """侧视行走：前后腿在矢状面内反相摆动。"""
    for i in range(n):
        clear()
        t = i / n
        a = 2 * math.pi * t
        swing = math.sin(a)
        # 同侧前腿与后腿反相（对角步态的侧视近似）
        leg("L", 30 * swing, -22 * swing, front=True, paw=10 * swing)
        leg("R", -30 * swing, 22 * swing, front=True, paw=-10 * swing)
        leg("L", -26 * swing, 20 * swing, front=False, paw=8 * swing)
        leg("R", 26 * swing, -20 * swing, front=False, paw=-8 * swing)
        arm.location.z = -0.010 * h * (0.5 - 0.5 * math.cos(2 * a))
        P["spine_2"].rotation_euler = (R(3 * math.sin(2 * a)), 0, 0)
        P["chest"].rotation_euler = (R(-2 * math.sin(2 * a)), 0, 0)
        # 头随步伐轻微起伏
        P["neck"].rotation_euler = (R(2 * math.sin(2 * a + 0.5)), 0, 0)
        P["head"].rotation_euler = (R(-1.5 * math.sin(2 * a + 0.5)), 0, 0)
        # 尾巴：行走时略垂、左右摆
        for j in range(1, 7):
            P[f"tail_{j}"].rotation_euler = (R(-6 + 2 * math.sin(a - j * 0.3)), 0, R(5 * math.sin(a - j * 0.25)))
        render(prefix, i, ms)


# 任意角度的行走（用于启动/退场的 45° 素材）
# 环境变量：W45_PREFIX（文件名前缀）、W45_AZ（方位角）、W45_N（帧数）、W45_MS（每帧时长）
PREFIX = os.environ.get("W45_PREFIX", "swalk45_l")
AZ = float(os.environ.get("W45_AZ", "45"))
N = int(os.environ.get("W45_N", "12"))
MS = int(os.environ.get("W45_MS", "90"))

place_camera(AZ)
walk(PREFIX, N, MS)
print("done", PREFIX, "az", AZ, N, "frames", flush=True)
