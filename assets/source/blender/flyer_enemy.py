"""Generador oficial de flyer_enemy (Volador) para LULLABY:
Referencia visual: Cometa / Estrella fugaz de móvil de cuna hecha de tela/fieltro verde azulado,
con cara enfadada/pícara y alas de cintas / estelas de tela que fluyen hacia atrás.

Partes separadas para animación en Godot:
  - Body: Estrella de peluche verde azulado con ojos angulares y boca fruncida
  - WingL: Grupo de cintas / aletas de tela del lado izquierdo (-X), pivote en la punta izquierda
  - WingR: Grupo de cintas / aletas de tela del lado derecho (+X), pivote en la punta derecha

Reglas técnicas:
  - Huella <= 1.0 x 1.0 m
  - Altura ~0.8 m, flotando sobre el suelo (origen z=0)
  - Triángulos <= 5,000
  - Frente hacia +Y en Blender (importa hacia -Z en Godot)
  - Tres vistas en preview PNG (De frente | Cámara del juego a 60° | Perfil)
  - Sin archivos residuales (.blend1)
"""

import math
import os
import shutil
import tempfile
from pathlib import Path

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector

REPO_ROOT = Path(r"C:\LULLABY")
MODELS_DIR = REPO_ROOT / "assets" / "models"
SOURCE_DIR = REPO_ROOT / "assets" / "source" / "blender"
DRIVE_DIR = Path(r"C:\Users\davin\OneDrive\Documentos\MODELOS JUEGO\flyer_enemy")

MODELS_DIR.mkdir(parents=True, exist_ok=True)
SOURCE_DIR.mkdir(parents=True, exist_ok=True)
DRIVE_DIR.mkdir(parents=True, exist_ok=True)

GLB_PATH = MODELS_DIR / "flyer_enemy.glb"
BLEND_PATH = SOURCE_DIR / "flyer_enemy.blend"
PY_PATH = SOURCE_DIR / "flyer_enemy.py"
PREVIEW_PATH = SOURCE_DIR / "flyer_enemy_preview.png"

RENDER_SIZE = 800

# Paleta verde azulado (Teal / Cyan oscuro de móvil de cuna)
PALETTE = {
    "BodyTeal": (0.16, 0.44, 0.40),       # Verde azulado profundo de fieltro
    "StreamerMint": (0.42, 0.68, 0.62),   # Menta suave para las cintas
    "EyesGlow": (0.85, 0.98, 0.95),       # Ojos claros brillantes
    "MouthDark": (0.08, 0.18, 0.16),      # Boca y bordes de ojos oscuros
    "Stitch": (0.24, 0.55, 0.50),         # Detalle de costuras
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
                    inp.default_value = 0.30 if name == "EyesGlow" else 0.75
                elif inp.name == "Metallic":
                    inp.default_value = 0.0
                elif inp.name in ("Specular IOR Level", "Specular"):
                    inp.default_value = 0.50 if name == "EyesGlow" else 0.20
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


def create_star_mesh(name: str, r_outer: float, r_inner: float, thickness: float, z_center: float) -> bpy.types.Object:
    """Crea una estrella acolchada de 5 puntas estilo almohadón de cuna."""
    mesh = bpy.data.meshes.new(name)
    bm = bmesh.new()

    # Puntos 2D de la estrella (plano XZ, grosor en Y)
    num_points = 5
    pts_front = []
    pts_back = []
    y_front = thickness / 2.0
    y_back = -thickness / 2.0

    for i in range(num_points * 2):
        angle = math.pi / 2.0 + i * math.pi / num_points
        r = r_outer if (i % 2 == 0) else r_inner
        x = r * math.cos(angle)
        z = z_center + r * math.sin(angle)
        pts_front.append(bm.verts.new((x, y_front * 0.7, z)))
        pts_back.append(bm.verts.new((x, y_back * 0.7, z)))

    # Vértices centrales para dar volumen abombado al almohadón
    center_front = bm.verts.new((0.0, y_front, z_center))
    center_back = bm.verts.new((0.0, y_back, z_center))

    # Caras frontales
    for i in range(num_points * 2):
        next_i = (i + 1) % (num_points * 2)
        bm.faces.new([center_front, pts_front[i], pts_front[next_i]])

    # Caras traseras
    for i in range(num_points * 2):
        next_i = (i + 1) % (num_points * 2)
        bm.faces.new([center_back, pts_back[next_i], pts_back[i]])

    # Caras laterales del borde
    for i in range(num_points * 2):
        next_i = (i + 1) % (num_points * 2)
        bm.faces.new([pts_front[i], pts_back[i], pts_back[next_i], pts_front[next_i]])

    bm.to_mesh(mesh)
    bm.free()

    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)

    # Subdivisión y biselado suave para apariencia de peluche de fieltro
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    mod_sub = obj.modifiers.new(name="Subsurf", type='SUBSURF')
    mod_sub.levels = 1
    mod_sub.render_levels = 1
    bpy.ops.object.modifier_apply(modifier=mod_sub.name)

    return obj


def build_flyer_enemy(materials):
    flyer_col = bpy.data.collections.new("FlyerEnemy")
    bpy.context.scene.collection.children.link(flyer_col)

    z_mid = 0.55  # Altura de flotación

    # =========================================================================
    # 1. BODY: Estrella acolchada de felpa verde azulado con cara enfadada
    # =========================================================================
    body = create_star_mesh("Body", r_outer=0.36, r_inner=0.17, thickness=0.14, z_center=z_mid)
    body.data.materials.append(materials["BodyTeal"])

    # Cara: Ojos enfadados/inclinados estilo LULLABY hacia +Y (frente)
    # Fondo/borde oscuro del ojo izquierdo (-X)
    bpy.ops.mesh.primitive_cylinder_add(vertices=10, radius=0.065, depth=0.02, location=(-0.11, 0.08, z_mid + 0.04))
    eye_bg_l = bpy.context.object
    eye_bg_l.rotation_euler = (math.radians(90), math.radians(20), 0)
    eye_bg_l.scale = (1.0, 0.65, 1.0)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    eye_bg_l.data.materials.append(materials["MouthDark"])

    # Pupila luminosa izquierda
    bpy.ops.mesh.primitive_uv_sphere_add(segments=8, ring_count=6, radius=0.045, location=(-0.11, 0.09, z_mid + 0.04))
    eye_l = bpy.context.object
    eye_l.scale = (1.0, 0.35, 0.75)
    eye_l.rotation_euler = (0, 0, math.radians(20))
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    eye_l.data.materials.append(materials["EyesGlow"])

    # Fondo/borde oscuro del ojo derecho (+X)
    bpy.ops.mesh.primitive_cylinder_add(vertices=10, radius=0.065, depth=0.02, location=(0.11, 0.08, z_mid + 0.04))
    eye_bg_r = bpy.context.object
    eye_bg_r.rotation_euler = (math.radians(90), math.radians(-20), 0)
    eye_bg_r.scale = (1.0, 0.65, 1.0)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    eye_bg_r.data.materials.append(materials["MouthDark"])

    # Pupila luminosa derecha
    bpy.ops.mesh.primitive_uv_sphere_add(segments=8, ring_count=6, radius=0.045, location=(0.11, 0.09, z_mid + 0.04))
    eye_r = bpy.context.object
    eye_r.scale = (1.0, 0.35, 0.75)
    eye_r.rotation_euler = (0, 0, math.radians(-20))
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    eye_r.data.materials.append(materials["EyesGlow"])

    # Ceño fruncido bordado (línea inclinada)
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(-0.11, 0.085, z_mid + 0.085))
    brow_l = bpy.context.object
    brow_l.scale = (0.09, 0.015, 0.015)
    brow_l.rotation_euler = (0, 0, math.radians(25))
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    brow_l.data.materials.append(materials["MouthDark"])

    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0.11, 0.085, z_mid + 0.085))
    brow_r = bpy.context.object
    brow_r.scale = (0.09, 0.015, 0.015)
    brow_r.rotation_euler = (0, 0, math.radians(-25))
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    brow_r.data.materials.append(materials["MouthDark"])

    # Boca fruncida / quejosa bordada
    bpy.ops.mesh.primitive_torus_add(major_segments=10, minor_segments=4, major_radius=0.05, minor_radius=0.012, location=(0.0, 0.082, z_mid - 0.065))
    mouth = bpy.context.object
    mouth.rotation_euler = (math.radians(90), 0, 0)
    mouth.scale = (1.1, 0.5, 1.0)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    mouth.data.materials.append(materials["MouthDark"])

    # Unir elementos faciales a Body
    bpy.context.view_layer.objects.active = body
    for p in [eye_bg_l, eye_l, eye_bg_r, eye_r, brow_l, brow_r, mouth]:
        p.select_set(True)
    body.select_set(True)
    bpy.ops.object.join()
    body_obj = bpy.context.object

    # Pivote de Body en el suelo (0, 0, 0)
    set_pivot(body_obj, (0.0, 0.0, 0.0))

    # =========================================================================
    # 2. WING_L: Cintas / estelas de tela que fluyen hacia atrás desde el lado izquierdo (-X)
    # Articulación / pivote en la punta lateral izquierda (-0.34, 0.0, z_mid + 0.08)
    # =========================================================================
    pivot_l = (-0.32, 0.0, z_mid + 0.08)
    ribbons_l = []

    for idx, (dy, dz, ang_x, ang_z, length) in enumerate([
        (-0.25, 0.04, -15, 25, 0.38),
        (-0.28, -0.02, -10, 32, 0.44),
        (-0.22, -0.08, -5, 38, 0.35),
    ]):
        bpy.ops.mesh.primitive_cube_add(size=1.0, location=(pivot_l[0] - length * 0.45, dy, pivot_l[2] + dz))
        rib = bpy.context.object
        rib.scale = (length, 0.012, 0.055)
        rib.rotation_euler = (math.radians(ang_x), math.radians(12), math.radians(ang_z))
        bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
        rib.data.materials.append(materials["StreamerMint"])
        ribbons_l.append(rib)

    bpy.context.view_layer.objects.active = ribbons_l[0]
    ribbons_l[0].name = "WingL"
    for r in ribbons_l[1:]:
        r.select_set(True)
    ribbons_l[0].select_set(True)
    bpy.ops.object.join()
    wing_l_obj = bpy.context.object
    set_pivot(wing_l_obj, pivot_l)

    # =========================================================================
    # 3. WING_R: Cintas / estelas de tela que fluyen hacia atrás desde el lado derecho (+X)
    # Articulación / pivote en la punta lateral derecha (+0.32, 0.0, z_mid + 0.08)
    # =========================================================================
    pivot_r = (0.32, 0.0, z_mid + 0.08)
    ribbons_r = []

    for idx, (dy, dz, ang_x, ang_z, length) in enumerate([
        (-0.25, 0.04, -15, -25, 0.38),
        (-0.28, -0.02, -10, -32, 0.44),
        (-0.22, -0.08, -5, -38, 0.35),
    ]):
        bpy.ops.mesh.primitive_cube_add(size=1.0, location=(pivot_r[0] + length * 0.45, dy, pivot_r[2] + dz))
        rib = bpy.context.object
        rib.scale = (length, 0.012, 0.055)
        rib.rotation_euler = (math.radians(ang_x), math.radians(-12), math.radians(ang_z))
        bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
        rib.data.materials.append(materials["StreamerMint"])
        ribbons_r.append(rib)

    bpy.context.view_layer.objects.active = ribbons_r[0]
    ribbons_r[0].name = "WingR"
    for r in ribbons_r[1:]:
        r.select_set(True)
    ribbons_r[0].select_set(True)
    bpy.ops.object.join()
    wing_r_obj = bpy.context.object
    set_pivot(wing_r_obj, pivot_r)

    # Suavizado de las partes
    parts = [body_obj, wing_l_obj, wing_r_obj]
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
        bsdf.inputs["Base Color"].default_value = (0.12, 0.10, 0.14, 1.0)
        bsdf.inputs["Roughness"].default_value = 0.85
    mat_floor.diffuse_color = (0.12, 0.10, 0.14, 1.0)

    bpy.ops.mesh.primitive_cylinder_add(vertices=24, radius=0.9, depth=0.02, location=(0, 0, -0.01))
    floor_obj = bpy.context.object
    floor_obj.name = "Floor"
    floor_obj.data.materials.append(mat_floor)

    # Luces
    sun_key = bpy.data.lights.new("SunKey", type='SUN')
    sun_key.energy = 3.5
    sun_key_obj = bpy.data.objects.new("SunKey", sun_key)
    sun_key_obj.rotation_euler = (math.radians(50), 0, math.radians(-30))
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

    target = Vector((0.0, 0.0, 0.55))

    def make_cam(name, loc, target_pt, ortho=False, ortho_scale=1.65, fov_deg=55.0):
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

    cam_front = make_cam("CamFront", (0.0, 3.2, 0.55), target, ortho=True, ortho_scale=1.6)
    dist = 2.8
    pitch_rad = math.radians(60)
    cam_game_loc = (0.0, dist * math.cos(pitch_rad), dist * math.sin(pitch_rad) + 0.35)
    cam_game = make_cam("CamGame", cam_game_loc, Vector((0.0, 0.0, 0.50)), ortho=False, fov_deg=55.0)
    cam_side = make_cam("CamSide", (3.2, 0.0, 0.55), target, ortho=True, ortho_scale=1.6)

    return [("1_frente", cam_front), ("2_camara_juego_60deg", cam_game), ("3_perfil", cam_side)]


def render_preview_sheet(cameras, output_path):
    scene = bpy.context.scene
    temp_dir = tempfile.mkdtemp(prefix="flyer_preview_")
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
    out = bpy.data.images.new("flyer_preview", sheet.shape[1], sheet.shape[0], alpha=False)
    out.pixels.foreach_set(sheet.ravel())
    out.filepath_raw = str(output_path)
    out.file_format = 'PNG'
    out.save()
    bpy.data.images.remove(out)


def main():
    print("[flyer_enemy] Iniciando modelado oficial del Volador (Flyer)...")
    clean_scene()
    materials = make_materials()
    parts = build_flyer_enemy(materials)

    part_names = [p.name for p in parts]
    print(f"[flyer_enemy] Partes construidas ({len(parts)}): {', '.join(part_names)}")
    expected_parts = {"Body", "WingL", "WingR"}
    assert set(part_names) == expected_parts, f"Faltan partes: {set(part_names)} vs {expected_parts}"

    # Medición de polígonos
    total_tris = 0
    for p in parts:
        p.data.calc_loop_triangles()
        t = len(p.data.loop_triangles)
        total_tris += t
        print(f"  • {p.name}: {t} tris, loc={p.location}, dims={p.dimensions}")
    print(f"[flyer_enemy] Triángulos totales: {total_tris} (Límite: 5,000)")
    assert total_tris <= 5000, f"Excede límite de 5,000 tris: {total_tris}"

    # Exportar GLB oficial en assets/models/flyer_enemy.glb
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
    print(f"[flyer_enemy] Exportado GLB oficial -> {GLB_PATH}")

    # Renderizar vista previa de 3 paneles
    cameras = setup_preview_scene(parts)
    render_preview_sheet(cameras, PREVIEW_PATH)
    print(f"[flyer_enemy] Vista previa (3 vistas) -> {PREVIEW_PATH}")

    # Guardar archivo .blend oficial en assets/source/blender/flyer_enemy.blend
    bpy.ops.wm.save_as_mainfile(filepath=str(BLEND_PATH), compress=True)
    print(f"[flyer_enemy] Archivo .blend oficial -> {BLEND_PATH}")

    # Copiar script fuente a assets/source/blender/flyer_enemy.py
    this_script = Path(__file__).resolve()
    shutil.copy2(this_script, PY_PATH)
    print(f"[flyer_enemy] Script fuente guardado en -> {PY_PATH}")

    # Copiar entrega a la carpeta designada de David: MODELOS JUEGO/flyer_enemy/
    shutil.copy2(GLB_PATH, DRIVE_DIR / "flyer_enemy.glb")
    shutil.copy2(BLEND_PATH, DRIVE_DIR / "flyer_enemy.blend")
    shutil.copy2(PY_PATH, DRIVE_DIR / "flyer_enemy.py")
    shutil.copy2(PREVIEW_PATH, DRIVE_DIR / "flyer_enemy_preview.png")
    print(f"[flyer_enemy] Copia guardada en carpeta de David -> {DRIVE_DIR}")

    print("[flyer_enemy] ¡Completado con éxito!")


if __name__ == "__main__":
    main()
