"""Generador oficial de tear.glb (Lágrima de Noah) para LULLABY:
Especificaciones del plan:
  - Lágrima de Noah: gota estilizada pequeña azul brillante
  - Presupuesto: <= 150 triángulos (estricto)
  - Origen en la base (Z = 0)
  - Frente hacia +Y en Blender (importa hacia -Z en Godot)
  - Color plano azul pastel brillante sin texturas externas
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
DRIVE_DIR = Path(r"C:\Users\davin\OneDrive\Documentos\MODELOS JUEGO\tear")
ARTIFACTS_DIR = Path(r"C:\Users\davin\.gemini\antigravity\brain\61207137-232d-4414-8860-f3fa45906692")

MODELS_DIR.mkdir(parents=True, exist_ok=True)
SOURCE_DIR.mkdir(parents=True, exist_ok=True)
DRIVE_DIR.mkdir(parents=True, exist_ok=True)

GLB_PATH = MODELS_DIR / "tear.glb"
BLEND_PATH = SOURCE_DIR / "tear.blend"
PY_PATH = SOURCE_DIR / "tear.py"
PREVIEW_PATH = SOURCE_DIR / "tear_preview.png"
ARTIFACT_PREVIEW = ARTIFACTS_DIR / "tear_preview.png"

RENDER_SIZE = 800

# Paleta azul brillante de la lágrima (sRGB)
PALETTE = {
    "TearBlue": (0.35, 0.72, 0.96),       # Azul celeste acuoso brillante
    "TearGleam": (0.85, 0.95, 1.00),      # Brillo/reflejo blanquecino en la gota
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
                    inp.default_value = 0.20  # Superficie brillante de agua
                elif inp.name == "Specular IOR Level":
                    inp.default_value = 0.80
        materials[name] = mat
    return materials


def set_smooth(obj):
    for f in obj.data.polygons:
        f.use_smooth = True


def build_teardrop(materials):
    """Construye una lágrima 3D estilizada en forma de gota con reflejo y <= 150 tris."""
    verts = []
    faces = []
    
    # 5 anillos verticales y 8 segmentos angulares
    # Perfil vertical de una gota:
    # z_norm de 0 a 1: z=0 fondo redondeado ancho, z=1 punta afilada
    rings = [
        (0.00, 0.00),     # Polo inferior (z=0, radio=0)
        (0.08, 0.14),     # Anillo inferior ancho
        (0.18, 0.13),     # Anillo medio
        (0.30, 0.08),     # Tapering hacia la punta
        (0.40, 0.02),     # Casi punta
        (0.44, 0.00),     # Punta afilada superior
    ]
    
    segments = 8
    
    # Polo inferior
    v_bottom = len(verts)
    verts.append((0.0, 0.0, rings[0][0]))
    
    # Anillos intermedios
    ring_start_indices = []
    for z_pos, r in rings[1:-1]:
        start_idx = len(verts)
        ring_start_indices.append(start_idx)
        for i in range(segments):
            angle = i * (2 * math.pi / segments)
            vx = math.cos(angle) * r
            vy = math.sin(angle) * r
            verts.append((vx, vy, z_pos))
            
    # Polo superior
    v_top = len(verts)
    verts.append((0.0, 0.0, rings[-1][0]))
    
    # Caras del polo inferior
    first_ring = ring_start_indices[0]
    for i in range(segments):
        nxt = (i + 1) % segments
        faces.append((v_bottom, first_ring + nxt, first_ring + i))
        
    # Caras entre anillos
    for r_idx in range(len(ring_start_indices) - 1):
        idx0 = ring_start_indices[r_idx]
        idx1 = ring_start_indices[r_idx + 1]
        for i in range(segments):
            nxt = (i + 1) % segments
            faces.append((idx0 + i, idx0 + nxt, idx1 + nxt, idx1 + i))
            
    # Caras hacia el polo superior
    last_ring = ring_start_indices[-1]
    for i in range(segments):
        nxt = (i + 1) % segments
        faces.append((v_top, last_ring + i, last_ring + nxt))
        
    mesh = bpy.data.meshes.new("TearMesh")
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    
    tear_obj = bpy.data.objects.new("Tear_Body", mesh)
    bpy.context.collection.objects.link(tear_obj)
    set_smooth(tear_obj)
    tear_obj.data.materials.append(materials["TearBlue"])
    
    # Pequeño brillo/reflejo especular blanco en un costado de la gota (+X, +Y)
    bpy.ops.mesh.primitive_uv_sphere_add(segments=6, ring_count=3, radius=0.035, location=(0.06, 0.06, 0.16))
    gleam = bpy.context.active_object
    gleam.name = "Tear_Gleam"
    gleam.scale = (0.5, 0.8, 1.2)
    bpy.ops.object.transform_apply(scale=True)
    set_smooth(gleam)
    gleam.data.materials.append(materials["TearGleam"])
    
    # Unir en un objeto limpio "Tear"
    bpy.ops.object.select_all(action='DESELECT')
    tear_obj.select_set(True)
    gleam.select_set(True)
    bpy.context.view_layer.objects.active = tear_obj
    bpy.ops.object.join()
    tear_final = bpy.context.active_object
    tear_final.name = "Tear"
    
    return [tear_final]


def setup_preview_scene(parts):
    light_data = bpy.data.lights.new(name="KeyLight", type='SUN')
    light_data.energy = 4.0
    light_data.color = (0.95, 0.95, 1.0)
    light_obj = bpy.data.objects.new("KeyLight", light_data)
    light_obj.rotation_euler = (math.radians(50), math.radians(20), math.radians(-35))
    bpy.context.collection.objects.link(light_obj)

    fill_data = bpy.data.lights.new(name="FillLight", type='SUN')
    fill_data.energy = 1.5
    fill_data.color = (0.45, 0.65, 0.95)
    fill_obj = bpy.data.objects.new("FillLight", fill_data)
    fill_obj.rotation_euler = (math.radians(-40), math.radians(-30), math.radians(140))
    bpy.context.collection.objects.link(fill_obj)

    rim_data = bpy.data.lights.new(name="RimLight", type='POINT')
    rim_data.energy = 30.0
    rim_data.color = (0.8, 0.95, 1.0)
    rim_obj = bpy.data.objects.new("RimLight", rim_data)
    rim_obj.location = (0, -0.6, 0.7)
    bpy.context.collection.objects.link(rim_obj)

    center = (0.0, 0.0, 0.22)
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
    out = bpy.data.images.new("tear_preview", sheet.shape[1], sheet.shape[0], alpha=False)
    out.pixels.foreach_set(sheet.ravel())
    out.filepath_raw = str(output_path)
    out.file_format = 'PNG'
    out.save()
    bpy.data.images.remove(out)


def main():
    print("[tear] Iniciando modelado oficial de la Lágrima de Noah (tear.glb)...")
    clean_scene()
    materials = make_materials()
    parts = build_teardrop(materials)

    part_names = [p.name for p in parts]
    print(f"[tear] Partes construidas ({len(parts)}): {', '.join(part_names)}")

    total_tris = 0
    for p in parts:
        p.data.calc_loop_triangles()
        t = len(p.data.loop_triangles)
        total_tris += t
        print(f"  • {p.name}: {t} tris, loc={p.location}, dims={p.dimensions}")
    print(f"[tear] Triángulos totales: {total_tris} (Límite para proyectiles: <= 150)")
    assert total_tris <= 150, f"Excede límite de 150 tris: {total_tris}"

    # Exportar GLB oficial en assets/models/tear.glb
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
    print(f"[tear] Exportado GLB oficial -> {GLB_PATH}")

    # Renderizar vista previa de 3 paneles
    cameras = setup_preview_scene(parts)
    render_preview_sheet(cameras, PREVIEW_PATH)
    print(f"[tear] Vista previa generada -> {PREVIEW_PATH}")

    # Guardar archivo .blend oficial
    bpy.ops.wm.save_as_mainfile(filepath=str(BLEND_PATH), compress=True)
    print(f"[tear] Archivo .blend oficial -> {BLEND_PATH}")

    # Copiar script fuente a assets/source/blender/tear.py
    this_script = Path(__file__).resolve()
    shutil.copy2(this_script, PY_PATH)
    print(f"[tear] Script fuente guardado en -> {PY_PATH}")

    # Copiar entrega a la carpeta designada de David: MODELOS JUEGO/tear/
    shutil.copy2(GLB_PATH, DRIVE_DIR / "tear.glb")
    shutil.copy2(BLEND_PATH, DRIVE_DIR / "tear.blend")
    shutil.copy2(PY_PATH, DRIVE_DIR / "tear.py")
    shutil.copy2(PREVIEW_PATH, DRIVE_DIR / "tear_preview.png")
    shutil.copy2(PREVIEW_PATH, ARTIFACT_PREVIEW)
    print(f"[tear] Copia guardada en carpeta de David -> {DRIVE_DIR}")

    print("[tear] ¡Modelado de lágrima completado con éxito!")


if __name__ == "__main__":
    main()
