"""Generador oficial de oso_gigante.glb (Prop #5 de The Cradle) para LULLABY:
Referencia visual exacta (Nuevo concepto aprobado):
  - Oso de peluche gigante tétrico estilo horror infantil / patchwork
  - Orejas de oso clásicas redondas en la parte superior-lateral de la cabeza (forma de cuenco)
  - Ojos saltones desorbitados con pupilas oscuras mirando fijamente
  - Hocico redondeado en crema con nariz negra
  - Boca abierta desencajada con cavidad roja oscura y colmillos/dientes puntiagudos
  - Torso sentado en forma de pera con botones y costuras
  - Brazos y piernas abiertas hacia el frente apoyadas en el suelo con plantas de las patas en rojo oscuro desgastado

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
DRIVE_DIR = Path(r"C:\Users\davin\OneDrive\Documentos\MODELOS JUEGO\oso_gigante")
ARTIFACTS_DIR = Path(r"C:\Users\davin\.gemini\antigravity\brain\61207137-232d-4414-8860-f3fa45906692")

PROPS_MODELS_DIR.mkdir(parents=True, exist_ok=True)
SOURCE_DIR.mkdir(parents=True, exist_ok=True)
DRIVE_DIR.mkdir(parents=True, exist_ok=True)

GLB_PATH = PROPS_MODELS_DIR / "oso_gigante.glb"
BLEND_PATH = SOURCE_DIR / "oso_gigante.blend"
PY_PATH = SOURCE_DIR / "oso_gigante.py"
PREVIEW_PATH = SOURCE_DIR / "oso_gigante_preview.png"
ARTIFACT_PREVIEW = ARTIFACTS_DIR / "oso_gigante_preview.png"

RENDER_SIZE = 800

# Paleta exacta de la referencia (sRGB)
PALETTE = {
    "PlushTan": (0.60, 0.53, 0.44),       # Felpa ocre/beige claro envejecida
    "MuzzleCream": (0.86, 0.82, 0.72),    # Hocico crema claro
    "MouthRed": (0.50, 0.14, 0.14),       # Cavidad bucal y plantas rojas desgastadas
    "EyeWhite": (0.92, 0.90, 0.86),       # Esclerótica de ojos saltones
    "DarkDetail": (0.12, 0.10, 0.10),     # Pupilas, botones y nariz oscura
    "TeethIvory": (0.86, 0.84, 0.76),     # Dientes afilados de marfil
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
                    inp.default_value = 0.88
                elif inp.name == "Specular IOR Level":
                    inp.default_value = 0.15
        materials[name] = mat
    return materials


def set_smooth(obj):
    for f in obj.data.polygons:
        f.use_smooth = True


def create_uv_sphere(name, radius, segments, rings, location, scale=(1, 1, 1), rotation=(0, 0, 0), material=None):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=segments, ring_count=rings, radius=radius, location=location, rotation=rotation)
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = scale
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    set_smooth(obj)
    if material:
        obj.data.materials.append(material)
    return obj


def create_cylinder(name, radius, depth, vertices, location, rotation=(0, 0, 0), scale=(1, 1, 1), material=None):
    bpy.ops.mesh.primitive_cylinder_add(vertices=vertices, radius=radius, depth=depth, location=location, rotation=rotation)
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = scale
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    set_smooth(obj)
    if material:
        obj.data.materials.append(material)
    return obj


def create_box(name, size, location, rotation=(0, 0, 0), material=None):
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=location, rotation=rotation)
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = size
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    set_smooth(obj)
    if material:
        obj.data.materials.append(material)
    return obj


def build_bear_ear(name, location, rotation, material_outer):
    """Crea una oreja redonda clásica de oso de peluche (semicírculo tipo cuenco low-poly)."""
    ear = create_uv_sphere(name, radius=0.14, segments=6, rings=3, location=location, scale=(0.32, 0.95, 0.95), rotation=rotation, material=material_outer)
    return ear


def build_creepy_bear(materials):
    parts_head = []
    parts_body = []
    parts_limbs = []
    
    # 1. Cabeza y Rostro Tétrico
    head_z = 0.94
    head = create_uv_sphere("Head_Main", radius=0.38, segments=8, rings=5, location=(0, 0.02, head_z), scale=(1.02, 0.98, 1.00), rotation=(math.radians(5), 0, 0), material=materials["PlushTan"])
    parts_head.append(head)
    
    # Orejas clásicas de oso redondas en la parte superior-lateral de la cabeza (forma de cuenco)
    ear_l = build_bear_ear("Ear_Bear_L", location=(-0.30, 0.02, head_z + 0.30), rotation=(math.radians(10), math.radians(-32), math.radians(-15)), material_outer=materials["PlushTan"])
    ear_r = build_bear_ear("Ear_Bear_R", location=(0.30, 0.02, head_z + 0.30), rotation=(math.radians(10), math.radians(32), math.radians(15)), material_outer=materials["PlushTan"])
    parts_head.extend([ear_l, ear_r])
    
    # Hocico prominente en crema claro (Muzzle)
    muzzle_z = head_z - 0.05
    muzzle = create_uv_sphere("Muzzle", radius=0.18, segments=6, rings=4, location=(0, 0.28, muzzle_z), scale=(0.95, 0.80, 0.70), rotation=(math.radians(-8), 0, 0), material=materials["MuzzleCream"])
    parts_head.append(muzzle)
    
    # Nariz negra en la punta del hocico
    nose = create_uv_sphere("Nose", radius=0.045, segments=5, rings=3, location=(0, 0.41, muzzle_z + 0.03), scale=(1.0, 0.65, 0.75), material=materials["DarkDetail"])
    parts_head.append(nose)
    
    # Mandíbula inferior abierta desencajada (Jaw)
    jaw_z = head_z - 0.22
    jaw = create_uv_sphere("Jaw_Lower", radius=0.17, segments=6, rings=3, location=(0, 0.22, jaw_z), scale=(0.90, 0.95, 0.55), rotation=(math.radians(24), 0, 0), material=materials["MuzzleCream"])
    parts_head.append(jaw)
    
    # Cavidad bucal interior roja profunda (Mouth Cavity)
    mouth_interior = create_uv_sphere("Mouth_Interior", radius=0.15, segments=5, rings=3, location=(0, 0.21, head_z - 0.14), scale=(0.85, 0.85, 0.65), rotation=(math.radians(10), 0, 0), material=materials["MouthRed"])
    parts_head.append(mouth_interior)
    
    # Colmillos afilados (2 superiores, 2 inferiores)
    for i in [-0.05, 0.05]:
        t_up = create_cylinder(f"Tooth_Up_{i}", radius=0.012, depth=0.035, vertices=4, location=(i, 0.33, head_z - 0.11), rotation=(math.radians(20), 0, 0), material=materials["TeethIvory"])
        t_dn = create_cylinder(f"Tooth_Dn_{i}", radius=0.012, depth=0.035, vertices=4, location=(i, 0.30, jaw_z + 0.06), rotation=(math.radians(-20), 0, 0), material=materials["TeethIvory"])
        parts_head.extend([t_up, t_dn])
        
    # Ojos saltones desorbitados (Grandes globos blancos con pupilas oscuras frontales)
    eye_z = head_z + 0.12
    eye_y = 0.27
    eye_l = create_uv_sphere("Eye_L", radius=0.09, segments=6, rings=4, location=(-0.16, eye_y, eye_z), scale=(0.90, 0.95, 0.95), material=materials["EyeWhite"])
    pupil_l = create_cylinder("Pupil_L", radius=0.032, depth=0.015, vertices=5, location=(-0.15, eye_y + 0.08, eye_z - 0.01), rotation=(math.radians(75), 0, 0), material=materials["DarkDetail"])
    
    eye_r = create_uv_sphere("Eye_R", radius=0.09, segments=6, rings=4, location=(0.16, eye_y, eye_z), scale=(0.90, 0.95, 0.95), material=materials["EyeWhite"])
    pupil_r = create_cylinder("Pupil_R", radius=0.032, depth=0.015, vertices=5, location=(0.15, eye_y + 0.08, eye_z - 0.01), rotation=(math.radians(75), 0, 0), material=materials["DarkDetail"])
    parts_head.extend([eye_l, pupil_l, eye_r, pupil_r])
    
    # 2. Torso en forma de pera apoyado en el suelo (Bear_Body)
    torso_z = 0.42
    torso_base = create_uv_sphere("Torso_Base", radius=0.46, segments=9, rings=5, location=(0, -0.04, torso_z), scale=(0.96, 0.92, 0.90), rotation=(math.radians(-8), 0, 0), material=materials["PlushTan"])
    torso_chest = create_uv_sphere("Torso_Chest", radius=0.38, segments=8, rings=4, location=(0, 0.00, torso_z + 0.26), scale=(0.88, 0.85, 0.85), rotation=(math.radians(-4), 0, 0), material=materials["PlushTan"])
    parts_body.extend([torso_base, torso_chest])
    
    # Botones oscuros cosidos en el pecho
    btn1 = create_cylinder("Button_Chest_1", radius=0.032, depth=0.018, vertices=6, location=(-0.08, 0.28, torso_z + 0.22), rotation=(math.radians(75), 0, 0), material=materials["DarkDetail"])
    btn2 = create_cylinder("Button_Chest_2", radius=0.032, depth=0.018, vertices=6, location=(0.08, 0.28, torso_z + 0.22), rotation=(math.radians(75), 0, 0), material=materials["DarkDetail"])
    parts_body.extend([btn1, btn2])
    
    # 3. Extremidades sentadas con plantas rojas desgastadas (Bear_Limbs)
    # Brazo izquierdo (-X): apoyado hacia un lado en el suelo
    arm_l = create_cylinder("Arm_L", radius=0.13, depth=0.55, vertices=6, location=(-0.38, 0.10, 0.42), rotation=(math.radians(35), math.radians(-24), math.radians(-10)), material=materials["PlushTan"])
    paw_l = create_uv_sphere("Paw_L", radius=0.15, segments=6, rings=4, location=(-0.44, 0.28, 0.22), scale=(0.95, 1.05, 0.85), material=materials["PlushTan"])
    parts_limbs.extend([arm_l, paw_l])
    
    # Brazo derecho (+X): apoyado hacia un lado en el suelo
    arm_r = create_cylinder("Arm_R", radius=0.13, depth=0.55, vertices=6, location=(0.38, 0.10, 0.42), rotation=(math.radians(35), math.radians(24), math.radians(10)), material=materials["PlushTan"])
    paw_r = create_uv_sphere("Paw_R", radius=0.15, segments=6, rings=4, location=(0.44, 0.28, 0.22), scale=(0.95, 1.05, 0.85), material=materials["PlushTan"])
    parts_limbs.extend([arm_r, paw_r])
    
    # Piernas extendidas abiertas en el suelo hacia el frente (+Y)
    # Pierna izquierda (-X)
    leg_l = create_cylinder("Leg_L", radius=0.15, depth=0.52, vertices=6, location=(-0.28, 0.35, 0.18), rotation=(math.radians(-72), math.radians(-16), math.radians(-20)), material=materials["PlushTan"])
    foot_l = create_uv_sphere("Foot_L", radius=0.18, segments=6, rings=4, location=(-0.34, 0.60, 0.18), scale=(1.05, 0.85, 1.15), material=materials["PlushTan"])
    pad_l = create_cylinder("Pad_L", radius=0.13, depth=0.03, vertices=6, location=(-0.35, 0.67, 0.18), rotation=(math.radians(-90), 0, math.radians(-22)), material=materials["MouthRed"])
    parts_limbs.extend([leg_l, foot_l, pad_l])
    
    # Pierna derecha (+X)
    leg_r = create_cylinder("Leg_R", radius=0.15, depth=0.52, vertices=6, location=(0.28, 0.35, 0.18), rotation=(math.radians(-72), math.radians(16), math.radians(20)), material=materials["PlushTan"])
    foot_r = create_uv_sphere("Foot_R", radius=0.18, segments=6, rings=4, location=(0.34, 0.60, 0.18), scale=(1.05, 0.85, 1.15), material=materials["PlushTan"])
    pad_r = create_cylinder("Pad_R", radius=0.13, depth=0.03, vertices=6, location=(0.35, 0.67, 0.18), rotation=(math.radians(-90), 0, math.radians(22)), material=materials["MouthRed"])
    parts_limbs.extend([leg_r, foot_r, pad_r])
    
    # 4. Unir en 3 partes limpias
    # Bear_Head
    bpy.ops.object.select_all(action='DESELECT')
    for p in parts_head:
        p.select_set(True)
    bpy.context.view_layer.objects.active = parts_head[0]
    bpy.ops.object.join()
    head_obj = bpy.context.active_object
    head_obj.name = "Bear_Head"
    
    # Bear_Body
    bpy.ops.object.select_all(action='DESELECT')
    for p in parts_body:
        p.select_set(True)
    bpy.context.view_layer.objects.active = parts_body[0]
    bpy.ops.object.join()
    body_obj = bpy.context.active_object
    body_obj.name = "Bear_Body"
    
    # Bear_Limbs
    bpy.ops.object.select_all(action='DESELECT')
    for p in parts_limbs:
        p.select_set(True)
    bpy.context.view_layer.objects.active = parts_limbs[0]
    bpy.ops.object.join()
    limbs_obj = bpy.context.active_object
    limbs_obj.name = "Bear_Limbs"
    
    # Ajustar Z = 0 exacto en el suelo
    all_objs = [head_obj, body_obj, limbs_obj]
    min_z = min(obj.bound_box[0][2] + obj.location[2] for obj in all_objs)
    if abs(min_z) > 0.001:
        for obj in all_objs:
            obj.location.z -= min_z
            
    return [head_obj, body_obj, limbs_obj]


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
    rim_data.energy = 80.0
    rim_data.color = (0.7, 0.8, 1.0)
    rim_obj = bpy.data.objects.new("RimLight", rim_data)
    rim_obj.location = (0, -1.8, 2.0)
    bpy.context.collection.objects.link(rim_obj)

    center = (0.0, 0.15, 0.65)
    dist = 2.8

    cams = []

    # 1. Cámara frontal
    cam_front = bpy.data.cameras.new("Cam_Front")
    cam_front.lens = 50
    o_front = bpy.data.objects.new("Cam_Front_Obj", cam_front)
    o_front.location = (0.0, dist + 0.1, center[2] + 0.1)
    o_front.rotation_euler = (math.radians(84), 0.0, math.radians(180))
    bpy.context.collection.objects.link(o_front)
    cams.append(("front", o_front))

    # 2. Cámara isométrica 3/4
    cam_iso = bpy.data.cameras.new("Cam_Iso")
    cam_iso.lens = 50
    o_iso = bpy.data.objects.new("Cam_Iso_Obj", cam_iso)
    iso_dist = 2.7
    o_iso.location = (iso_dist * 0.7, iso_dist * 0.7, 1.8)
    direction = Vector(center) - Vector(o_iso.location)
    rot_quat = direction.to_track_quat('-Z', 'Y')
    o_iso.rotation_euler = rot_quat.to_euler()
    bpy.context.collection.objects.link(o_iso)
    cams.append(("isometric", o_iso))

    # 3. Cámara lateral
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
    out = bpy.data.images.new("oso_preview", sheet.shape[1], sheet.shape[0], alpha=False)
    out.pixels.foreach_set(sheet.ravel())
    out.filepath_raw = str(output_path)
    out.file_format = 'PNG'
    out.save()
    bpy.data.images.remove(out)


def main():
    print("[oso_gigante] Iniciando modelado oficial del Oso Gigante (Prop #5 - Nuevo diseño)...")
    clean_scene()
    materials = make_materials()
    parts = build_creepy_bear(materials)

    part_names = [p.name for p in parts]
    print(f"[oso_gigante] Partes construidas ({len(parts)}): {', '.join(part_names)}")

    total_tris = 0
    for p in parts:
        p.data.calc_loop_triangles()
        t = len(p.data.loop_triangles)
        total_tris += t
        print(f"  • {p.name}: {t} tris, loc={p.location}, dims={p.dimensions}")
    print(f"[oso_gigante] Triángulos totales: {total_tris} (Límite: <= 800)")
    assert total_tris <= 800, f"Excede límite de 800 tris: {total_tris}"

    # Exportar GLB oficial en assets/models/props/oso_gigante.glb
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
    print(f"[oso_gigante] Exportado GLB oficial -> {GLB_PATH}")

    # Renderizar vista previa de 3 paneles
    cameras = setup_preview_scene(parts)
    render_preview_sheet(cameras, PREVIEW_PATH)
    print(f"[oso_gigante] Vista previa generada -> {PREVIEW_PATH}")

    # Guardar archivo .blend oficial
    bpy.ops.wm.save_as_mainfile(filepath=str(BLEND_PATH), compress=True)
    print(f"[oso_gigante] Archivo .blend oficial -> {BLEND_PATH}")

    # Copiar script fuente a assets/source/blender/oso_gigante.py
    this_script = Path(__file__).resolve()
    shutil.copy2(this_script, PY_PATH)
    print(f"[oso_gigante] Script fuente guardado en -> {PY_PATH}")

    # Copiar entrega a la carpeta designada de David: MODELOS JUEGO/oso_gigante/
    shutil.copy2(GLB_PATH, DRIVE_DIR / "oso_gigante.glb")
    shutil.copy2(BLEND_PATH, DRIVE_DIR / "oso_gigante.blend")
    shutil.copy2(PY_PATH, DRIVE_DIR / "oso_gigante.py")
    shutil.copy2(PREVIEW_PATH, DRIVE_DIR / "oso_gigante_preview.png")
    shutil.copy2(PREVIEW_PATH, ARTIFACT_PREVIEW)
    print(f"[oso_gigante] Copia guardada en carpeta de David -> {DRIVE_DIR}")

    print("[oso_gigante] ¡Modelado de oso gigante completado con éxito!")


if __name__ == "__main__":
    main()
