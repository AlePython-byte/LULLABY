"""Generador oficial de alfombra.glb (Prop #6 de The Cradle) para LULLABY:
Referencia visual exacta (Nano Banana aprobada):
  - Alfombra circular infantil apoyada plana en el suelo
  - Borde perimetral cosido con cordel con relieve suave
  - Color base azul pizarra/azul noche oscuro
  - Ilustración central de luna creciente dormida en tono dorado/amarillo pastel
  - Pequeñas estrellas de 4 puntas en tono crema dispersas alrededor de la luna
  - Puntadas rústicas de hilo en el borde perimetral

Reglas técnicas:
  - Presupuesto: <= 800 triángulos (estricto)
  - Origen en la base (Z = 0 en el suelo)
  - Frente hacia +Y en Blender (importa hacia -Z en Godot)
  - Sin texturas externas, materiales Principled BSDF sRGB -> Linear
  - Exportación GLB y guardado .blend sin .blend1
  - Vista previa de 3 paneles (Frente | Cámara Isométrica / Juego | Lateral)
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
PROPS_MODELS_DIR = REPO_ROOT / "assets" / "models" / "props"
SOURCE_DIR = REPO_ROOT / "assets" / "source" / "blender"
DRIVE_DIR = Path(r"C:\Users\davin\OneDrive\Documentos\MODELOS JUEGO\alfombra")
ARTIFACTS_DIR = Path(r"C:\Users\davin\.gemini\antigravity\brain\61207137-232d-4414-8860-f3fa45906692")

PROPS_MODELS_DIR.mkdir(parents=True, exist_ok=True)
SOURCE_DIR.mkdir(parents=True, exist_ok=True)
DRIVE_DIR.mkdir(parents=True, exist_ok=True)

GLB_PATH = PROPS_MODELS_DIR / "alfombra.glb"
BLEND_PATH = SOURCE_DIR / "alfombra.blend"
PY_PATH = SOURCE_DIR / "alfombra.py"
PREVIEW_PATH = SOURCE_DIR / "alfombra_preview.png"
ARTIFACT_PREVIEW = ARTIFACTS_DIR / "alfombra_preview.png"

RENDER_SIZE = 800

# Paleta exacta de la referencia (sRGB)
PALETTE = {
    "RugNavy": (0.24, 0.31, 0.43),        # Azul pizarra/azul noche oscuro principal
    "RugBorder": (0.20, 0.26, 0.36),      # Borde perimetral cosido más oscuro
    "MoonGold": (0.84, 0.74, 0.48),       # Luna dorada/amarillo pastel
    "StarCream": (0.92, 0.90, 0.84),      # Estrellitas crema/marfil
    "StitchThread": (0.55, 0.52, 0.48),   # Puntadas de hilo perimetrales
    "FaceDark": (0.35, 0.30, 0.25),       # Ojo cerrado y sonrisa de la luna
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
                    inp.default_value = 0.92
                elif inp.name == "Specular IOR Level":
                    inp.default_value = 0.10
        materials[name] = mat
    return materials


def set_smooth(obj):
    for f in obj.data.polygons:
        f.use_smooth = True


def create_cylinder(name, radius, depth, vertices, location, rotation=(0, 0, 0), scale=(1, 1, 1), material=None):
    bpy.ops.mesh.primitive_cylinder_add(vertices=vertices, radius=radius, depth=depth, location=location, rotation=rotation)
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = scale
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if material:
        obj.data.materials.append(material)
    return obj


def create_box(name, size, location, rotation=(0, 0, 0), material=None):
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=location, rotation=rotation)
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = size
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if material:
        obj.data.materials.append(material)
    return obj


def build_crescent_moon_flat(name, center, scale=0.42, material=None):
    """Crea la luna creciente dormida plana sobre la superficie de la alfombra."""
    verts = []
    faces = []
    
    segments = 8
    outer_angles = np.linspace(-math.pi * 0.45, math.pi * 0.45, segments)
    inner_angles = np.linspace(-math.pi * 0.45, math.pi * 0.45, segments)
    
    r_outer = 1.0 * scale
    r_inner = 0.74 * scale
    offset_inner = 0.34 * scale
    thick = 0.005
    z_top = center[2] + thick
    z_bot = center[2]
    
    pts_outer = [(math.cos(a) * r_outer, math.sin(a) * r_outer) for a in outer_angles]
    pts_inner = [(math.cos(a) * r_inner + offset_inner, math.sin(a) * r_inner) for a in inner_angles]
    
    n = len(pts_outer)
    # Vértices superiores
    for x, y in pts_outer:
        verts.append((center[0] + x - offset_inner * 0.4, center[1] + y, z_top))
    for x, y in pts_inner:
        verts.append((center[0] + x - offset_inner * 0.4, center[1] + y, z_top))
    # Vértices inferiores
    for x, y in pts_outer:
        verts.append((center[0] + x - offset_inner * 0.4, center[1] + y, z_bot))
    for x, y in pts_inner:
        verts.append((center[0] + x - offset_inner * 0.4, center[1] + y, z_bot))
        
    # Caras superiores
    for i in range(n - 1):
        faces.append((i, i + 1, n + i + 1, n + i))
    # Caras laterales exteriores
    for i in range(n - 1):
        faces.append((i, 2 * n + i, 2 * n + i + 1, i + 1))
    # Caras laterales interiores
    for i in range(n - 1):
        faces.append((n + i, n + i + 1, 3 * n + i + 1, 3 * n + i))
        
    mesh = bpy.data.meshes.new(f"{name}_Mesh")
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    if material:
        obj.data.materials.append(material)
    return obj


def build_star_flat(name, center, scale=0.06, material=None):
    """Crea una estrella plana de 4 puntas para la alfombra."""
    verts = []
    faces = []
    
    thick = 0.004
    z_top = center[2] + thick
    z_bot = center[2]
    
    r_out = scale
    r_in = scale * 0.32
    pts = [
        (0, r_out), (r_in, r_in), (r_out, 0), (r_in, -r_in),
        (0, -r_out), (-r_in, -r_in), (-r_out, 0), (-r_in, r_in)
    ]
    
    center_top = len(verts)
    verts.append((center[0], center[1], z_top))
    base_idx = len(verts)
    for x, y in pts:
        verts.append((center[0] + x, center[1] + y, z_top))
        
    # Caras superiores hacia el centro
    for i in range(len(pts)):
        nxt = (i + 1) % len(pts)
        faces.append((center_top, base_idx + i, base_idx + nxt))
        
    mesh = bpy.data.meshes.new(f"{name}_Mesh")
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    if material:
        obj.data.materials.append(material)
    return obj


def build_circular_rug(materials):
    parts_base = []
    parts_decor = []
    
    # Dimensiones generales:
    # Diámetro: 2.20 m (Radio = 1.10 m)
    # Espesor total: 0.024 m (desde Z = 0 a Z = 0.024 m)
    
    radius_rug = 1.10
    thick_rug = 0.022
    z_center = thick_rug / 2.0
    
    # 1. Cuerpo principal circular de la alfombra (24 lados para curva limpia)
    rug_main = create_cylinder("Rug_Main", radius=radius_rug, depth=thick_rug, vertices=24, location=(0, 0, z_center), material=materials["RugNavy"])
    parts_base.append(rug_main)
    
    # 2. Borde perimetral cosido con ligero relieve (Ring)
    border_w = 0.075
    border_r = radius_rug - border_w / 2.0
    border_thick = thick_rug + 0.005
    # Crear borde como cilindro exterior con material de borde
    border_ring = create_cylinder("Rug_Border_Ring", radius=radius_rug * 1.01, depth=border_thick, vertices=24, location=(0, 0, border_thick / 2.0), material=materials["RugBorder"])
    parts_base.append(border_ring)
    
    # 3. Puntadas rústicas de hilo alrededor del perímetro (10 puntadas)
    num_stitches = 10
    for i in range(num_stitches):
        a = i * (2 * math.pi / num_stitches) + math.radians(12)
        sx = math.cos(a) * (radius_rug - border_w * 0.45)
        sy = math.sin(a) * (radius_rug - border_w * 0.45)
        stitch = create_box(f"Stitch_{i}", (0.014, 0.045, 0.012), (sx, sy, border_thick + 0.002), rotation=(0, 0, a + math.radians(90)), material=materials["StitchThread"])
        parts_base.append(stitch)
        
    # 4. Ilustración Central: Luna Creciente Dormida
    moon = build_crescent_moon_flat("Rug_Moon", center=(0.0, 0.02, thick_rug + 0.001), scale=0.45, material=materials["MoonGold"])
    # Ojo cerrado de la luna (pequeña curva oscura)
    eye_moon = create_box("Moon_Eye", (0.012, 0.065, 0.004), (-0.05, 0.04, thick_rug + 0.007), rotation=(0, 0, math.radians(15)), material=materials["FaceDark"])
    # Sonrisa suave
    smile_moon = create_box("Moon_Smile", (0.010, 0.055, 0.004), (-0.02, -0.06, thick_rug + 0.007), rotation=(0, 0, math.radians(-25)), material=materials["FaceDark"])
    parts_decor.extend([moon, eye_moon, smile_moon])
    
    # 5. Estrellas dispersas alrededor de la luna (según la referencia)
    star_positions = [
        (-0.58, 0.12, 0.065),
        (-0.52, -0.08, 0.055),
        (-0.38, 0.36, 0.050),
        (-0.35, -0.42, 0.060),
        (-0.16, -0.52, 0.045),
        (0.12, -0.56, 0.050),
        (0.48, -0.38, 0.055),
        (0.62, -0.06, 0.055),
        (0.58, 0.22, 0.060),
        (0.36, 0.45, 0.055),
        (-0.08, 0.58, 0.060),
        (0.14, 0.22, 0.045),
        (0.30, 0.08, 0.050),
    ]
    for idx, (px, py, s) in enumerate(star_positions):
        st = build_star_flat(f"Star_{idx}", center=(px, py, thick_rug + 0.001), scale=s, material=materials["StarCream"])
        parts_decor.append(st)
        
    # 6. Unir piezas en partes limpias
    # Rug_Base (Cuerpo de la alfombra, borde perimetral y puntadas)
    bpy.ops.object.select_all(action='DESELECT')
    for p in parts_base:
        p.select_set(True)
    bpy.context.view_layer.objects.active = parts_base[0]
    bpy.ops.object.join()
    base_obj = bpy.context.active_object
    base_obj.name = "Rug_Base"
    
    # Rug_Decor (Luna durmiente, ojos y estrellas)
    bpy.ops.object.select_all(action='DESELECT')
    for p in parts_decor:
        p.select_set(True)
    bpy.context.view_layer.objects.active = parts_decor[0]
    bpy.ops.object.join()
    decor_obj = bpy.context.active_object
    decor_obj.name = "Rug_Decor"
    
    # Asegurar Z = 0 exacto en el suelo
    all_objs = [base_obj, decor_obj]
    min_z = min(obj.bound_box[0][2] + obj.location[2] for obj in all_objs)
    if abs(min_z) > 0.001:
        for obj in all_objs:
            obj.location.z -= min_z
            
    return [base_obj, decor_obj]


def setup_preview_scene(parts):
    light_data = bpy.data.lights.new(name="KeyLight", type='SUN')
    light_data.energy = 3.6
    light_data.color = (0.95, 0.92, 0.88)
    light_obj = bpy.data.objects.new("KeyLight", light_data)
    light_obj.rotation_euler = (math.radians(50), math.radians(20), math.radians(-35))
    bpy.context.collection.objects.link(light_obj)

    fill_data = bpy.data.lights.new(name="FillLight", type='SUN')
    fill_data.energy = 1.3
    fill_data.color = (0.60, 0.65, 0.85)
    fill_obj = bpy.data.objects.new("FillLight", fill_data)
    fill_obj.rotation_euler = (math.radians(-40), math.radians(-30), math.radians(140))
    bpy.context.collection.objects.link(fill_obj)

    rim_data = bpy.data.lights.new(name="RimLight", type='POINT')
    rim_data.energy = 70.0
    rim_data.color = (0.7, 0.8, 1.0)
    rim_obj = bpy.data.objects.new("RimLight", rim_data)
    rim_obj.location = (0, -1.8, 2.0)
    bpy.context.collection.objects.link(rim_obj)

    center = (0.0, 0.0, 0.02)
    dist = 3.2

    cams = []

    # 1. Cámara frontal / cenital inclinada (+Y)
    cam_front = bpy.data.cameras.new("Cam_Front")
    cam_front.lens = 45
    o_front = bpy.data.objects.new("Cam_Front_Obj", cam_front)
    o_front.location = (0.0, dist * 0.8, 2.4)
    direction = Vector(center) - Vector(o_front.location)
    rot_quat = direction.to_track_quat('-Z', 'Y')
    o_front.rotation_euler = rot_quat.to_euler()
    bpy.context.collection.objects.link(o_front)
    cams.append(("front", o_front))

    # 2. Cámara isométrica 3/4 (igual al ángulo cenital de Nano Banana)
    cam_iso = bpy.data.cameras.new("Cam_Iso")
    cam_iso.lens = 45
    o_iso = bpy.data.objects.new("Cam_Iso_Obj", cam_iso)
    iso_dist = 3.0
    o_iso.location = (iso_dist * 0.7, iso_dist * 0.7, 2.6)
    direction = Vector(center) - Vector(o_iso.location)
    rot_quat = direction.to_track_quat('-Z', 'Y')
    o_iso.rotation_euler = rot_quat.to_euler()
    bpy.context.collection.objects.link(o_iso)
    cams.append(("isometric", o_iso))

    # 3. Cámara lateral (+X)
    cam_side = bpy.data.cameras.new("Cam_Side")
    cam_side.lens = 45
    o_side = bpy.data.objects.new("Cam_Side_Obj", cam_side)
    o_side.location = (dist * 0.85, 0.0, 2.4)
    direction = Vector(center) - Vector(o_side.location)
    rot_quat = direction.to_track_quat('-Z', 'Y')
    o_side.rotation_euler = rot_quat.to_euler()
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
    out = bpy.data.images.new("alfombra_preview", sheet.shape[1], sheet.shape[0], alpha=False)
    out.pixels.foreach_set(sheet.ravel())
    out.filepath_raw = str(output_path)
    out.file_format = 'PNG'
    out.save()
    bpy.data.images.remove(out)


def main():
    print("[alfombra] Iniciando modelado oficial de la Alfombra (Prop #6)...")
    clean_scene()
    materials = make_materials()
    parts = build_circular_rug(materials)

    part_names = [p.name for p in parts]
    print(f"[alfombra] Partes construidas ({len(parts)}): {', '.join(part_names)}")

    total_tris = 0
    for p in parts:
        p.data.calc_loop_triangles()
        t = len(p.data.loop_triangles)
        total_tris += t
        print(f"  • {p.name}: {t} tris, loc={p.location}, dims={p.dimensions}")
    print(f"[alfombra] Triángulos totales: {total_tris} (Límite: <= 800)")
    assert total_tris <= 800, f"Excede límite de 800 tris: {total_tris}"

    # Exportar GLB oficial en assets/models/props/alfombra.glb
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
    print(f"[alfombra] Exportado GLB oficial -> {GLB_PATH}")

    # Renderizar vista previa de 3 paneles
    cameras = setup_preview_scene(parts)
    render_preview_sheet(cameras, PREVIEW_PATH)
    print(f"[alfombra] Vista previa generada -> {PREVIEW_PATH}")

    # Guardar archivo .blend oficial
    bpy.ops.wm.save_as_mainfile(filepath=str(BLEND_PATH), compress=True)
    print(f"[alfombra] Archivo .blend oficial -> {BLEND_PATH}")

    # Copiar script fuente a assets/source/blender/alfombra.py
    this_script = Path(__file__).resolve()
    shutil.copy2(this_script, PY_PATH)
    print(f"[alfombra] Script fuente guardado en -> {PY_PATH}")

    # Copiar entrega a la carpeta designada de David: MODELOS JUEGO/alfombra/
    shutil.copy2(GLB_PATH, DRIVE_DIR / "alfombra.glb")
    shutil.copy2(BLEND_PATH, DRIVE_DIR / "alfombra.blend")
    shutil.copy2(PY_PATH, DRIVE_DIR / "alfombra.py")
    shutil.copy2(PREVIEW_PATH, DRIVE_DIR / "alfombra_preview.png")
    shutil.copy2(PREVIEW_PATH, ARTIFACT_PREVIEW)
    print(f"[alfombra] Copia guardada en carpeta de David -> {DRIVE_DIR}")

    print("[alfombra] ¡Modelado de alfombra completado con éxito!")


if __name__ == "__main__":
    main()
