"""Generador oficial de runner_enemy (Huidor) para LULLABY:
Referencia visual: Conejito de peluche amarillo mantequilla encogido de miedo,
con grandes orejas caídas, mirada nerviosa/llorosa, patas dobladas y cola de pompón esponjosa.

Partes separadas para animación en Godot (7 partes oficiales):
  - Body: Torso encogido con bracitos apretados contra el pecho
  - Head: Cabeza tierna e inquieta con ojos llorosos y nariz rosada
  - EarL: Oreja larga caída izquierda, pivote en la base de la cabeza
  - EarR: Oreja larga caída derecha, pivote en la base de la cabeza
  - LegL: Pata trasera izquierda doblada para saltos rápidos, pivote en cadera
  - LegR: Pata trasera derecha doblada para saltos rápidos, pivote en cadera
  - Tail: Pompón esponjoso de lana en la rabadilla trasera (visible desde arriba y atrás)

Reglas técnicas:
  - Huella <= 0.8 x 0.8 m
  - Altura ~0.65 - 0.70 m
  - Triángulos <= 5,000
  - Frente hacia +Y en Blender (queda como -Z en Godot)
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
DRIVE_DIR = Path(r"C:\Users\davin\OneDrive\Documentos\MODELOS JUEGO\runner_enemy")

MODELS_DIR.mkdir(parents=True, exist_ok=True)
SOURCE_DIR.mkdir(parents=True, exist_ok=True)
DRIVE_DIR.mkdir(parents=True, exist_ok=True)

GLB_PATH = MODELS_DIR / "runner_enemy.glb"
BLEND_PATH = SOURCE_DIR / "runner_enemy.blend"
PY_PATH = SOURCE_DIR / "runner_enemy.py"
PREVIEW_PATH = SOURCE_DIR / "runner_enemy_preview.png"

RENDER_SIZE = 800

# Paleta suave de conejo de felpa (sRGB)
PALETTE = {
    "ButterYellow": (0.95, 0.82, 0.48),    # Amarillo mantequilla de peluche
    "PompomCream": (0.98, 0.94, 0.82),     # Crema claro esponjoso
    "EyeWhite": (0.96, 0.96, 0.98),        # Blanco esclerótica y lágrimas
    "EyeDark": (0.16, 0.12, 0.10),         # Pupila oscura y boca
    "PinkNose": (0.90, 0.60, 0.65),        # Nariz y rubor suave
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
                    inp.default_value = 0.25 if name == "EyeWhite" else 0.80
                elif inp.name == "Metallic":
                    inp.default_value = 0.0
                elif inp.name in ("Specular IOR Level", "Specular"):
                    inp.default_value = 0.45 if name == "EyeWhite" else 0.15
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


def build_runner_enemy(materials):
    runner_col = bpy.data.collections.new("RunnerEnemy")
    bpy.context.scene.collection.children.link(runner_col)

    # =========================================================================
    # 1. BODY: Torso de peluche encogido de miedo, con bracitos pegados al pecho
    # =========================================================================
    # Torso encogido / aperado
    bpy.ops.mesh.primitive_uv_sphere_add(segments=12, ring_count=8, radius=0.18, location=(0, -0.04, 0.24))
    body = bpy.context.object
    body.name = "Body"
    body.scale = (0.92, 1.05, 1.15)
    body.rotation_euler = (math.radians(-12), 0, 0)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    body.data.materials.append(materials["ButterYellow"])

    # Bracito izquierdo pegado al pecho (-X)
    bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=0.042, depth=0.16, location=(-0.08, 0.08, 0.28))
    arm_l = bpy.context.object
    arm_l.rotation_euler = (math.radians(-45), math.radians(-25), math.radians(20))
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
    arm_l.data.materials.append(materials["ButterYellow"])

    # Bracito derecho pegado al pecho (+X)
    bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=0.042, depth=0.16, location=(0.08, 0.08, 0.28))
    arm_r = bpy.context.object
    arm_r.rotation_euler = (math.radians(-45), math.radians(25), math.radians(-20))
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
    arm_r.data.materials.append(materials["ButterYellow"])

    # Manitas juntas temblorosas
    bpy.ops.mesh.primitive_uv_sphere_add(segments=6, ring_count=4, radius=0.038, location=(0, 0.14, 0.30))
    paws = bpy.context.object
    paws.scale = (1.4, 0.8, 0.7)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    paws.data.materials.append(materials["ButterYellow"])

    # Unir brazos al Body
    bpy.context.view_layer.objects.active = body
    for p in [arm_l, arm_r, paws]:
        p.select_set(True)
    body.select_set(True)
    bpy.ops.object.join()
    body_obj = bpy.context.object
    set_pivot(body_obj, (0.0, 0.0, 0.0))

    # =========================================================================
    # 2. HEAD: Cabeza asustadiza con ojos grandes y llorosos (+Y al frente)
    # Pivote en el cuello (0.0, 0.02, 0.40)
    # =========================================================================
    bpy.ops.mesh.primitive_uv_sphere_add(segments=12, ring_count=8, radius=0.16, location=(0, 0.04, 0.48))
    head = bpy.context.object
    head.name = "Head"
    head.scale = (1.05, 1.08, 1.0)
    head.rotation_euler = (math.radians(-8), 0, 0)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    head.data.materials.append(materials["ButterYellow"])

    # Hocico pequeño de conejo
    bpy.ops.mesh.primitive_uv_sphere_add(segments=8, ring_count=6, radius=0.055, location=(0, 0.16, 0.45))
    snout = bpy.context.object
    snout.scale = (1.05, 0.8, 0.75)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    snout.data.materials.append(materials["ButterYellow"])

    # Naricita rosada
    bpy.ops.mesh.primitive_uv_sphere_add(segments=6, ring_count=4, radius=0.018, location=(0, 0.20, 0.46))
    nose = bpy.context.object
    nose.data.materials.append(materials["PinkNose"])

    # Ojo Izquierdo (-X): grande, asustado y con lagrimita
    bpy.ops.mesh.primitive_uv_sphere_add(segments=8, ring_count=6, radius=0.048, location=(-0.08, 0.13, 0.51))
    sclera_l = bpy.context.object
    sclera_l.scale = (0.75, 1.0, 1.1)
    sclera_l.rotation_euler = (0, math.radians(-15), 0)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    sclera_l.data.materials.append(materials["EyeWhite"])

    bpy.ops.mesh.primitive_uv_sphere_add(segments=6, ring_count=4, radius=0.026, location=(-0.09, 0.16, 0.51))
    pupil_l = bpy.context.object
    pupil_l.data.materials.append(materials["EyeDark"])

    # Lagrimita saliente bajo el ojo izquierdo
    bpy.ops.mesh.primitive_uv_sphere_add(segments=4, ring_count=3, radius=0.012, location=(-0.095, 0.155, 0.465))
    tear_l = bpy.context.object
    tear_l.data.materials.append(materials["EyeWhite"])

    # Ojo Derecho (+X): grande, asustado y con lagrimita
    bpy.ops.mesh.primitive_uv_sphere_add(segments=8, ring_count=6, radius=0.048, location=(0.08, 0.13, 0.51))
    sclera_r = bpy.context.object
    sclera_r.scale = (0.75, 1.0, 1.1)
    sclera_r.rotation_euler = (0, math.radians(15), 0)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    sclera_r.data.materials.append(materials["EyeWhite"])

    bpy.ops.mesh.primitive_uv_sphere_add(segments=6, ring_count=4, radius=0.026, location=(0.088, 0.16, 0.51))
    pupil_r = bpy.context.object
    pupil_r.data.materials.append(materials["EyeDark"])

    bpy.ops.mesh.primitive_uv_sphere_add(segments=4, ring_count=3, radius=0.012, location=(0.095, 0.155, 0.465))
    tear_r = bpy.context.object
    tear_r.data.materials.append(materials["EyeWhite"])

    # Cejas tristes inclinadas
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(-0.075, 0.14, 0.57))
    brow_l = bpy.context.object
    brow_l.scale = (0.05, 0.012, 0.012)
    brow_l.rotation_euler = (0, 0, math.radians(-30))
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    brow_l.data.materials.append(materials["EyeDark"])

    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0.075, 0.14, 0.57))
    brow_r = bpy.context.object
    brow_r.scale = (0.05, 0.012, 0.012)
    brow_r.rotation_euler = (0, 0, math.radians(30))
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    brow_r.data.materials.append(materials["EyeDark"])

    # Unir componentes faciales a Head
    bpy.context.view_layer.objects.active = head
    for p in [snout, nose, sclera_l, pupil_l, tear_l, sclera_r, pupil_r, tear_r, brow_l, brow_r]:
        p.select_set(True)
    head.select_set(True)
    bpy.ops.object.join()
    head_obj = bpy.context.object
    set_pivot(head_obj, (0.0, 0.02, 0.40))

    # =========================================================================
    # 3. EAR_L: Oreja larga caída izquierda de felpa (-X)
    # Articulación en la base de la cabeza (-0.11, 0.0, 0.58)
    # =========================================================================
    pivot_ear_l = (-0.11, 0.0, 0.58)
    bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=0.065, depth=0.34, location=(-0.18, -0.08, 0.44))
    ear_l = bpy.context.object
    ear_l.name = "EarL"
    ear_l.scale = (0.45, 1.0, 1.0)
    # Caída hacia atrás y al costado
    ear_l.rotation_euler = (math.radians(-35), math.radians(-25), math.radians(15))
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    ear_l.data.materials.append(materials["ButterYellow"])
    set_pivot(ear_l, pivot_ear_l)

    # =========================================================================
    # 4. EAR_R: Oreja larga caída derecha de felpa (+X)
    # Articulación en la base de la cabeza (0.11, 0.0, 0.58)
    # =========================================================================
    pivot_ear_r = (0.11, 0.0, 0.58)
    bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=0.065, depth=0.34, location=(0.18, -0.08, 0.44))
    ear_r = bpy.context.object
    ear_r.name = "EarR"
    ear_r.scale = (0.45, 1.0, 1.0)
    ear_r.rotation_euler = (math.radians(-35), math.radians(25), math.radians(-15))
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    ear_r.data.materials.append(materials["ButterYellow"])
    set_pivot(ear_r, pivot_ear_r)

    # =========================================================================
    # 5. LEG_L: Pata trasera izquierda doblada para saltos/huidas (-X)
    # Articulación en la cadera (-0.14, -0.06, 0.18)
    # =========================================================================
    pivot_leg_l = (-0.14, -0.06, 0.18)
    # Muslo redondeado
    bpy.ops.mesh.primitive_uv_sphere_add(segments=8, ring_count=6, radius=0.085, location=(-0.14, -0.04, 0.14))
    thigh_l = bpy.context.object
    thigh_l.name = "LegL"
    thigh_l.scale = (0.8, 1.25, 0.95)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    thigh_l.data.materials.append(materials["ButterYellow"])

    # Pie alargado de conejo asentado en el suelo
    bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=0.048, depth=0.18, location=(-0.14, 0.08, 0.05))
    foot_l = bpy.context.object
    foot_l.rotation_euler = (math.radians(85), 0, 0)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
    foot_l.data.materials.append(materials["ButterYellow"])

    bpy.context.view_layer.objects.active = thigh_l
    foot_l.select_set(True)
    thigh_l.select_set(True)
    bpy.ops.object.join()
    leg_l_obj = bpy.context.object
    set_pivot(leg_l_obj, pivot_leg_l)

    # =========================================================================
    # 6. LEG_R: Pata trasera derecha doblada (+X)
    # Articulación en la cadera (0.14, -0.06, 0.18)
    # =========================================================================
    pivot_leg_r = (0.14, -0.06, 0.18)
    bpy.ops.mesh.primitive_uv_sphere_add(segments=8, ring_count=6, radius=0.085, location=(0.14, -0.04, 0.14))
    thigh_r = bpy.context.object
    thigh_r.name = "LegR"
    thigh_r.scale = (0.8, 1.25, 0.95)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    thigh_r.data.materials.append(materials["ButterYellow"])

    bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=0.048, depth=0.18, location=(0.14, 0.08, 0.05))
    foot_r = bpy.context.object
    foot_r.rotation_euler = (math.radians(85), 0, 0)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
    foot_r.data.materials.append(materials["ButterYellow"])

    bpy.context.view_layer.objects.active = thigh_r
    foot_r.select_set(True)
    thigh_r.select_set(True)
    bpy.ops.object.join()
    leg_r_obj = bpy.context.object
    set_pivot(leg_r_obj, pivot_leg_r)

    # =========================================================================
    # 7. TAIL: Cola de pompón esponjoso de lana en la rabadilla (-Y)
    # Articulación en la base de la rabadilla (0.0, -0.22, 0.14)
    # =========================================================================
    pivot_tail = (0.0, -0.20, 0.14)
    # Centro del pompón
    bpy.ops.mesh.primitive_uv_sphere_add(segments=10, ring_count=7, radius=0.075, location=(0, -0.25, 0.14))
    tail_core = bpy.context.object
    tail_core.name = "Tail"
    tail_core.data.materials.append(materials["PompomCream"])

    # Bolitas de lana extra para dar la apariencia rizada de pompón de la imagen
    for ang, dz in [(0, 0.04), (math.pi/2, -0.03), (math.pi, 0.03), (-math.pi/2, -0.02)]:
        bx = 0.035 * math.cos(ang)
        by = -0.26 + 0.025 * math.sin(ang)
        bpy.ops.mesh.primitive_uv_sphere_add(segments=6, ring_count=4, radius=0.035, location=(bx, by, 0.14 + dz))
        fluff = bpy.context.object
        fluff.data.materials.append(materials["PompomCream"])
        bpy.context.view_layer.objects.active = tail_core
        fluff.select_set(True)
        tail_core.select_set(True)
        bpy.ops.object.join()

    tail_obj = bpy.context.object
    set_pivot(tail_obj, pivot_tail)

    # Suavizado de todas las partes
    parts = [body_obj, head_obj, ear_l, ear_r, leg_l_obj, leg_r_obj, tail_obj]
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

    bpy.ops.mesh.primitive_cylinder_add(vertices=24, radius=0.8, depth=0.02, location=(0, 0, -0.01))
    floor_obj = bpy.context.object
    floor_obj.name = "Floor"
    floor_obj.data.materials.append(mat_floor)

    # Luces
    sun_key = bpy.data.lights.new("SunKey", type='SUN')
    sun_key.energy = 3.5
    sun_key_obj = bpy.data.objects.new("SunKey", sun_key)
    sun_key_obj.rotation_euler = (math.radians(52), 0, math.radians(-32))
    preview_col.objects.link(sun_key_obj)

    sun_fill = bpy.data.lights.new("SunFill", type='SUN')
    sun_fill.energy = 1.6
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

    target = Vector((0.0, 0.0, 0.35))

    def make_cam(name, loc, target_pt, ortho=False, ortho_scale=1.35, fov_deg=55.0):
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

    cam_front = make_cam("CamFront", (0.0, 2.5, 0.35), target, ortho=True, ortho_scale=1.2)
    dist = 2.4
    pitch_rad = math.radians(60)
    cam_game_loc = (0.0, dist * math.cos(pitch_rad), dist * math.sin(pitch_rad) + 0.25)
    cam_game = make_cam("CamGame", cam_game_loc, Vector((0.0, 0.0, 0.30)), ortho=False, fov_deg=50.0)
    cam_side = make_cam("CamSide", (2.5, 0.0, 0.35), target, ortho=True, ortho_scale=1.2)

    return [("1_frente", cam_front), ("2_camara_juego_60deg", cam_game), ("3_perfil", cam_side)]


def render_preview_sheet(cameras, output_path):
    scene = bpy.context.scene
    temp_dir = tempfile.mkdtemp(prefix="runner_preview_")
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
    out = bpy.data.images.new("runner_preview", sheet.shape[1], sheet.shape[0], alpha=False)
    out.pixels.foreach_set(sheet.ravel())
    out.filepath_raw = str(output_path)
    out.file_format = 'PNG'
    out.save()
    bpy.data.images.remove(out)


def main():
    print("[runner_enemy] Iniciando modelado oficial del Huidor (Runner)...")
    clean_scene()
    materials = make_materials()
    parts = build_runner_enemy(materials)

    part_names = [p.name for p in parts]
    print(f"[runner_enemy] Partes construidas ({len(parts)}): {', '.join(part_names)}")
    expected_parts = {"Body", "Head", "EarL", "EarR", "LegL", "LegR", "Tail"}
    assert set(part_names) == expected_parts, f"Faltan partes: {set(part_names)} vs {expected_parts}"

    total_tris = 0
    for p in parts:
        p.data.calc_loop_triangles()
        t = len(p.data.loop_triangles)
        total_tris += t
        print(f"  • {p.name}: {t} tris, loc={p.location}, dims={p.dimensions}")
    print(f"[runner_enemy] Triángulos totales: {total_tris} (Límite: 5,000)")
    assert total_tris <= 5000, f"Excede límite de 5,000 tris: {total_tris}"

    # Exportar GLB oficial en assets/models/runner_enemy.glb
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
    print(f"[runner_enemy] Exportado GLB oficial -> {GLB_PATH}")

    # Renderizar vista previa de 3 paneles
    cameras = setup_preview_scene(parts)
    render_preview_sheet(cameras, PREVIEW_PATH)
    print(f"[runner_enemy] Vista previa (3 vistas) -> {PREVIEW_PATH}")

    # Guardar archivo .blend oficial en assets/source/blender/runner_enemy.blend
    bpy.ops.wm.save_as_mainfile(filepath=str(BLEND_PATH), compress=True)
    print(f"[runner_enemy] Archivo .blend oficial -> {BLEND_PATH}")

    # Copiar script fuente a assets/source/blender/runner_enemy.py
    this_script = Path(__file__).resolve()
    shutil.copy2(this_script, PY_PATH)
    print(f"[runner_enemy] Script fuente guardado en -> {PY_PATH}")

    # Copiar entrega a la carpeta designada de David: MODELOS JUEGO/runner_enemy/
    shutil.copy2(GLB_PATH, DRIVE_DIR / "runner_enemy.glb")
    shutil.copy2(BLEND_PATH, DRIVE_DIR / "runner_enemy.blend")
    shutil.copy2(PY_PATH, DRIVE_DIR / "runner_enemy.py")
    shutil.copy2(PREVIEW_PATH, DRIVE_DIR / "runner_enemy_preview.png")
    print(f"[runner_enemy] Copia guardada en carpeta de David -> {DRIVE_DIR}")

    print("[runner_enemy] ¡Completado con éxito!")


if __name__ == "__main__":
    main()
