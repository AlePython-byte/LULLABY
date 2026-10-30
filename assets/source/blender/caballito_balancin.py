"""Generador oficial de caballito_balancin.glb (Prop #2 de The Cradle) para LULLABY:
Referencia visual exacta (Nano Banana aprobada):
  - Caballito balancín clásico de madera oscura envejecida estilo Little Nightmares
  - Dos patines curvos inferiores con barras transversales/reposapiés
  - Cuatro patas de madera inclinadas
  - Viga central de soporte del cuerpo
  - Montura contorneada con respaldo en color crema/marfil pastel
  - Cabeza estilizada tallada con ceño/ojo triste tallado, orejas y crin escalonada
  - Clavija/manillar transversal cilíndrico atravesando el cuello
  - Pequeña cola de madera en la parte trasera

Reglas técnicas:
  - Presupuesto: <= 800 triángulos (estricto)
  - Origen en la base (Z = 0, punto más bajo de los patines)
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
DRIVE_DIR = Path(r"C:\Users\davin\OneDrive\Documentos\MODELOS JUEGO\caballito_balancin")
ARTIFACTS_DIR = Path(r"C:\Users\davin\.gemini\antigravity\brain\61207137-232d-4414-8860-f3fa45906692")

PROPS_MODELS_DIR.mkdir(parents=True, exist_ok=True)
SOURCE_DIR.mkdir(parents=True, exist_ok=True)
DRIVE_DIR.mkdir(parents=True, exist_ok=True)

GLB_PATH = PROPS_MODELS_DIR / "caballito_balancin.glb"
BLEND_PATH = SOURCE_DIR / "caballito_balancin.blend"
PY_PATH = SOURCE_DIR / "caballito_balancin.py"
PREVIEW_PATH = SOURCE_DIR / "caballito_balancin_preview.png"
ARTIFACT_PREVIEW = ARTIFACTS_DIR / "caballito_balancin_preview.png"

RENDER_SIZE = 800

# Paleta exacta de la referencia (sRGB)
PALETTE = {
    "DarkWood": (0.34, 0.31, 0.35),       # Madera grisácea oscura
    "SaddleCream": (0.90, 0.88, 0.76),    # Asiento/silla crema pastel
    "EyeDark": (0.18, 0.15, 0.18),        # Ojo triste tallado oscuro
    "AccentWood": (0.42, 0.38, 0.43),     # Manillar y acentos de madera
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


def create_box(name, size, location, rotation=(0, 0, 0), material=None):
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=location, rotation=rotation)
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = size
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if material:
        obj.data.materials.append(material)
    return obj


def build_curved_rocker(name, center_x, length=1.20, width=0.065, height=0.055, curve_depth=0.10, material=None):
    """Crea un patín curvado tipo mecedora que toca Z=0 en el punto central (Y=0)."""
    segments = 10
    verts = []
    faces = []
    half_w = width / 2.0
    half_l = length / 2.0
    
    for i in range(segments + 1):
        t = i / segments
        y = -half_l + t * length
        norm_y = y / half_l  # -1 a 1
        # Parábola: en y=0, z=0. En extremos, z = curve_depth
        z_bottom = curve_depth * (norm_y ** 2)
        z_top = z_bottom + height
        
        # 4 vértices por sección transversal
        verts.append((center_x - half_w, y, z_top))
        verts.append((center_x + half_w, y, z_top))
        verts.append((center_x + half_w, y, z_bottom))
        verts.append((center_x - half_w, y, z_bottom))
        
    for i in range(segments):
        idx0 = i * 4
        idx1 = (i + 1) * 4
        # Top
        faces.append((idx0 + 0, idx1 + 0, idx1 + 1, idx0 + 1))
        # Bottom
        faces.append((idx0 + 3, idx0 + 2, idx1 + 2, idx1 + 3))
        # Left
        faces.append((idx0 + 0, idx0 + 3, idx1 + 3, idx1 + 0))
        # Right
        faces.append((idx0 + 1, idx1 + 1, idx1 + 2, idx0 + 2))
        
    # Extremos
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


def build_horse(materials):
    parts_wood = []
    
    # 1. Patines curvos (Rockers)
    # Separación X = 0.44m (de X = -0.22 a X = +0.22)
    rocker_spacing_x = 0.22
    rocker_l = 1.15
    rocker_w = 0.06
    rocker_h = 0.05
    curve_z = 0.08
    
    rocker_l_obj = build_curved_rocker("Rocker_L", -rocker_spacing_x, length=rocker_l, width=rocker_w, height=rocker_h, curve_depth=curve_z, material=materials["DarkWood"])
    rocker_r_obj = build_curved_rocker("Rocker_R", rocker_spacing_x, length=rocker_l, width=rocker_w, height=rocker_h, curve_depth=curve_z, material=materials["DarkWood"])
    parts_wood.extend([rocker_l_obj, rocker_r_obj])
    
    # 2. Barras transversales / Reposapiés entre patines
    bar_thick = 0.04
    crossbar_front = create_box("Crossbar_Front", (2 * rocker_spacing_x, bar_thick, bar_thick), (0, 0.32, 0.05 + bar_thick / 2.0), material=materials["DarkWood"])
    crossbar_back = create_box("Crossbar_Back", (2 * rocker_spacing_x, bar_thick, bar_thick), (0, -0.32, 0.05 + bar_thick / 2.0), material=materials["DarkWood"])
    crossbar_mid = create_box("Crossbar_Mid", (2 * rocker_spacing_x, bar_thick, bar_thick), (0, 0.0, 0.02 + bar_thick / 2.0), material=materials["DarkWood"])
    parts_wood.extend([crossbar_front, crossbar_back, crossbar_mid])
    
    # 3. Cuatro patas inclinadas de madera
    # Altura de unión al cuerpo Z = 0.45m
    # Patas delanteras (+Y = 0.16) y traseras (-Y = -0.22)
    leg_w = 0.06
    leg_t = 0.045
    leg_h = 0.42
    
    # Patas delanteras: se inclinan ligeramente hacia adelante (+Y) y hacia afuera
    leg_fl = create_box("Leg_FL", (leg_w, leg_t, leg_h), (-0.17, 0.16, 0.26), rotation=(math.radians(-16), math.radians(-7), 0), material=materials["DarkWood"])
    leg_fr = create_box("Leg_FR", (leg_w, leg_t, leg_h), (0.17, 0.16, 0.26), rotation=(math.radians(-16), math.radians(7), 0), material=materials["DarkWood"])
    # Patas traseras: se inclinan hacia atrás (-Y)
    leg_bl = create_box("Leg_BL", (leg_w, leg_t, leg_h), (-0.17, -0.22, 0.26), rotation=(math.radians(16), math.radians(-7), 0), material=materials["DarkWood"])
    leg_br = create_box("Leg_BR", (leg_w, leg_t, leg_h), (0.17, -0.22, 0.26), rotation=(math.radians(16), math.radians(7), 0), material=materials["DarkWood"])
    parts_wood.extend([leg_fl, leg_fr, leg_bl, leg_br])
    
    # 4. Bloques de soporte del cuerpo
    # Viga longitudinal central entre las patas
    body_beam = create_box("Body_Beam", (0.24, 0.52, 0.07), (0, -0.03, 0.44), material=materials["DarkWood"])
    parts_wood.append(body_beam)
    
    # Soporte frontal del pecho
    chest_block = create_box("Chest_Block", (0.16, 0.12, 0.14), (0, 0.21, 0.49), material=materials["DarkWood"])
    parts_wood.append(chest_block)
    
    # 5. Cuello y Pecho del Caballo
    # Sube en ángulo hacia +Y
    neck = create_box("Horse_Neck", (0.12, 0.20, 0.32), (0, 0.24, 0.63), rotation=(math.radians(-24), 0, 0), material=materials["DarkWood"])
    parts_wood.append(neck)
    
    # Crin escalonada de madera tallada (bloque a lo largo del cuello en la parte posterior)
    mane = create_box("Horse_Mane", (0.05, 0.10, 0.26), (0, 0.17, 0.67), rotation=(math.radians(-24), 0, 0), material=materials["DarkWood"])
    parts_wood.append(mane)
    
    # 6. Cabeza del Caballo
    # Cabeza tallada inclinada hacia el frente (+Y)
    head = create_box("Horse_Head", (0.13, 0.24, 0.18), (0, 0.34, 0.76), rotation=(math.radians(18), 0, 0), material=materials["DarkWood"])
    # Hocico cónico/truncado
    snout = create_box("Horse_Snout", (0.11, 0.12, 0.12), (0, 0.44, 0.72), rotation=(math.radians(24), 0, 0), material=materials["DarkWood"])
    parts_wood.extend([head, snout])
    
    # Orejas talladas
    ear_l = create_box("Ear_L", (0.03, 0.04, 0.08), (-0.045, 0.27, 0.88), rotation=(math.radians(-10), math.radians(-12), 0), material=materials["DarkWood"])
    ear_r = create_box("Ear_R", (0.03, 0.04, 0.08), (0.045, 0.27, 0.88), rotation=(math.radians(-10), math.radians(12), 0), material=materials["DarkWood"])
    parts_wood.extend([ear_l, ear_r])
    
    # Manillar / Clavija cilíndrica de agarre para las manos
    bpy.ops.mesh.primitive_cylinder_add(radius=0.022, depth=0.34, location=(0, 0.28, 0.71), rotation=(0, math.radians(90), 0))
    handlebar = bpy.context.active_object
    handlebar.name = "Handlebar"
    handlebar.data.materials.append(materials["AccentWood"])
    parts_wood.append(handlebar)
    
    # Ojos tallados oscuros (rehundidos en los lados de la cabeza)
    eye_l = create_box("Eye_L", (0.015, 0.04, 0.03), (-0.068, 0.33, 0.76), rotation=(math.radians(18), 0, 0), material=materials["EyeDark"])
    eye_r = create_box("Eye_R", (0.015, 0.04, 0.03), (0.068, 0.33, 0.76), rotation=(math.radians(18), 0, 0), material=materials["EyeDark"])
    parts_wood.extend([eye_l, eye_r])
    
    # 7. Cola de madera
    tail = create_box("Horse_Tail", (0.04, 0.08, 0.16), (0, -0.31, 0.50), rotation=(math.radians(28), 0, 0), material=materials["DarkWood"])
    parts_wood.append(tail)
    
    # Unir todas las piezas de madera en "Horse_Body"
    bpy.ops.object.select_all(action='DESELECT')
    for p in parts_wood:
        p.select_set(True)
    bpy.context.view_layer.objects.active = parts_wood[0]
    bpy.ops.object.join()
    horse_body = bpy.context.active_object
    horse_body.name = "Horse_Body"
    
    # 8. Asiento / Montura en crema pastel (Saddle)
    # Base del asiento contorneada
    saddle_seat = create_box("Saddle_Seat", (0.22, 0.38, 0.045), (0, -0.06, 0.495), material=materials["SaddleCream"])
    bev_s = saddle_seat.modifiers.new(name="Bevel", type='BEVEL')
    bev_s.width = 0.018
    bev_s.segments = 2
    bpy.context.view_layer.objects.active = saddle_seat
    bpy.ops.object.modifier_apply(modifier="Bevel")
    
    # Respaldo pequeño trasero del asiento
    saddle_back = create_box("Saddle_Back", (0.16, 0.05, 0.12), (0, -0.21, 0.56), rotation=(math.radians(-6), 0, 0), material=materials["SaddleCream"])
    bev_sb = saddle_back.modifiers.new(name="Bevel", type='BEVEL')
    bev_sb.width = 0.015
    bev_sb.segments = 1
    bpy.context.view_layer.objects.active = saddle_back
    bpy.ops.object.modifier_apply(modifier="Bevel")
    
    # Unir asiento y respaldo en "Horse_Saddle"
    bpy.ops.object.select_all(action='DESELECT')
    saddle_seat.select_set(True)
    saddle_back.select_set(True)
    bpy.context.view_layer.objects.active = saddle_seat
    bpy.ops.object.join()
    saddle = bpy.context.active_object
    saddle.name = "Horse_Saddle"
    
    return [horse_body, saddle]


def setup_preview_scene(parts):
    # Luz suave y atmosférica
    light_data = bpy.data.lights.new(name="KeyLight", type='SUN')
    light_data.energy = 3.5
    light_data.color = (0.95, 0.92, 0.88)
    light_obj = bpy.data.objects.new("KeyLight", light_data)
    light_obj.rotation_euler = (math.radians(50), math.radians(20), math.radians(-35))
    bpy.context.collection.objects.link(light_obj)

    fill_data = bpy.data.lights.new(name="FillLight", type='SUN')
    fill_data.energy = 1.2
    fill_data.color = (0.60, 0.65, 0.85)
    fill_obj = bpy.data.objects.new("FillLight", fill_data)
    fill_obj.rotation_euler = (math.radians(-40), math.radians(-30), math.radians(140))
    bpy.context.collection.objects.link(fill_obj)

    rim_data = bpy.data.lights.new(name="RimLight", type='POINT')
    rim_data.energy = 70.0
    rim_data.color = (0.7, 0.8, 1.0)
    rim_obj = bpy.data.objects.new("RimLight", rim_data)
    rim_obj.location = (0, -1.6, 1.8)
    bpy.context.collection.objects.link(rim_obj)

    center = (0.0, 0.05, 0.45)
    dist = 2.4

    cams = []

    # 1. Cámara frontal (+Y mirando hacia el pecho/cabeza del caballo)
    cam_front = bpy.data.cameras.new("Cam_Front")
    cam_front.lens = 50
    o_front = bpy.data.objects.new("Cam_Front_Obj", cam_front)
    o_front.location = (0.0, dist + 0.1, center[2] + 0.2)
    o_front.rotation_euler = (math.radians(82), 0.0, math.radians(180))
    bpy.context.collection.objects.link(o_front)
    cams.append(("front", o_front))

    # 2. Cámara isométrica 3/4 (perspectiva exacta de la referencia de Nano Banana)
    cam_iso = bpy.data.cameras.new("Cam_Iso")
    cam_iso.lens = 50
    o_iso = bpy.data.objects.new("Cam_Iso_Obj", cam_iso)
    iso_dist = 2.3
    o_iso.location = (iso_dist * 0.7, iso_dist * 0.7, 1.6)
    direction = Vector(center) - Vector(o_iso.location)
    rot_quat = direction.to_track_quat('-Z', 'Y')
    o_iso.rotation_euler = rot_quat.to_euler()
    bpy.context.collection.objects.link(o_iso)
    cams.append(("isometric", o_iso))

    # 3. Cámara lateral (+X)
    cam_side = bpy.data.cameras.new("Cam_Side")
    cam_side.lens = 50
    o_side = bpy.data.objects.new("Cam_Side_Obj", cam_side)
    o_side.location = (dist, 0.0, center[2] + 0.15)
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
    out = bpy.data.images.new("caballito_preview", sheet.shape[1], sheet.shape[0], alpha=False)
    out.pixels.foreach_set(sheet.ravel())
    out.filepath_raw = str(output_path)
    out.file_format = 'PNG'
    out.save()
    bpy.data.images.remove(out)


def main():
    print("[caballito_balancin] Iniciando modelado oficial del Caballito Balancín (Prop #2)...")
    clean_scene()
    materials = make_materials()
    parts = build_horse(materials)

    part_names = [p.name for p in parts]
    print(f"[caballito_balancin] Partes construidas ({len(parts)}): {', '.join(part_names)}")

    total_tris = 0
    for p in parts:
        p.data.calc_loop_triangles()
        t = len(p.data.loop_triangles)
        total_tris += t
        print(f"  • {p.name}: {t} tris, loc={p.location}, dims={p.dimensions}")
    print(f"[caballito_balancin] Triángulos totales: {total_tris} (Límite: <= 800)")
    assert total_tris <= 800, f"Excede límite de 800 tris: {total_tris}"

    # Exportar GLB oficial en assets/models/props/caballito_balancin.glb
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
    print(f"[caballito_balancin] Exportado GLB oficial -> {GLB_PATH}")

    # Renderizar vista previa de 3 paneles
    cameras = setup_preview_scene(parts)
    render_preview_sheet(cameras, PREVIEW_PATH)
    print(f"[caballito_balancin] Vista previa generada -> {PREVIEW_PATH}")

    # Guardar archivo .blend oficial
    bpy.ops.wm.save_as_mainfile(filepath=str(BLEND_PATH), compress=True)
    print(f"[caballito_balancin] Archivo .blend oficial -> {BLEND_PATH}")

    # Copiar script fuente a assets/source/blender/caballito_balancin.py
    this_script = Path(__file__).resolve()
    shutil.copy2(this_script, PY_PATH)
    print(f"[caballito_balancin] Script fuente guardado en -> {PY_PATH}")

    # Copiar entrega a la carpeta designada de David: MODELOS JUEGO/caballito_balancin/
    shutil.copy2(GLB_PATH, DRIVE_DIR / "caballito_balancin.glb")
    shutil.copy2(BLEND_PATH, DRIVE_DIR / "caballito_balancin.blend")
    shutil.copy2(PY_PATH, DRIVE_DIR / "caballito_balancin.py")
    shutil.copy2(PREVIEW_PATH, DRIVE_DIR / "caballito_balancin_preview.png")
    shutil.copy2(PREVIEW_PATH, ARTIFACT_PREVIEW)
    print(f"[caballito_balancin] Copia guardada en carpeta de David -> {DRIVE_DIR}")

    print("[caballito_balancin] ¡Modelado de caballito balancín completado con éxito!")


if __name__ == "__main__":
    main()
