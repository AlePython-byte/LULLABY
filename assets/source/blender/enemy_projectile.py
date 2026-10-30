"""Generador oficial de enemy_projectile.glb (Proyectil del Tirador) para LULLABY:
Especificaciones del plan:
  - Proyectil del enemigo tirador: proyectil tétrico estilizado (esfera espinosa tóxica de pesadilla infantil)
  - Presupuesto: <= 150 triángulos (estricto)
  - Origen en la base (Z = 0)
  - Frente hacia +Y en Blender (importa hacia -Z en Godot)
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
DRIVE_DIR = Path(r"C:\Users\davin\OneDrive\Documentos\MODELOS JUEGO\enemy_projectile")
ARTIFACTS_DIR = Path(r"C:\Users\davin\.gemini\antigravity\brain\61207137-232d-4414-8860-f3fa45906692")

MODELS_DIR.mkdir(parents=True, exist_ok=True)
SOURCE_DIR.mkdir(parents=True, exist_ok=True)
DRIVE_DIR.mkdir(parents=True, exist_ok=True)

GLB_PATH = MODELS_DIR / "enemy_projectile.glb"
BLEND_PATH = SOURCE_DIR / "enemy_projectile.blend"
PY_PATH = SOURCE_DIR / "enemy_projectile.py"
PREVIEW_PATH = SOURCE_DIR / "enemy_projectile_preview.png"
ARTIFACT_PREVIEW = ARTIFACTS_DIR / "enemy_projectile_preview.png"

RENDER_SIZE = 800

# Paleta oscura/tóxica de pesadilla (sRGB)
PALETTE = {
    "SpikeDark": (0.28, 0.12, 0.22),      # Púrpura oscuro tétrico
    "SpikeGlow": (0.78, 0.32, 0.62),      # Puntas espinosas magenta/lila brillante
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
                    inp.default_value = 0.60
                elif inp.name == "Specular IOR Level":
                    inp.default_value = 0.40
        materials[name] = mat
    return materials


def set_smooth(obj):
    for f in obj.data.polygons:
        f.use_smooth = True


def build_enemy_projectile(materials):
    """Construye un proyectil esférico con púas estilizadas de pesadilla (<= 150 tris)."""
    parts = []
    
    # Esfera central tóxica (6 segmentos, 4 anillos = 36 tris)
    center_z = 0.20
    bpy.ops.mesh.primitive_uv_sphere_add(segments=6, ring_count=4, radius=0.14, location=(0, 0, center_z))
    core = bpy.context.active_object
    core.name = "Projectile_Core"
    set_smooth(core)
    core.data.materials.append(materials["SpikeDark"])
    parts.append(core)
    
    # 6 Puntas cónicas de 4 lados (6 x 8 = 48 tris) en las 6 direcciones
    spike_r = 0.035
    spike_depth = 0.12
    spike_dirs = [
        ((0, 0, center_z + 0.17), (0, 0, 0)),                                      # +Z Arriba
        ((0, 0, center_z - 0.17), (math.radians(180), 0, 0)),                      # -Z Abajo
        ((0, 0.17, center_z), (math.radians(90), 0, 0)),                           # +Y Frente
        ((0, -0.17, center_z), (math.radians(-90), 0, 0)),                         # -Y Atrás
        ((0.17, 0, center_z), (0, math.radians(90), 0)),                           # +X Derecha
        ((-0.17, 0, center_z), (0, math.radians(-90), 0)),                         # -X Izquierda
    ]
    for idx, (loc, rot) in enumerate(spike_dirs):
        bpy.ops.mesh.primitive_cone_add(vertices=4, radius1=spike_r, depth=spike_depth, location=loc, rotation=rot)
        sp = bpy.context.active_object
        sp.name = f"Spike_{idx}"
        set_smooth(sp)
        sp.data.materials.append(materials["SpikeGlow"])
        parts.append(sp)
        
    # Unir en un solo objeto limpio "Enemy_Projectile"
    bpy.ops.object.select_all(action='DESELECT')
    for p in parts:
        p.select_set(True)
    bpy.context.view_layer.objects.active = core
    bpy.ops.object.join()
    proj = bpy.context.active_object
    proj.name = "Enemy_Projectile"
    
    # Asegurar origen en el suelo (Z = 0)
    min_z = min(v[2] for v in proj.bound_box)
    proj.location.z -= min_z
    bpy.ops.object.transform_apply(location=True)
    
    return [proj]


def setup_preview_scene(parts):
    light_data = bpy.data.lights.new(name="KeyLight", type='SUN')
    light_data.energy = 4.0
    light_data.color = (0.95, 0.90, 0.95)
    light_obj = bpy.data.objects.new("KeyLight", light_data)
    light_obj.rotation_euler = (math.radians(50), math.radians(20), math.radians(-35))
    bpy.context.collection.objects.link(light_obj)

    fill_data = bpy.data.lights.new(name="FillLight", type='SUN')
    fill_data.energy = 1.6
    fill_data.color = (0.65, 0.35, 0.65)
    fill_obj = bpy.data.objects.new("FillLight", fill_data)
    fill_obj.rotation_euler = (math.radians(-40), math.radians(-30), math.radians(140))
    bpy.context.collection.objects.link(fill_obj)

    rim_data = bpy.data.lights.new(name="RimLight", type='POINT')
    rim_data.energy = 35.0
    rim_data.color = (0.9, 0.5, 0.9)
    rim_obj = bpy.data.objects.new("RimLight", rim_data)
    rim_obj.location = (0, -0.6, 0.7)
    bpy.context.collection.objects.link(rim_obj)

    center = (0.0, 0.0, 0.20)
    dist = 1.1

    cams = []

    # 1. Cámara frontal
    cam_front = bpy.data.cameras.new("Cam_Front")
    cam_front.lens = 50
    o_front = bpy.data.objects.new("Cam_Front_Obj", cam_front)
    o_front.location = (0.0, dist, center[2])
    o_front.rotation_euler = (math.radians(88), 0.0, math.radians(180))
    bpy.context.collection.objects.link(o_front)
    cams.append(("front", o_front))

    # 2. Cámara isométrica 3/4
    cam_iso = bpy.data.cameras.new("Cam_Iso")
    cam_iso.lens = 50
    o_iso = bpy.data.objects.new("Cam_Iso_Obj", cam_iso)
    iso_dist = 1.0
    o_iso.location = (iso_dist * 0.7, iso_dist * 0.7, 0.65)
    direction = Vector(center) - Vector(o_iso.location)
    rot_quat = direction.to_track_quat('-Z', 'Y')
    o_iso.rotation_euler = rot_quat.to_euler()
    bpy.context.collection.objects.link(o_iso)
    cams.append(("isometric", o_iso))

    # 3. Cámara lateral
    cam_side = bpy.data.cameras.new("Cam_Side")
    cam_side.lens = 50
    o_side = bpy.data.objects.new("Cam_Side_Obj", cam_side)
    o_side.location = (dist, 0.0, center[2])
    o_side.rotation_euler = (math.radians(88), 0.0, math.radians(90))
    bpy.context.collection.objects.link(o_side)
    cams.append(("side", o_side))

    return cams


def render_preview_sheet(cameras, output_path):
    scene = bpy.context.scene
    scene.render.resolution_x = RENDER_SIZE
    scene.render.resolution_y = RENDER_SIZE
    scene.render.film_transparent = False

    world = bpy.data.worlds.new("PreviewWorld")
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    if bg:
        bg.inputs["Color"].default_value = (0.08, 0.08, 0.10, 1.0)
        bg.inputs["Strength"].default_value = 1.0
    scene.world = world

    temp_dir = tempfile.mkdtemp()
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
    out = bpy.data.images.new("projectile_preview", sheet.shape[1], sheet.shape[0], alpha=False)
    out.pixels.foreach_set(sheet.ravel())
    out.filepath_raw = str(output_path)
    out.file_format = 'PNG'
    out.save()
    bpy.data.images.remove(out)


def main():
    print("[enemy_projectile] Iniciando modelado oficial del Proyectil del Tirador (enemy_projectile.glb)...")
    clean_scene()
    materials = make_materials()
    parts = build_enemy_projectile(materials)

    part_names = [p.name for p in parts]
    print(f"[enemy_projectile] Partes construidas ({len(parts)}): {', '.join(part_names)}")

    total_tris = 0
    for p in parts:
        p.data.calc_loop_triangles()
        t = len(p.data.loop_triangles)
        total_tris += t
        print(f"  • {p.name}: {t} tris, loc={p.location}, dims={p.dimensions}")
    print(f"[enemy_projectile] Triángulos totales: {total_tris} (Límite: <= 150)")
    assert total_tris <= 150, f"Excede límite de 150 tris: {total_tris}"

    # Exportar GLB oficial en assets/models/enemy_projectile.glb
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
    print(f"[enemy_projectile] Exportado GLB oficial -> {GLB_PATH}")

    # Renderizar vista previa de 3 paneles
    cameras = setup_preview_scene(parts)
    render_preview_sheet(cameras, PREVIEW_PATH)
    print(f"[enemy_projectile] Vista previa generada -> {PREVIEW_PATH}")

    # Guardar archivo .blend oficial
    bpy.ops.wm.save_as_mainfile(filepath=str(BLEND_PATH), compress=True)
    print(f"[enemy_projectile] Archivo .blend oficial -> {BLEND_PATH}")

    # Copiar script fuente a assets/source/blender/enemy_projectile.py
    this_script = Path(__file__).resolve()
    shutil.copy2(this_script, PY_PATH)
    print(f"[enemy_projectile] Script fuente guardado en -> {PY_PATH}")

    # Copiar entrega a la carpeta designada de David: MODELOS JUEGO/enemy_projectile/
    shutil.copy2(GLB_PATH, DRIVE_DIR / "enemy_projectile.glb")
    shutil.copy2(BLEND_PATH, DRIVE_DIR / "enemy_projectile.blend")
    shutil.copy2(PY_PATH, DRIVE_DIR / "enemy_projectile.py")
    shutil.copy2(PREVIEW_PATH, DRIVE_DIR / "enemy_projectile_preview.png")
    shutil.copy2(PREVIEW_PATH, ARTIFACT_PREVIEW)
    print(f"[enemy_projectile] Copia guardada en carpeta de David -> {DRIVE_DIR}")

    print("[enemy_projectile] ¡Modelado de proyectil completado con éxito!")


if __name__ == "__main__":
    main()
