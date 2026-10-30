"""Generador oficial de digger_enemy (Excavador) para LULLABY:
Referencia visual exacta (Nano Banana):
  - Cabeza de felpa ocre con costura central tosca en la frente, ceño fruncido y cuello de arpillera
  - Hocico rosado estriado y truncado al frente
  - Manoplas con costuras en las muñecas y 4 garras curvas prominentes de marfil
  - Montículo de tierra oscura con terrones y cráter de emergencia

Partes separadas para animación en Godot (6 partes oficiales):
  - Body: Lomo arqueado de felpa ocre emergiendo del montículo
  - Head: Cabeza con ceño marcado, costura frontal, ojos oscuros y babero de arpillera
  - Snout: Hocico cónico estriado rosado apuntando al frente (+Y)
  - ArmL: Brazo izquierdo con manopla y 4 garras curvas, pivote en hombro
  - ArmR: Brazo derecho con manopla y 4 garras curvas, pivote en hombro
  - Mound: Montículo de tierra removida con cráter central, pivote en el suelo (0, 0, 0)

Reglas técnicas:
  - Huella <= 1.0 x 1.0 m
  - Triángulos <= 5,000
  - Frente hacia +Y en Blender (importa hacia -Z en Godot)
  - Vistas previas en 3 paneles (De frente | Cámara del juego a 60° | Perfil)
  - Sin residuos (.blend1)
"""

import math
import os
import shutil
import tempfile
from pathlib import Path

import bpy
import numpy as np
from mathutils import Matrix, Vector

REPO_ROOT = Path(r"C:\LULLABY")
MODELS_DIR = REPO_ROOT / "assets" / "models"
SOURCE_DIR = REPO_ROOT / "assets" / "source" / "blender"
DRIVE_DIR = Path(r"C:\Users\davin\OneDrive\Documentos\MODELOS JUEGO\digger_enemy")

MODELS_DIR.mkdir(parents=True, exist_ok=True)
SOURCE_DIR.mkdir(parents=True, exist_ok=True)
DRIVE_DIR.mkdir(parents=True, exist_ok=True)

GLB_PATH = MODELS_DIR / "digger_enemy.glb"
BLEND_PATH = SOURCE_DIR / "digger_enemy.blend"
PY_PATH = SOURCE_DIR / "digger_enemy.py"
PREVIEW_PATH = SOURCE_DIR / "digger_enemy_preview.png"

RENDER_SIZE = 800

# Paleta exacta de la referencia (sRGB)
PALETTE = {
    "MustardFelt": (0.78, 0.58, 0.20),    # Fieltro amarillo mostaza/ocre desgastado
    "KnittedPink": (0.84, 0.52, 0.50),    # Hocico rosado estriado
    "BurlapCowl": (0.62, 0.50, 0.38),     # Cuello/capucha de tela de arpillera
    "IvoryClaw": (0.92, 0.90, 0.82),      # Garras curvas de marfil/hueso
    "DarkThread": (0.16, 0.11, 0.08),     # Ojos oscuros y puntadas de hilo
    "StitchCream": (0.94, 0.90, 0.80),    # Puntadas gruesas en la frente
    "DarkEarth": (0.24, 0.17, 0.13),      # Tierra oscura del montículo
}

bpy.context.preferences.filepaths.save_version = 0


def clean_scene():
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete(use_global=False)
    for block in bpy.data.meshes:
        if block.users == 0:
            bpy.data.meshes.remove(block)
    for block in bpy.data.materials:
        if block.users == 0:
            bpy.data.materials.remove(block)
    for block in bpy.data.cameras:
        if block.users == 0:
            bpy.data.cameras.remove(block)
    for block in bpy.data.lights:
        if block.users == 0:
            bpy.data.lights.remove(block)


def srgb_to_linear(v: float) -> float:
    return v / 12.92 if v <= 0.04045 else ((v + 0.055) / 1.055) ** 2.4


def make_materials():
    materials = {}
    for name, srgb in PALETTE.items():
        mat = bpy.data.materials.new(name)
        mat.use_nodes = True
        linear = tuple(srgb_to_linear(c) for c in srgb)
        bsdf = None
        for n in mat.node_tree.nodes:
            if n.type == 'BSDF_PRINCIPLED':
                bsdf = n
                break
        if bsdf:
            for inp in bsdf.inputs:
                if inp.name == "Base Color":
                    inp.default_value = (*linear, 1.0)
                elif inp.name == "Roughness":
                    inp.default_value = 0.30 if name in ("IvoryClaw", "DarkThread") else 0.80
                elif inp.name == "Metallic":
                    inp.default_value = 0.0
                elif inp.name in ("Specular IOR Level", "Specular"):
                    inp.default_value = 0.50 if name in ("IvoryClaw", "DarkThread") else 0.15
        mat.diffuse_color = (*linear, 1.0)
        materials[name] = mat
    return materials


def set_pivot(obj, pivot_co: tuple[float, float, float]):
    current_cursor = bpy.context.scene.cursor.location.copy()
    bpy.context.scene.cursor.location = pivot_co
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.origin_set(type='ORIGIN_CURSOR', center='MEDIAN')
    bpy.context.scene.cursor.location = current_cursor


def build_digger_enemy(materials):
    digger_col = bpy.data.collections.new("DiggerEnemy")
    bpy.context.scene.collection.children.link(digger_col)

    # =========================================================================
    # 1. BODY: Lomo encorvado de felpa ocre emergiendo del cráter
    # =========================================================================
    bpy.ops.mesh.primitive_uv_sphere_add(segments=12, ring_count=8, radius=0.24, location=(0, -0.08, 0.24))
    body = bpy.context.object
    body.name = "Body"
    body.scale = (0.95, 1.30, 0.98)
    body.rotation_euler = (math.radians(-18), 0, 0)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    body.data.materials.append(materials["MustardFelt"])

    # Costura dorsal trasera en relieve
    bpy.ops.mesh.primitive_cylinder_add(vertices=6, radius=0.010, depth=0.48, location=(0, -0.10, 0.32))
    seam_back = bpy.context.object
    seam_back.rotation_euler = (math.radians(-72), 0, 0)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
    seam_back.data.materials.append(materials["StitchCream"])

    bpy.context.view_layer.objects.active = body
    seam_back.select_set(True)
    body.select_set(True)
    bpy.ops.object.join()
    body_obj = bpy.context.object
    set_pivot(body_obj, (0.0, 0.0, 0.0))

    # =========================================================================
    # 2. HEAD: Cabeza con ceño marcado, costura frontal, ojos y cuello de arpillera
    # Pivote en el cuello (0.0, 0.02, 0.34)
    # =========================================================================
    bpy.ops.mesh.primitive_uv_sphere_add(segments=14, ring_count=10, radius=0.20, location=(0, 0.10, 0.38))
    head = bpy.context.object
    head.name = "Head"
    head.scale = (1.05, 1.05, 0.95)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    head.data.materials.append(materials["MustardFelt"])

    # Gran costura central con puntadas en la frente (detalle clave de la referencia)
    # Línea central
    bpy.ops.mesh.primitive_cylinder_add(vertices=6, radius=0.007, depth=0.26, location=(0, 0.22, 0.44))
    seam_front = bpy.context.object
    seam_front.rotation_euler = (math.radians(35), 0, 0)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
    seam_front.data.materials.append(materials["StitchCream"])

    # Puntadas cruzadas horizontales a lo largo de la frente
    stitches = []
    for sz in [0.38, 0.43, 0.48, 0.52]:
        sy = 0.14 + (0.55 - sz) * 0.75
        bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0, sy, sz))
        st = bpy.context.object
        st.scale = (0.045, 0.008, 0.006)
        st.rotation_euler = (math.radians(30), 0, 0)
        bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
        st.data.materials.append(materials["StitchCream"])
        stitches.append(st)

    # Ceño esculpido grueso y angular sobre los ojos
    bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=0.045, depth=0.14, location=(-0.09, 0.23, 0.42))
    brow_l = bpy.context.object
    brow_l.rotation_euler = (math.radians(25), math.radians(-35), math.radians(18))
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
    brow_l.data.materials.append(materials["MustardFelt"])

    bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=0.045, depth=0.14, location=(0.09, 0.23, 0.42))
    brow_r = bpy.context.object
    brow_r.rotation_euler = (math.radians(25), math.radians(35), math.radians(-18))
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
    brow_r.data.materials.append(materials["MustardFelt"])

    # Ojos oscuros de cuenta/vidrio bajo el ceño
    bpy.ops.mesh.primitive_uv_sphere_add(segments=8, ring_count=6, radius=0.028, location=(-0.08, 0.25, 0.38))
    eye_l = bpy.context.object
    eye_l.data.materials.append(materials["DarkThread"])

    bpy.ops.mesh.primitive_uv_sphere_add(segments=8, ring_count=6, radius=0.028, location=(0.08, 0.25, 0.38))
    eye_r = bpy.context.object
    eye_r.data.materials.append(materials["DarkThread"])

    # Brillo reflejo en los ojos
    for ex in [-0.075, 0.085]:
        bpy.ops.mesh.primitive_uv_sphere_add(segments=4, ring_count=3, radius=0.007, location=(ex, 0.27, 0.39))
        glint = bpy.context.object
        glint.data.materials.append(materials["StitchCream"])
        stitches.append(glint)

    # Babero / capucha de arpillera gruesa alrededor del cuello y pecho
    bpy.ops.mesh.primitive_torus_add(major_segments=12, minor_segments=5, major_radius=0.22, minor_radius=0.045, location=(0, 0.08, 0.27))
    cowl = bpy.context.object
    cowl.scale = (1.05, 1.15, 0.90)
    cowl.rotation_euler = (math.radians(-15), 0, 0)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    cowl.data.materials.append(materials["BurlapCowl"])

    # Flecos / jirones de arpillera alrededor del cuello
    for ang in [-0.8, -0.4, 0.0, 0.4, 0.8]:
        fx = 0.23 * math.sin(ang)
        fy = 0.08 + 0.23 * math.cos(ang)
        bpy.ops.mesh.primitive_cone_add(vertices=4, radius1=0.035, radius2=0.008, depth=0.07, location=(fx, fy, 0.23))
        fringe = bpy.context.object
        fringe.rotation_euler = (math.radians(-35), 0, math.radians(ang * 25))
        bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
        fringe.data.materials.append(materials["BurlapCowl"])
        stitches.append(fringe)

    # Unir componentes a Head
    bpy.context.view_layer.objects.active = head
    for p in [seam_front, brow_l, brow_r, eye_l, eye_r, cowl] + stitches:
        p.select_set(True)
    head.select_set(True)
    bpy.ops.object.join()
    head_obj = bpy.context.object
    set_pivot(head_obj, (0.0, 0.02, 0.34))

    # =========================================================================
    # 3. SNOUT: Hocico cónico rosado estriado y alargado (+Y)
    # Pivote en la base del hocico (0.0, 0.20, 0.31)
    # =========================================================================
    pivot_snout = (0.0, 0.20, 0.31)
    # Cuerpo cónico del hocico
    bpy.ops.mesh.primitive_cone_add(vertices=12, radius1=0.078, radius2=0.048, depth=0.20, location=(0, 0.30, 0.28))
    snout_main = bpy.context.object
    snout_main.name = "Snout"
    snout_main.rotation_euler = (math.radians(72), 0, 0)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
    snout_main.data.materials.append(materials["KnittedPink"])

    # Anillos estriados horizontales (efecto de tejido de punto de la referencia)
    snout_rings = []
    for ry, rz, rad in [(0.25, 0.30, 0.074), (0.29, 0.28, 0.065), (0.33, 0.26, 0.056), (0.37, 0.24, 0.048)]:
        bpy.ops.mesh.primitive_torus_add(major_segments=10, minor_segments=4, major_radius=rad, minor_radius=0.008, location=(0, ry, rz))
        ring_obj = bpy.context.object
        ring_obj.rotation_euler = (math.radians(72), 0, 0)
        bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
        ring_obj.data.materials.append(materials["KnittedPink"])
        snout_rings.append(ring_obj)

    # Punta chata del hocico
    bpy.ops.mesh.primitive_cylinder_add(vertices=10, radius=0.048, depth=0.025, location=(0, 0.40, 0.23))
    snout_tip = bpy.context.object
    snout_tip.rotation_euler = (math.radians(72), 0, 0)
    snout_tip.data.materials.append(materials["KnittedPink"])

    bpy.context.view_layer.objects.active = snout_main
    for r in snout_rings + [snout_tip]:
        r.select_set(True)
    snout_main.select_set(True)
    bpy.ops.object.join()
    snout_obj = bpy.context.object
    set_pivot(snout_obj, pivot_snout)

    # =========================================================================
    # Función para construir las manoplas con costura de muñeca y 4 garras curvas
    # =========================================================================
    def build_digging_paw(name: str, side_sign: float, pivot: tuple[float, float, float]):
        # Brazo muscular ocre
        bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=0.07, depth=0.22, location=(pivot[0] + side_sign * 0.09, pivot[1] + 0.08, pivot[2] - 0.06))
        arm = bpy.context.object
        arm.name = name
        arm.rotation_euler = (math.radians(45), side_sign * math.radians(-32), side_sign * math.radians(22))
        bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
        arm.data.materials.append(materials["MustardFelt"])

        # Costura tosca anular en la muñeca (detalle artesanal de la foto)
        bpy.ops.mesh.primitive_torus_add(major_segments=8, minor_segments=4, major_radius=0.072, minor_radius=0.012, location=(pivot[0] + side_sign * 0.12, pivot[1] + 0.15, 0.14))
        wrist_stitch = bpy.context.object
        wrist_stitch.rotation_euler = (math.radians(45), side_sign * math.radians(-32), 0)
        bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
        wrist_stitch.data.materials.append(materials["StitchCream"])

        # Manopla gruesa acolchada de felpa
        bpy.ops.mesh.primitive_cube_add(size=1.0, location=(pivot[0] + side_sign * 0.14, pivot[1] + 0.20, 0.10))
        paw = bpy.context.object
        paw.scale = (0.17, 0.15, 0.085)
        paw.rotation_euler = (math.radians(20), side_sign * math.radians(-15), 0)
        bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
        paw.data.materials.append(materials["MustardFelt"])

        # Las 4 grandes garras curvas de marfil (hueso) apoyadas sobre el montículo
        claw_objs = []
        for i, cx in enumerate([-0.055, -0.018, 0.020, 0.058]):
            claw_x = pivot[0] + side_sign * (0.14 + cx * 0.95)
            # Cono curvado con inclinación pronunciada hacia abajo clavándose en la tierra
            bpy.ops.mesh.primitive_cone_add(vertices=6, radius1=0.022, radius2=0.005, depth=0.18, location=(claw_x, pivot[1] + 0.30, 0.07))
            claw = bpy.context.object
            # Curvatura hacia abajo (+Y y -Z)
            claw.rotation_euler = (math.radians(78 + (i % 2) * 6), side_sign * math.radians(-12 + i * 5), 0)
            bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
            claw.data.materials.append(materials["IvoryClaw"])
            claw_objs.append(claw)

        bpy.context.view_layer.objects.active = arm
        for p in [wrist_stitch, paw] + claw_objs:
            p.select_set(True)
        arm.select_set(True)
        bpy.ops.object.join()
        arm_obj = bpy.context.object
        set_pivot(arm_obj, pivot)
        return arm_obj

    # 4. ARM_L: Brazo excavador izquierdo (-X)
    arm_l_obj = build_digging_paw("ArmL", -1.0, (-0.22, 0.08, 0.30))

    # 5. ARM_R: Brazo excavador derecho (+X)
    arm_r_obj = build_digging_paw("ArmR", 1.0, (0.22, 0.08, 0.30))

    # =========================================================================
    # 6. MOUND: Montículo de tierra oscura con terrones y cráter de emergencia
    # Pivote en el suelo (0, 0, 0)
    # =========================================================================
    bpy.ops.mesh.primitive_cylinder_add(vertices=18, radius=0.48, depth=0.14, location=(0, 0.05, 0.07))
    mound = bpy.context.object
    mound.name = "Mound"
    mound.scale = (1.05, 1.12, 1.0)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    mound.data.materials.append(materials["DarkEarth"])

    # Terrones orgánicos de tierra alrededor del cráter
    dirt_clumps = []
    for ang, dist, bz in [
        (0.1, 0.38, 0.09), (0.9, 0.40, 0.10), (1.8, 0.39, 0.09),
        (2.7, 0.37, 0.08), (3.6, 0.39, 0.10), (4.5, 0.40, 0.09), (5.4, 0.38, 0.08)
    ]:
        bx = dist * math.cos(ang)
        by = 0.05 + dist * math.sin(ang)
        bpy.ops.mesh.primitive_uv_sphere_add(segments=6, ring_count=4, radius=0.12, location=(bx, by, bz))
        clump = bpy.context.object
        clump.data.materials.append(materials["DarkEarth"])
        dirt_clumps.append(clump)

    bpy.context.view_layer.objects.active = mound
    for c in dirt_clumps:
        c.select_set(True)
    mound.select_set(True)
    bpy.ops.object.join()
    mound_obj = bpy.context.object
    set_pivot(mound_obj, (0.0, 0.0, 0.0))

    # Suavizado de todas las partes
    parts = [body_obj, head_obj, snout_obj, arm_l_obj, arm_r_obj, mound_obj]
    for p in parts:
        bpy.context.view_layer.objects.active = p
        p.select_set(True)
        bpy.ops.object.shade_smooth()

    return parts


def setup_preview_scene(parts):
    scene = bpy.context.scene
    preview_col = bpy.data.collections.new("Preview")
    scene.collection.children.link(preview_col)

    # Suelo circular oscuro
    mat_floor = bpy.data.materials.new("PreviewFloor")
    mat_floor.use_nodes = True
    bsdf = mat_floor.node_tree.nodes.get("Principled BSDF")
    if bsdf:
        bsdf.inputs["Base Color"].default_value = (0.13, 0.11, 0.15, 1.0)
        bsdf.inputs["Roughness"].default_value = 0.85
    mat_floor.diffuse_color = (0.13, 0.11, 0.15, 1.0)

    bpy.ops.mesh.primitive_cylinder_add(vertices=24, radius=0.9, depth=0.02, location=(0, 0, -0.01))
    floor_obj = bpy.context.object
    floor_obj.name = "Floor"
    floor_obj.data.materials.append(mat_floor)

    # Luces dramáticas estilo render cinemático de la referencia
    sun_key = bpy.data.lights.new("SunKey", type='SUN')
    sun_key.energy = 3.6
    sun_key_obj = bpy.data.objects.new("SunKey", sun_key)
    sun_key_obj.rotation_euler = (math.radians(52), 0, math.radians(-32))
    preview_col.objects.link(sun_key_obj)

    sun_fill = bpy.data.lights.new("SunFill", type='SUN')
    sun_fill.energy = 1.7
    sun_fill_obj = bpy.data.objects.new("SunFill", sun_fill)
    sun_fill_obj.rotation_euler = (math.radians(60), 0, math.radians(145))
    preview_col.objects.link(sun_fill_obj)

    sun_top = bpy.data.lights.new("SunTop", type='SUN')
    sun_top.energy = 1.0
    sun_top_obj = bpy.data.objects.new("SunTop", sun_top)
    sun_top_obj.rotation_euler = (0, 0, 0)
    preview_col.objects.link(sun_top_obj)

    scene.render.engine = 'BLENDER_EEVEE'
    scene.render.resolution_x = RENDER_SIZE
    scene.render.resolution_y = RENDER_SIZE
    scene.render.resolution_percentage = 100
    scene.render.film_transparent = False
    scene.render.image_settings.file_format = 'PNG'
    scene.render.image_settings.color_mode = 'RGB'
    scene.view_settings.view_transform = 'Standard'

    target = Vector((0.0, 0.05, 0.28))

    def make_cam(name, loc, target_pt, ortho=False, ortho_scale=1.4, fov_deg=55.0):
        cdata = bpy.data.cameras.new(name)
        if ortho:
            cdata.type = 'ORTHO'
            cdata.ortho_scale = ortho_scale
        else:
            cdata.type = 'PERSP'
            cdata.angle = math.radians(fov_deg)
        cobj = bpy.data.objects.new(name, cdata)
        cobj.location = loc
        direction = target_pt - Vector(loc)
        rot_quat = direction.to_track_quat('-Z', 'Y')
        cobj.rotation_euler = rot_quat.to_euler()
        preview_col.objects.link(cobj)
        return cobj

    cam_front = make_cam("CamFront", (0.0, 2.8, 0.38), target, ortho=True, ortho_scale=1.35)
    dist = 2.6
    pitch_rad = math.radians(60)
    cam_game_loc = (0.0, dist * math.cos(pitch_rad), dist * math.sin(pitch_rad) + 0.28)
    cam_game = make_cam("CamGame", cam_game_loc, Vector((0.0, 0.05, 0.22)), ortho=False, fov_deg=50.0)
    cam_side = make_cam("CamSide", (2.8, 0.05, 0.38), target, ortho=True, ortho_scale=1.35)

    return [("1_frente", cam_front), ("2_camara_juego_60deg", cam_game), ("3_perfil", cam_side)]


def render_preview_sheet(cameras, output_path):
    scene = bpy.context.scene
    temp_dir = tempfile.mkdtemp(prefix="digger_preview_")
    panels = []
    for name, cam in cameras:
        scene.camera = cam
        path = os.path.join(temp_dir, f"{name}.png")
        scene.render.filepath = path
        bpy.ops.render.render(write_still=True)
        img = bpy.data.images.load(path)
        w, h = img.size
        pixels = np.empty(w * h * 4, dtype=np.float32)
        img.pixels.foreach_get(pixels)
        panels.append(pixels.reshape(h, w, 4))
        bpy.data.images.remove(img)
        os.remove(path)
    os.rmdir(temp_dir)

    sheet = np.concatenate(panels, axis=1)
    out = bpy.data.images.new("digger_preview", sheet.shape[1], sheet.shape[0], alpha=False)
    out.pixels.foreach_set(sheet.ravel())
    out.filepath_raw = str(output_path)
    out.file_format = 'PNG'
    out.save()
    bpy.data.images.remove(out)


def main():
    print("[digger_enemy] Iniciando modelado oficial del Excavador (Digger)...")
    clean_scene()
    materials = make_materials()
    parts = build_digger_enemy(materials)

    part_names = [p.name for p in parts]
    print(f"[digger_enemy] Partes construidas ({len(parts)}): {', '.join(part_names)}")
    expected_parts = {"Body", "Head", "Snout", "ArmL", "ArmR", "Mound"}
    assert set(part_names) == expected_parts, f"Faltan partes: {set(part_names)} vs {expected_parts}"

    total_tris = 0
    for p in parts:
        p.data.calc_loop_triangles()
        t = len(p.data.loop_triangles)
        total_tris += t
        print(f"  • {p.name}: {t} tris, loc={p.location}, dims={p.dimensions}")
    print(f"[digger_enemy] Triángulos totales: {total_tris} (Límite: 5,000)")
    assert total_tris <= 5000, f"Excede límite de 5,000 tris: {total_tris}"

    # Exportar GLB oficial en assets/models/digger_enemy.glb
    bpy.ops.object.select_all(action='DESELECT')
    for p in parts:
        p.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]

    bpy.ops.export_scene.gltf(
        filepath=str(GLB_PATH),
        export_format='GLB',
        use_selection=True,
        export_yup=True,
        export_apply=False,
    )
    print(f"[digger_enemy] Exportado GLB oficial -> {GLB_PATH}")

    # Renderizar vista previa de 3 paneles
    cameras = setup_preview_scene(parts)
    render_preview_sheet(cameras, PREVIEW_PATH)
    print(f"[digger_enemy] Vista previa (3 vistas) -> {PREVIEW_PATH}")

    # Guardar archivo .blend oficial en assets/source/blender/digger_enemy.blend
    bpy.ops.wm.save_as_mainfile(filepath=str(BLEND_PATH), compress=True)
    print(f"[digger_enemy] Archivo .blend oficial -> {BLEND_PATH}")

    # Copiar script fuente a assets/source/blender/digger_enemy.py
    this_script = Path(__file__).resolve()
    shutil.copy2(this_script, PY_PATH)
    print(f"[digger_enemy] Script fuente guardado en -> {PY_PATH}")

    # Copiar entrega a la carpeta designada de David: MODELOS JUEGO/digger_enemy/
    shutil.copy2(GLB_PATH, DRIVE_DIR / "digger_enemy.glb")
    shutil.copy2(BLEND_PATH, DRIVE_DIR / "digger_enemy.blend")
    shutil.copy2(PY_PATH, DRIVE_DIR / "digger_enemy.py")
    shutil.copy2(PREVIEW_PATH, DRIVE_DIR / "digger_enemy_preview.png")
    print(f"[digger_enemy] Copia guardada en carpeta de David -> {DRIVE_DIR}")

    print("[digger_enemy] ¡Completado con éxito!")


if __name__ == "__main__":
    main()
