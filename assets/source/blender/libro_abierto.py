"""Generador oficial de libro_abierto.glb (Prop #4 de The Cradle) para LULLABY:
Referencia visual exacta (Nano Banana aprobada):
  - Cuento infantil gigante abierto en el suelo visto en perspectiva 3/4
  - Cubierta rígida vintage oscura en la base con lomo central
  - Bloques gruesos de páginas apiladas a izquierda y derecha
  - Páginas curvadas dinámicas abriéndose hacia arriba en el centro (efecto de viento/paso de página)
  - Tonos cálidos de pergamino/papel envejecido y detalles contrastados de ilustración estilizada

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
DRIVE_DIR = Path(r"C:\Users\davin\OneDrive\Documentos\MODELOS JUEGO\libro_abierto")
ARTIFACTS_DIR = Path(r"C:\Users\davin\.gemini\antigravity\brain\61207137-232d-4414-8860-f3fa45906692")

PROPS_MODELS_DIR.mkdir(parents=True, exist_ok=True)
SOURCE_DIR.mkdir(parents=True, exist_ok=True)
DRIVE_DIR.mkdir(parents=True, exist_ok=True)

GLB_PATH = PROPS_MODELS_DIR / "libro_abierto.glb"
BLEND_PATH = SOURCE_DIR / "libro_abierto.blend"
PY_PATH = SOURCE_DIR / "libro_abierto.py"
PREVIEW_PATH = SOURCE_DIR / "libro_abierto_preview.png"
ARTIFACT_PREVIEW = ARTIFACTS_DIR / "libro_abierto_preview.png"

RENDER_SIZE = 800

# Paleta exacta de la referencia (sRGB)
PALETTE = {
    "CoverDark": (0.28, 0.30, 0.35),      # Cubierta dura envejecida azulada/pizarra
    "PagesEdge": (0.76, 0.70, 0.58),      # Bloque lateral de páginas envejecidas
    "PageFace": (0.90, 0.86, 0.74),       # Superficie clara de papel pergamino
    "IllustrationDark": (0.36, 0.34, 0.38), # Detalle gráfico de bosque oscuro
    "IllustrationColor": (0.84, 0.72, 0.56), # Tono cálido de ilustración infantil
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
                    inp.default_value = 0.85
                elif inp.name == "Specular IOR Level":
                    inp.default_value = 0.15
        materials[name] = mat
    return materials


def create_box(name, size, location, rotation=(0, 0, 0), material=None):
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=location, rotation=rotation)
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = size
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if material:
        obj.data.materials.append(material)
    return obj


def build_curved_page_strip(name, p0, p1, p2, p3, width_y, thickness, material, segments=6):
    """Crea una hoja de papel curvada con grosor mediante una curva Bezier cúbica."""
    verts = []
    faces = []
    
    half_y = width_y / 2.0
    half_t = thickness / 2.0
    
    for i in range(segments + 1):
        t = float(i) / float(segments)
        omt = 1.0 - t
        # Puntos Bezier cúbicos en plano XZ
        pt_x = (omt**3) * p0.x + 3 * (omt**2) * t * p1.x + 3 * omt * (t**2) * p2.x + (t**3) * p3.x
        pt_z = (omt**3) * p0.z + 3 * (omt**2) * t * p1.z + 3 * omt * (t**2) * p2.z + (t**3) * p3.z
        
        # Tangente dP/dt
        tang_x = 3 * (omt**2) * (p1.x - p0.x) + 6 * omt * t * (p2.x - p1.x) + 3 * (t**2) * (p3.x - p2.x)
        tang_z = 3 * (omt**2) * (p1.z - p0.z) + 6 * omt * t * (p2.z - p1.z) + 3 * (t**2) * (p3.z - p2.z)
        
        tang_len = math.sqrt(tang_x**2 + tang_z**2)
        if tang_len > 0.0001:
            norm_x = -tang_z / tang_len
            norm_z = tang_x / tang_len
        else:
            norm_x, norm_z = 0.0, 1.0
            
        top_x = pt_x + norm_x * half_t
        top_z = pt_z + norm_z * half_t
        bot_x = pt_x - norm_x * half_t
        bot_z = pt_z - norm_z * half_t
        
        # 4 vértices por corte transversal
        verts.append((top_x, half_y, top_z))
        verts.append((top_x, -half_y, top_z))
        verts.append((bot_x, -half_y, bot_z))
        verts.append((bot_x, half_y, bot_z))
        
    for i in range(segments):
        idx0 = i * 4
        idx1 = (i + 1) * 4
        # Top
        faces.append((idx0 + 0, idx1 + 0, idx1 + 1, idx0 + 1))
        # Bottom
        faces.append((idx0 + 3, idx0 + 2, idx1 + 2, idx1 + 3))
        # Back (+Y)
        faces.append((idx0 + 0, idx0 + 3, idx1 + 3, idx1 + 0))
        # Front (-Y)
        faces.append((idx0 + 1, idx1 + 1, idx1 + 2, idx0 + 2))
        
    # Tapas en los extremos
    faces.append((0, 1, 2, 3))
    last = segments * 4
    faces.append((last + 0, last + 3, last + 2, last + 1))
    
    mesh = bpy.data.meshes.new(f"{name}_Mesh")
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    if material:
        obj.data.materials.append(material)
    return obj


def build_open_book(materials):
    parts_cover = []
    parts_pages = []
    parts_illustrations = []
    
    # Dimensiones generales:
    # Libro abierto: Ancho X = 1.10 m (de X = -0.55 a +0.55)
    # Longitud Y = 0.90 m (de Y = -0.45 a +0.45)
    # Grosor del lomo y tapas en Z = 0.02 a 0.14 m
    # Páginas dinámicas levantadas hasta Z = 0.58 m
    
    book_len = 0.88
    cover_w = 0.54
    cover_thick = 0.025
    block_thick = 0.10
    
    # 1. Cubierta dura y Lomo (Book_Cover)
    # Lomo central ligeramente curvado a Z = 0
    spine = create_box("Book_Spine", (0.10, book_len + 0.04, 0.02), (0, 0, 0.01), material=materials["CoverDark"])
    # Tapa izquierda (-X): se abre con leve inclinación
    cover_l = create_box("Cover_Left", (cover_w, book_len + 0.04, cover_thick), (-0.31, 0, 0.016), rotation=(0, math.radians(-3.5), 0), material=materials["CoverDark"])
    # Tapa derecha (+X): inclinación simétrica
    cover_r = create_box("Cover_Right", (cover_w, book_len + 0.04, cover_thick), (0.31, 0, 0.016), rotation=(0, math.radians(3.5), 0), material=materials["CoverDark"])
    parts_cover.extend([spine, cover_l, cover_r])
    
    # 2. Bloques de páginas apiladas (Page Blocks)
    # Bloque izquierdo (-X)
    block_w = 0.48
    block_l = create_box("Pages_Block_Left", (block_w, book_len * 0.96, block_thick), (-0.29, 0, 0.025 + block_thick / 2.0), rotation=(0, math.radians(-2.5), 0), material=materials["PagesEdge"])
    # Bloque derecho (+X)
    block_r = create_box("Pages_Block_Right", (block_w, book_len * 0.96, block_thick), (0.29, 0, 0.025 + block_thick / 2.0), rotation=(0, math.radians(2.5), 0), material=materials["PagesEdge"])
    parts_pages.extend([block_l, block_r])
    
    # 3. Páginas abiertas estáticas base (suaves curvas saliendo del canal central)
    # Página abierta izquierda base (se posa sobre el bloque izquierdo)
    p0_l = Vector((-0.03, 0.0, 0.05))
    p1_l = Vector((-0.15, 0.0, 0.16))
    p2_l = Vector((-0.35, 0.0, 0.14))
    p3_l = Vector((-0.52, 0.0, 0.13))
    page_base_l = build_curved_page_strip("Page_Base_Left", p0_l, p1_l, p2_l, p3_l, width_y=book_len * 0.95, thickness=0.008, material=materials["PageFace"], segments=6)
    parts_pages.append(page_base_l)
    
    # Página abierta derecha base (se posa sobre el bloque derecho)
    p0_r = Vector((0.03, 0.0, 0.05))
    p1_r = Vector((0.15, 0.0, 0.15))
    p2_r = Vector((0.35, 0.0, 0.14))
    p3_r = Vector((0.52, 0.0, 0.13))
    page_base_r = build_curved_page_strip("Page_Base_Right", p0_r, p1_r, p2_r, p3_r, width_y=book_len * 0.95, thickness=0.008, material=materials["PageFace"], segments=6)
    parts_pages.append(page_base_r)
    
    # 4. Páginas dinámicas levantadas (Efecto viento / paso de página dramático de la referencia)
    # Página 1 (La gran página central levantada verticalmente con curva en S hacia la derecha)
    # Sube hasta Z = 0.54 m y se dobla grácilmente
    p0_turn1 = Vector((-0.01, 0.0, 0.06))
    p1_turn1 = Vector((0.04, 0.0, 0.32))
    p2_turn1 = Vector((0.18, 0.0, 0.54))
    p3_turn1 = Vector((0.32, 0.0, 0.44))
    page_turn1 = build_curved_page_strip("Page_Turning_Main", p0_turn1, p1_turn1, p2_turn1, p3_turn1, width_y=book_len * 0.94, thickness=0.008, material=materials["PageFace"], segments=8)
    parts_pages.append(page_turn1)
    
    # Detalle de ilustración oscura en la página levantada (como el bosque misterioso de la imagen)
    p0_ill = Vector((0.02, 0.0, 0.12))
    p1_ill = Vector((0.07, 0.0, 0.33))
    p2_ill = Vector((0.19, 0.0, 0.51))
    p3_ill = Vector((0.30, 0.0, 0.42))
    page_ill = build_curved_page_strip("Page_Illustration_Dark", p0_ill, p1_ill, p2_ill, p3_ill, width_y=book_len * 0.76, thickness=0.009, material=materials["IllustrationDark"], segments=6)
    parts_illustrations.append(page_ill)
    
    # Página 2 (Segunda página levantada detrás a medio abrir)
    p0_turn2 = Vector((0.02, 0.0, 0.06))
    p1_turn2 = Vector((0.12, 0.0, 0.24))
    p2_turn2 = Vector((0.32, 0.0, 0.38))
    p3_turn2 = Vector((0.44, 0.0, 0.28))
    page_turn2 = build_curved_page_strip("Page_Turning_Second", p0_turn2, p1_turn2, p2_turn2, p3_turn2, width_y=book_len * 0.93, thickness=0.007, material=materials["PageFace"], segments=7)
    parts_pages.append(page_turn2)
    
    # Página 3 (Tercera página levantada flotando cerca del bloque derecho)
    p0_turn3 = Vector((0.03, 0.0, 0.06))
    p1_turn3 = Vector((0.18, 0.0, 0.18))
    p2_turn3 = Vector((0.36, 0.0, 0.24))
    p3_turn3 = Vector((0.48, 0.0, 0.18))
    page_turn3 = build_curved_page_strip("Page_Turning_Third", p0_turn3, p1_turn3, p2_turn3, p3_turn3, width_y=book_len * 0.93, thickness=0.006, material=materials["PageFace"], segments=6)
    parts_pages.append(page_turn3)
    
    # 5. Detalle de ilustración en la página izquierda (Castillo / Sol infantil)
    ill_l = create_box("Illustration_Castle", (0.34, book_len * 0.68, 0.006), (-0.30, 0, 0.145), rotation=(0, math.radians(-2.5), 0), material=materials["IllustrationColor"])
    parts_illustrations.append(ill_l)
    
    # 6. Unir piezas en partes limpias
    # Book_Cover
    bpy.ops.object.select_all(action='DESELECT')
    for p in parts_cover:
        p.select_set(True)
    bpy.context.view_layer.objects.active = parts_cover[0]
    bpy.ops.object.join()
    cover = bpy.context.active_object
    cover.name = "Book_Cover"
    
    # Book_Pages
    bpy.ops.object.select_all(action='DESELECT')
    for p in parts_pages:
        p.select_set(True)
    bpy.context.view_layer.objects.active = parts_pages[0]
    bpy.ops.object.join()
    pages = bpy.context.active_object
    pages.name = "Book_Pages"
    
    # Book_Illustrations
    bpy.ops.object.select_all(action='DESELECT')
    for p in parts_illustrations:
        p.select_set(True)
    bpy.context.view_layer.objects.active = parts_illustrations[0]
    bpy.ops.object.join()
    illustrations = bpy.context.active_object
    illustrations.name = "Book_Illustrations"
    
    # Ajuste de base exacta Z = 0
    all_objs = [cover, pages, illustrations]
    min_z = min(obj.bound_box[0][2] + obj.location[2] for obj in all_objs)
    if abs(min_z) > 0.001:
        for obj in all_objs:
            obj.location.z -= min_z
            
    return [cover, pages, illustrations]


def setup_preview_scene(parts):
    light_data = bpy.data.lights.new(name="KeyLight", type='SUN')
    light_data.energy = 3.6
    light_data.color = (0.95, 0.92, 0.88)
    light_obj = bpy.data.objects.new("KeyLight", light_data)
    light_obj.rotation_euler = (math.radians(52), math.radians(22), math.radians(-38))
    bpy.context.collection.objects.link(light_obj)

    fill_data = bpy.data.lights.new(name="FillLight", type='SUN')
    fill_data.energy = 1.3
    fill_data.color = (0.60, 0.65, 0.85)
    fill_obj = bpy.data.objects.new("FillLight", fill_data)
    fill_obj.rotation_euler = (math.radians(-40), math.radians(-30), math.radians(140))
    bpy.context.collection.objects.link(fill_obj)

    rim_data = bpy.data.lights.new(name="RimLight", type='POINT')
    rim_data.energy = 75.0
    rim_data.color = (0.7, 0.8, 1.0)
    rim_obj = bpy.data.objects.new("RimLight", rim_data)
    rim_obj.location = (0, -1.6, 1.6)
    bpy.context.collection.objects.link(rim_obj)

    center = (0.0, 0.0, 0.25)
    dist = 2.4

    cams = []

    # 1. Cámara frontal
    cam_front = bpy.data.cameras.new("Cam_Front")
    cam_front.lens = 50
    o_front = bpy.data.objects.new("Cam_Front_Obj", cam_front)
    o_front.location = (0.0, dist + 0.1, center[2] + 0.3)
    o_front.rotation_euler = (math.radians(78), 0.0, math.radians(180))
    bpy.context.collection.objects.link(o_front)
    cams.append(("front", o_front))

    # 2. Cámara isométrica 3/4
    cam_iso = bpy.data.cameras.new("Cam_Iso")
    cam_iso.lens = 50
    o_iso = bpy.data.objects.new("Cam_Iso_Obj", cam_iso)
    iso_dist = 2.3
    o_iso.location = (iso_dist * 0.7, iso_dist * 0.7, 1.5)
    direction = Vector(center) - Vector(o_iso.location)
    rot_quat = direction.to_track_quat('-Z', 'Y')
    o_iso.rotation_euler = rot_quat.to_euler()
    bpy.context.collection.objects.link(o_iso)
    cams.append(("isometric", o_iso))

    # 3. Cámara lateral
    cam_side = bpy.data.cameras.new("Cam_Side")
    cam_side.lens = 50
    o_side = bpy.data.objects.new("Cam_Side_Obj", cam_side)
    o_side.location = (dist, 0.0, center[2] + 0.25)
    o_side.rotation_euler = (math.radians(82), 0.0, math.radians(90))
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
    out = bpy.data.images.new("libro_preview", sheet.shape[1], sheet.shape[0], alpha=False)
    out.pixels.foreach_set(sheet.ravel())
    out.filepath_raw = str(output_path)
    out.file_format = 'PNG'
    out.save()
    bpy.data.images.remove(out)


def main():
    print("[libro_abierto] Iniciando modelado oficial del Libro Abierto (Prop #4)...")
    clean_scene()
    materials = make_materials()
    parts = build_open_book(materials)

    part_names = [p.name for p in parts]
    print(f"[libro_abierto] Partes construidas ({len(parts)}): {', '.join(part_names)}")

    total_tris = 0
    for p in parts:
        p.data.calc_loop_triangles()
        t = len(p.data.loop_triangles)
        total_tris += t
        print(f"  • {p.name}: {t} tris, loc={p.location}, dims={p.dimensions}")
    print(f"[libro_abierto] Triángulos totales: {total_tris} (Límite: <= 800)")
    assert total_tris <= 800, f"Excede límite de 800 tris: {total_tris}"

    # Exportar GLB oficial en assets/models/props/libro_abierto.glb
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
    print(f"[libro_abierto] Exportado GLB oficial -> {GLB_PATH}")

    # Renderizar vista previa de 3 paneles
    cameras = setup_preview_scene(parts)
    render_preview_sheet(cameras, PREVIEW_PATH)
    print(f"[libro_abierto] Vista previa generada -> {PREVIEW_PATH}")

    # Guardar archivo .blend oficial
    bpy.ops.wm.save_as_mainfile(filepath=str(BLEND_PATH), compress=True)
    print(f"[libro_abierto] Archivo .blend oficial -> {BLEND_PATH}")

    # Copiar script fuente a assets/source/blender/libro_abierto.py
    this_script = Path(__file__).resolve()
    shutil.copy2(this_script, PY_PATH)
    print(f"[libro_abierto] Script fuente guardado en -> {PY_PATH}")

    # Copiar entrega a la carpeta designada de David: MODELOS JUEGO/libro_abierto/
    shutil.copy2(GLB_PATH, DRIVE_DIR / "libro_abierto.glb")
    shutil.copy2(BLEND_PATH, DRIVE_DIR / "libro_abierto.blend")
    shutil.copy2(PY_PATH, DRIVE_DIR / "libro_abierto.py")
    shutil.copy2(PREVIEW_PATH, DRIVE_DIR / "libro_abierto_preview.png")
    shutil.copy2(PREVIEW_PATH, ARTIFACT_PREVIEW)
    print(f"[libro_abierto] Copia guardada en carpeta de David -> {DRIVE_DIR}")

    print("[libro_abierto] ¡Modelado de libro abierto completado con éxito!")


if __name__ == "__main__":
    main()
