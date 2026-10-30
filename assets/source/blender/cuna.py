"""Generador oficial de cuna.glb (Prop #1 de The Cradle) para LULLABY:
Referencia visual exacta (Nano Banana aprobada):
  - Cuna de madera envejecida oscura estilo Little Nightmares / low-poly asset
  - 4 postes esquineros robustos con remate cuadrado superior (más altos en el cabecero)
  - Cabecero con arco superior curvado y tablones verticales
  - Piecero con baranda superior y barrotes verticales
  - Barandales laterales con barrotes verticales limpios
  - Marco inferior levantado del suelo (patas hasta Z=0)
  - Colchón grueso en color crema/amarillo pastel
  - Manta y almohada plegadas en azul celeste desaturado

Reglas técnicas:
  - Presupuesto: <= 800 triángulos (estricto)
  - Origen en la base (Z = 0)
  - Frente hacia +Y en Blender (importa hacia -Z en Godot)
  - Sin texturas externas, materiales Principled BSDF con colores sRGB -> Linear
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
DRIVE_DIR = Path(r"C:\Users\davin\OneDrive\Documentos\MODELOS JUEGO\cuna")
ARTIFACTS_DIR = Path(r"C:\Users\davin\.gemini\antigravity\brain\61207137-232d-4414-8860-f3fa45906692")

PROPS_MODELS_DIR.mkdir(parents=True, exist_ok=True)
SOURCE_DIR.mkdir(parents=True, exist_ok=True)
DRIVE_DIR.mkdir(parents=True, exist_ok=True)

GLB_PATH = PROPS_MODELS_DIR / "cuna.glb"
BLEND_PATH = SOURCE_DIR / "cuna.blend"
PY_PATH = SOURCE_DIR / "cuna.py"
PREVIEW_PATH = SOURCE_DIR / "cuna_preview.png"
ARTIFACT_PREVIEW = ARTIFACTS_DIR / "cuna_preview.png"

RENDER_SIZE = 800

# Paleta exacta de la referencia (sRGB)
PALETTE = {
    "DarkWood": (0.32, 0.29, 0.33),       # Madera grisácea oscura desgastada
    "HeadboardWood": (0.42, 0.38, 0.40),  # Tono ligeramente más claro para el arco
    "MattressCream": (0.88, 0.86, 0.70),  # Colchón crema pastel
    "BlanketBlue": (0.58, 0.68, 0.77),    # Manta y almohada celeste pastel
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
                    inp.default_value = 0.2
        materials[name] = mat
    return materials


def create_box(name, size, location, material=None):
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=location)
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = size
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if material:
        obj.data.materials.append(material)
    return obj


def build_headboard_arch(materials, width, thickness, center_y, base_z, arch_height):
    """Crea el arco superior del cabecero con geometría curvada low-poly (5 segmentos)."""
    segments = 6
    verts = []
    faces = []
    
    half_w = width / 2.0
    half_t = thickness / 2.0
    
    # Perfil superior curvado y perfil inferior recto/ligeramente curvado
    for i in range(segments + 1):
        t = i / segments
        # x de -half_w a +half_w
        x = -half_w + t * width
        # Curva parabólica para la parte superior
        norm_x = (x / half_w)  # -1 a 1
        z_top = base_z + arch_height * (1.0 - norm_x * norm_x)
        z_bot = base_z
        
        # 4 vértices por corte (y_front, y_back)
        verts.append((x, center_y + half_t, z_top))
        verts.append((x, center_y - half_t, z_top))
        verts.append((x, center_y - half_t, z_bot))
        verts.append((x, center_y + half_t, z_bot))
        
    for i in range(segments):
        idx0 = i * 4
        idx1 = (i + 1) * 4
        # Top face
        faces.append((idx0 + 0, idx1 + 0, idx1 + 1, idx0 + 1))
        # Bottom face
        faces.append((idx0 + 3, idx0 + 2, idx1 + 2, idx1 + 3))
        # Front face (+Y)
        faces.append((idx0 + 0, idx0 + 3, idx1 + 3, idx1 + 0))
        # Back face (-Y)
        faces.append((idx0 + 1, idx1 + 1, idx1 + 2, idx0 + 2))
        
    # Capas de los extremos
    faces.append((0, 1, 2, 3))
    last = segments * 4
    faces.append((last + 0, last + 3, last + 2, last + 1))
    
    mesh = bpy.data.meshes.new("HeadboardArchMesh")
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    
    obj = bpy.data.objects.new("HeadboardArch", mesh)
    bpy.context.collection.objects.link(obj)
    obj.data.materials.append(materials["HeadboardWood"])
    return obj


def build_crib(materials):
    """Construye la cuna completa respetando el diseño y el presupuesto de tris."""
    # Dimensiones generales:
    # Largo (Eje Y): 1.50 m (de Y = -0.75 a Y = +0.75)
    # Ancho (Eje X): 0.96 m (de X = -0.48 a X = +0.48)
    # Cabecero en -Y (Back), Piecero en +Y (Front)
    # Altura postes piecero: 1.05 m
    # Altura postes cabecero: 1.35 m
    
    parts_wood = []
    
    post_thick = 0.08
    half_x = 0.44
    half_y = 0.70
    
    # 1. Cuatro postes esquineros principales
    # Cabecero (Back, -Y): postes más altos
    post_hl = create_box("Post_HL", (post_thick, post_thick, 1.35), (-half_x, -half_y, 1.35 / 2.0), materials["DarkWood"])
    post_hr = create_box("Post_HR", (post_thick, post_thick, 1.35), (half_x, -half_y, 1.35 / 2.0), materials["DarkWood"])
    # Piecero (Front, +Y): postes medianos
    post_fl = create_box("Post_FL", (post_thick, post_thick, 1.05), (-half_x, half_y, 1.05 / 2.0), materials["DarkWood"])
    post_fr = create_box("Post_FR", (post_thick, post_thick, 1.05), (half_x, half_y, 1.05 / 2.0), materials["DarkWood"])
    parts_wood.extend([post_hl, post_hr, post_fl, post_fr])
    
    # Remates decorativos superiores de los 4 postes (pequeñas tapas piramidales/cúbicas)
    cap_size = post_thick * 1.15
    cap_h = 0.03
    cap_hl = create_box("Cap_HL", (cap_size, cap_size, cap_h), (-half_x, -half_y, 1.35 + cap_h / 2.0), materials["HeadboardWood"])
    cap_hr = create_box("Cap_HR", (cap_size, cap_size, cap_h), (half_x, -half_y, 1.35 + cap_h / 2.0), materials["HeadboardWood"])
    cap_fl = create_box("Cap_FL", (cap_size, cap_size, cap_h), (-half_x, half_y, 1.05 + cap_h / 2.0), materials["HeadboardWood"])
    cap_fr = create_box("Cap_FR", (cap_size, cap_size, cap_h), (half_x, half_y, 1.05 + cap_h / 2.0), materials["HeadboardWood"])
    parts_wood.extend([cap_hl, cap_hr, cap_fl, cap_fr])
    
    # 2. Marco base inferior (soporte del colchón a Z = 0.24)
    # Longitud X interna = 2 * half_x - post_thick = 0.88 - 0.08 = 0.80
    # Longitud Y interna = 2 * half_y - post_thick = 1.40 - 0.08 = 1.32
    rail_thick = 0.05
    rail_h = 0.08
    z_base = 0.24
    
    # Vigas inferiores
    beam_back = create_box("Beam_Bottom_Back", (2 * half_x, rail_thick, rail_h), (0, -half_y, z_base), materials["DarkWood"])
    beam_front = create_box("Beam_Bottom_Front", (2 * half_x, rail_thick, rail_h), (0, half_y, z_base), materials["DarkWood"])
    beam_left = create_box("Beam_Bottom_Left", (rail_thick, 2 * half_y - post_thick, rail_h), (-half_x, 0, z_base), materials["DarkWood"])
    beam_right = create_box("Beam_Bottom_Right", (rail_thick, 2 * half_y - post_thick, rail_h), (half_x, 0, z_base), materials["DarkWood"])
    parts_wood.extend([beam_back, beam_front, beam_left, beam_right])
    
    # 3. Barandas superiores
    # Baranda superior piecero (Front, +Y)
    z_top_front = 1.00
    rail_top_front = create_box("Rail_Top_Front", (2 * half_x, rail_thick, rail_thick), (0, half_y, z_top_front), materials["DarkWood"])
    parts_wood.append(rail_top_front)
    
    # Barandas laterales superiores (conectan del cabecero al piecero)
    # Con ligera inclinación o rectas a z_top_front
    rail_top_left = create_box("Rail_Top_Left", (rail_thick, 2 * half_y - post_thick, rail_thick), (-half_x, 0, z_top_front), materials["DarkWood"])
    rail_top_right = create_box("Rail_Top_Right", (rail_thick, 2 * half_y - post_thick, rail_thick), (half_x, 0, z_top_front), materials["DarkWood"])
    parts_wood.extend([rail_top_left, rail_top_right])
    
    # 4. Cabecero (Headboard, -Y)
    # Arco curvado superior
    headboard_w = 2 * half_x
    arch_obj = build_headboard_arch(materials, width=headboard_w, thickness=post_thick * 1.05, center_y=-half_y, base_z=1.20, arch_height=0.16)
    parts_wood.append(arch_obj)
    
    # Viga horizontal intermedia del cabecero
    rail_head_mid = create_box("Rail_Head_Mid", (headboard_w, rail_thick, rail_thick), (0, -half_y, 1.20 - rail_thick / 2.0), materials["DarkWood"])
    parts_wood.append(rail_head_mid)
    
    # Tablones verticales del cabecero (5 tablones anchos rústicos)
    num_head_planks = 5
    plank_w = 0.12
    plank_h = 1.20 - (z_base + rail_h / 2.0)
    plank_z = z_base + rail_h / 2.0 + plank_h / 2.0
    x_span = headboard_w - post_thick - plank_w
    for i in range(num_head_planks):
        px = -x_span / 2.0 + (i / (num_head_planks - 1)) * x_span
        plank = create_box(f"Head_Plank_{i}", (plank_w * 0.92, 0.025, plank_h), (px, -half_y, plank_z), materials["DarkWood"])
        parts_wood.append(plank)
        
    # 5. Barrotes verticales del Piecero (Front, +Y)
    # Altura de los barrotes = z_top_front - z_base - rail_h
    bar_h_front = z_top_front - rail_thick / 2.0 - (z_base + rail_h / 2.0)
    bar_z_front = (z_base + rail_h / 2.0) + bar_h_front / 2.0
    num_front_bars = 4
    x_span_front = (2 * half_x - post_thick - 0.06)
    for i in range(num_front_bars):
        bx = -x_span_front / 2.0 + (i / (num_front_bars - 1)) * x_span_front
        bar = create_box(f"Front_Bar_{i}", (0.04, 0.035, bar_h_front), (bx, half_y, bar_z_front), materials["DarkWood"])
        parts_wood.append(bar)
        
    # 6. Barrotes verticales laterales (Sides, Left & Right)
    num_side_bars = 6
    y_span_side = (2 * half_y - post_thick - 0.12)
    bar_h_side = bar_h_front
    bar_z_side = bar_z_front
    for i in range(num_side_bars):
        by = -y_span_side / 2.0 + (i / (num_side_bars - 1)) * y_span_side
        bar_l = create_box(f"Side_Bar_L_{i}", (0.035, 0.04, bar_h_side), (-half_x, by, bar_z_side), materials["DarkWood"])
        bar_r = create_box(f"Side_Bar_R_{i}", (0.035, 0.04, bar_h_side), (half_x, by, bar_z_side), materials["DarkWood"])
        parts_wood.extend([bar_l, bar_r])
        
    # Unir toda la madera en un único objeto limpio "Crib_Frame"
    bpy.ops.object.select_all(action='DESELECT')
    for p in parts_wood:
        p.select_set(True)
    bpy.context.view_layer.objects.active = parts_wood[0]
    bpy.ops.object.join()
    frame = bpy.context.active_object
    frame.name = "Crib_Frame"
    
    # 7. Colchón interior (Mattress)
    # Debe encajar dentro de la cuna y ser mullido (caja biselada)
    mat_w = 2 * half_x - post_thick * 1.1
    mat_l = 2 * half_y - post_thick * 1.1
    mat_h = 0.16
    mat_z = z_base + rail_h / 2.0 + mat_h / 2.0
    mattress = create_box("Crib_Mattress", (mat_w, mat_l, mat_h), (0, 0, mat_z), materials["MattressCream"])
    
    # Biselar bordes del colchón para que se vea mullido
    bev = mattress.modifiers.new(name="Bevel", type='BEVEL')
    bev.width = 0.035
    bev.segments = 2
    bpy.context.view_layer.objects.active = mattress
    bpy.ops.object.modifier_apply(modifier="Bevel")
    
    # 8. Manta y Almohada (Bedding)
    # Almohada en el cabecero (-Y)
    pillow_w = mat_w * 0.75
    pillow_l = 0.32
    pillow_h = 0.07
    pillow_z = mat_z + mat_h / 2.0 + pillow_h / 2.0
    pillow = create_box("Pillow", (pillow_w, pillow_l, pillow_h), (0, -half_y + post_thick + pillow_l / 2.0 + 0.04, pillow_z), materials["BlanketBlue"])
    bev_p = pillow.modifiers.new(name="Bevel", type='BEVEL')
    bev_p.width = 0.02
    bev_p.segments = 1
    bpy.context.view_layer.objects.active = pillow
    bpy.ops.object.modifier_apply(modifier="Bevel")
    
    # Manta plegada doblada en diagonal/lado (como en la referencia)
    blanket_w = mat_w * 0.95
    blanket_l = 0.70
    blanket_h = 0.05
    blanket_y = 0.05
    blanket_z = mat_z + mat_h / 2.0 + blanket_h / 2.0
    blanket = create_box("Blanket", (blanket_w, blanket_l, blanket_h), (0, blanket_y, blanket_z), materials["BlanketBlue"])
    bev_b = blanket.modifiers.new(name="Bevel", type='BEVEL')
    bev_b.width = 0.02
    bev_b.segments = 1
    bpy.context.view_layer.objects.active = blanket
    bpy.ops.object.modifier_apply(modifier="Bevel")
    
    # Unir Almohada y Manta en "Crib_Bedding"
    bpy.ops.object.select_all(action='DESELECT')
    pillow.select_set(True)
    blanket.select_set(True)
    bpy.context.view_layer.objects.active = blanket
    bpy.ops.object.join()
    bedding = bpy.context.active_object
    bedding.name = "Crib_Bedding"
    
    # Asignar origen en el suelo (0, 0, 0)
    for obj in [frame, mattress, bedding]:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = frame
    
    return [frame, mattress, bedding]


def setup_preview_scene(parts):
    # Luz suave y envolvente para terror infantil
    light_data = bpy.data.lights.new(name="KeyLight", type='SUN')
    light_data.energy = 3.5
    light_data.color = (0.95, 0.92, 0.88)
    light_obj = bpy.data.objects.new("KeyLight", light_data)
    light_obj.rotation_euler = (math.radians(50), math.radians(20), math.radians(-35))
    bpy.context.collection.objects.link(light_obj)

    fill_data = bpy.data.lights.new(name="FillLight", type='SUN')
    fill_data.energy = 1.2
    fill_data.color = (0.60, 0.65, 0.85)  # Azulado oscuro de relleno
    fill_obj = bpy.data.objects.new("FillLight", fill_data)
    fill_obj.rotation_euler = (math.radians(-40), math.radians(-30), math.radians(140))
    bpy.context.collection.objects.link(fill_obj)

    rim_data = bpy.data.lights.new(name="RimLight", type='POINT')
    rim_data.energy = 80.0
    rim_data.color = (0.7, 0.8, 1.0)
    rim_obj = bpy.data.objects.new("RimLight", rim_data)
    rim_obj.location = (0, -1.8, 2.2)
    bpy.context.collection.objects.link(rim_obj)

    # Centro de la cuna: (0, 0, 0.7)
    center = (0.0, 0.0, 0.70)
    dist = 3.2

    cams = []

    # 1. Cámara frontal (+Y mirando hacia el piecero / interior)
    cam_front = bpy.data.cameras.new("Cam_Front")
    cam_front.lens = 50
    o_front = bpy.data.objects.new("Cam_Front_Obj", cam_front)
    o_front.location = (0.0, dist + 0.2, center[2] + 0.4)
    o_front.rotation_euler = (math.radians(82), 0.0, math.radians(180))
    bpy.context.collection.objects.link(o_front)
    cams.append(("front", o_front))

    # 2. Cámara isométrica 3/4 (perspectiva exacta del juego / referencia)
    cam_iso = bpy.data.cameras.new("Cam_Iso")
    cam_iso.lens = 50
    o_iso = bpy.data.objects.new("Cam_Iso_Obj", cam_iso)
    # Ángulo 3/4 elevado
    iso_dist = 3.0
    o_iso.location = (iso_dist * 0.7, iso_dist * 0.7, 2.2)
    # Apuntar al centro
    direction = Vector(center) - Vector(o_iso.location)
    rot_quat = direction.to_track_quat('-Z', 'Y')
    o_iso.rotation_euler = rot_quat.to_euler()
    bpy.context.collection.objects.link(o_iso)
    cams.append(("isometric", o_iso))

    # 3. Cámara lateral (+X)
    cam_side = bpy.data.cameras.new("Cam_Side")
    cam_side.lens = 50
    o_side = bpy.data.objects.new("Cam_Side_Obj", cam_side)
    o_side.location = (dist, 0.0, center[2] + 0.3)
    o_side.rotation_euler = (math.radians(85), 0.0, math.radians(90))
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
        # Fondo oscuro neutro azulado acorde a la referencia
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
    out = bpy.data.images.new("cuna_preview", sheet.shape[1], sheet.shape[0], alpha=False)
    out.pixels.foreach_set(sheet.ravel())
    out.filepath_raw = str(output_path)
    out.file_format = 'PNG'
    out.save()
    bpy.data.images.remove(out)


def main():
    print("[cuna] Iniciando modelado oficial de la Cuna (Prop #1) para LULLABY...")
    clean_scene()
    materials = make_materials()
    parts = build_crib(materials)

    part_names = [p.name for p in parts]
    print(f"[cuna] Partes construidas ({len(parts)}): {', '.join(part_names)}")

    total_tris = 0
    for p in parts:
        p.data.calc_loop_triangles()
        t = len(p.data.loop_triangles)
        total_tris += t
        print(f"  • {p.name}: {t} tris, loc={p.location}, dims={p.dimensions}")
    print(f"[cuna] Triángulos totales: {total_tris} (Límite para props: <= 800)")
    assert total_tris <= 800, f"Excede límite de 800 tris: {total_tris}"

    # Exportar GLB oficial en assets/models/props/cuna.glb
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
    print(f"[cuna] Exportado GLB oficial -> {GLB_PATH}")

    # Renderizar vista previa de 3 paneles
    cameras = setup_preview_scene(parts)
    render_preview_sheet(cameras, PREVIEW_PATH)
    print(f"[cuna] Vista previa generada -> {PREVIEW_PATH}")

    # Guardar archivo .blend oficial
    bpy.ops.wm.save_as_mainfile(filepath=str(BLEND_PATH), compress=True)
    print(f"[cuna] Archivo .blend oficial -> {BLEND_PATH}")

    # Copiar script fuente a assets/source/blender/cuna.py
    this_script = Path(__file__).resolve()
    shutil.copy2(this_script, PY_PATH)
    print(f"[cuna] Script fuente guardado en -> {PY_PATH}")

    # Copiar entrega a la carpeta designada de David: MODELOS JUEGO/cuna/
    shutil.copy2(GLB_PATH, DRIVE_DIR / "cuna.glb")
    shutil.copy2(BLEND_PATH, DRIVE_DIR / "cuna.blend")
    shutil.copy2(PY_PATH, DRIVE_DIR / "cuna.py")
    shutil.copy2(PREVIEW_PATH, DRIVE_DIR / "cuna_preview.png")
    shutil.copy2(PREVIEW_PATH, ARTIFACT_PREVIEW)
    print(f"[cuna] Copia guardada en carpeta de David -> {DRIVE_DIR}")

    print("[cuna] ¡Modelado de cuna completado con éxito!")


if __name__ == "__main__":
    main()
