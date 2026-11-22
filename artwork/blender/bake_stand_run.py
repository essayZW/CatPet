"""站立猫：向左 / 向右 奔跑（侧视）。

在 plan_a_rig.blend 上烘焙，只新增 srun_l / srun_r，不动已有动作。
侧视：相机在 +X（az=90）看到的是向左跑，-X（az=270）向右跑。
"""

import math
import os

import bpy
from mathutils import Vector

OUT = os.path.dirname(os.path.abspath(__file__))
FRAMES = os.path.join(OUT, "frames_stand3")
VARIANTS = [("srun_l", 1.0), ("srun_r", 1.0)]   # 摆幅 24/26，左右各一套

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

# 取景：与站立素材一致
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
cd = bpy.data.cameras.new("cam_run"); cd.type = 'ORTHO'; cd.ortho_scale = L
cam = bpy.data.objects.new("cam_run", cd); sc.collection.objects.link(cam); sc.camera = cam
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


def _pose(i, n, k=1.0):
    """摆出第 i 帧的奔跑姿势（不做落地处理）。"""
    clear()
    t = i / n
    a = 2 * math.pi * t

    fl = math.sin(a)
    fr = math.sin(a + math.pi)
    hl = math.sin(a + math.pi * 0.62)
    hr = math.sin(a + math.pi * 1.62)

    leg("L", 24 * k * fl, -17 * k * max(0.0, fl), front=True, paw=8 * k * fl)
    leg("R", 24 * k * fr, -17 * k * max(0.0, fr), front=True, paw=8 * k * fr)
    leg("L", 26 * k * hl, -19 * k * max(0.0, -hl), front=False, paw=6 * k * hl)
    leg("R", 26 * k * hr, -19 * k * max(0.0, -hr), front=False, paw=6 * k * hr)

    # 每步一次起伏（正值抬起），配合落地钳制会产生腾空感
    arm.location.z = 0.018 * h * (0.5 - 0.5 * math.cos(2 * a))
    P["hips"].rotation_euler = (R(-4), 0, R(2.5 * math.sin(a)))
    P["spine_1"].rotation_euler = (R(-5 + 2 * math.sin(2 * a)), 0, 0)
    P["spine_2"].rotation_euler = (R(-4 + 2 * math.sin(2 * a + 0.6)), 0, 0)
    P["chest"].rotation_euler = (R(-3), 0, R(-2 * math.sin(a)))

    P["neck"].rotation_euler = (R(7 + 1.5 * math.sin(2 * a + 0.4)), 0, 0)
    P["head"].rotation_euler = (R(9 - 1.5 * math.sin(2 * a + 0.4)), 0, 0)
    P["ear_L"].rotation_euler = (R(-12), 0, 0)
    P["ear_R"].rotation_euler = (R(-12), 0, 0)

    for j in range(1, 7):
        base = -14 - 2 * j
        P[f"tail_{j}"].rotation_euler = (R(base * 0.25), 0, R(3 * math.sin(a - j * 0.35)))


def _lowest_z():
    bpy.context.view_layer.update()
    dg2 = bpy.context.evaluated_depsgraph_get()
    ev2 = obj.evaluated_get(dg2)
    me2 = ev2.to_mesh()
    mw2 = ev2.matrix_world
    minz = min((mw2 @ v.co).z for v in me2.vertices)
    ev2.to_mesh_clear()
    return minz


RUN_N = int(os.environ.get("RUN_N", "8"))
RUN_MS = int(os.environ.get("RUN_MS", "60"))


def run(prefix, n=RUN_N, ms=RUN_MS, k=1.0):
    """两遍：先量整轮的最低点，再统一抬高，让"触地"只发生在最低那一帧。"""
    lows = []
    for i in range(n):
        _pose(i, n, k)
        lows.append(_lowest_z())
    base = min(lows)

    for i in range(n):
        _pose(i, n, k)
        bpy.context.view_layer.update()
        # 只把低于整轮最低点的部分抬起来 → 其余帧自然离地
        arm.location.z += (base - lows[i]) + 0.0
        bpy.context.view_layer.update()
        sc.render.filepath = os.path.join(FRAMES, f"{prefix}_{i:03d}_{ms}.png")
        bpy.ops.render.render(write_still=True)

# +X 相机 → 猫头朝画面左 → 向左跑；-X → 向右跑
for prefix, k in VARIANTS:
    place_camera(90 if prefix.endswith("_l") else 270)
    run(prefix, k=k)

print("done", flush=True)
