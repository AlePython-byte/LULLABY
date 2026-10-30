"""Generador de Piezas Estructurales y Sala del Jefe/Tesoro para LULLABY:
Piezas incluidas:
1. marco_puerta.glb - Hueco libre de 3.0 x 3.0 m, profundidad 1.0 m para muro
2. puerta_barrotes.glb - Puerta cerrada tipo cuna, 3.0 x 3.0 x 0.3 m
3. columna_esquina.glb - Columna esquinera de 0.6 x 0.6 x 3.0 m
4. mecedora_gigante.glb - Mecedora monumental para sala de jefe (~3.0 m alto, <= 1200 tris)
5. cuna_gigante.glb - Cuna monumental con barrotes para sala de jefe (~3.5 m alto, <= 1200 tris)
6. pedestal_tesoro.glb - Pedestal de exhibición de tesoro (~0.6 m alto, 1.0 m diám, <= 800 tris)

Reglas técnicas:
- Medidas exactas de la sección 6 del plan
- Origen en base (Z = 0 en el suelo)
- Frente hacia +Y en Blender (hacia -Z en Godot)
- Colores planos Principled BSDF sRGB -> Linear
- Exportación GLB y guardado .blend sin .blend1
- Render de vista previa
"""

import math
import shutil
from pathlib import Path
import bpy
from mathutils import Vector, Matrix

LULLABY_ROOT = Path(r"C:\LULLABY")
PROPS_DIR = LULLABY_ROOT / "assets" / "models" / "props"
SOURCE_DIR = LULLABY_ROOT / "assets" / "source" / "blender"
DRIVE_BASE = Path(r"C:\Users\davin\OneDrive\Documentos\MODELOS JUEGO")
ARTIFACTS_DIR = Path(r"C:\Users\davin\.gemini\antigravity\brain\61207137-232d-4414-8860-f3fa45906692")

PROPS_DIR.mkdir(parents=True, exist_ok=True)
SOURCE_DIR.mkdir(parents=True, exist_ok=True)
DRIVE_BASE.mkdir(parents=True, exist_ok=True)

bpy.context.preferences.filepaths.save_version = 0

PALETTE = {
    # Maderas
    "WoodLight": (0.84, 0.72, 0.54),
    "WoodMedium": (0.65, 0.48, 0.32),
    "WoodDark": (0.38, 0.26, 0.18),
    "WoodEbony": (0.22, 0.18, 0.16),
    # Colores pastel apagados / infantiles
    "PastelRed": (0.82, 0.32, 0.32),
    "PastelOrange": (0.88, 0.54, 0.28),
    "PastelYellow": (0.92, 0.82, 0.38),
    "PastelGreen": (0.44, 0.68, 0.48),
    "PastelCyan": (0.35, 0.70, 0.75),
    "PastelBlue": (0.38, 0.52, 0.78),
    "PastelPurple": (0.62, 0.45, 0.72),
    "PastelPink": (0.88, 0.55, 0.68),
    # Neutros y Mármol
    "CreamWhite": (0.92, 0.89, 0.82),
    "WarmParchment": (0.86, 0.80, 0.70),
    "MarbleVelvet": (0.85, 0.28, 0.35),
    "GoldBrass": (0.78, 0.64, 0.28),
    "DarkMetal": (0.22, 0.20, 0.22),
}


def srgb_to_linear(v: float) -> float:
    return v / 12.92 if v <= 0.04045 else ((v + 0.055) / 1.055) ** 2.4


def clean_scene():
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete(use_global=False)
    for block in [bpy.data.meshes, bpy.data.materials, bpy.data.cameras, bpy.data.lights]:
        for item in block:
            if item.users == 0:
                block.remove(item)


def get_material(name: str):
    if name in bpy.data.materials:
        return bpy.data.materials[name]
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    srgb = PALETTE.get(name, (0.7, 0.7, 0.7))
    linear = tuple(srgb_to_linear(c) for c in srgb)
    bsdf = next((n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None)
    if bsdf:
        for inp in bsdf.inputs:
            if inp.name == "Base Color":
                inp.default_value = (*linear, 1.0)
            elif inp.name == "Roughness":
                inp.default_value = 0.75
            elif inp.name == "Specular IOR Level":
                inp.default_value = 0.2
    return mat


def set_smooth(obj):
    for f in obj.data.polygons:
        f.use_smooth = True


def finalize_model(name: str, target_dir_name: str, max_tris: int = 800):
    mesh_objs = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    if not mesh_objs:
        print(f"ERROR: No mesh objects found for {name}")
        return

    # Ajuste Z=0 en la base exacta
    all_min_z = min(min([(o.matrix_world @ v.co).z for v in o.data.vertices]) for o in mesh_objs)
    for o in mesh_objs:
        o.location.z -= all_min_z
        bpy.context.view_layer.objects.active = o
        o.select_set(True)
        bpy.ops.object.transform_apply(location=True, rotation=False, scale=False)

    bpy.ops.object.select_all(action='DESELECT')
    for o in mesh_objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = mesh_objs[0]
    if len(mesh_objs) > 1:
        bpy.ops.object.join()
    final_obj = bpy.context.active_object
    final_obj.name = name

    total_tris = sum(len(p.vertices) - 2 for p in final_obj.data.polygons)
    bbox = final_obj.bound_box
    height = max(b[2] for b in bbox) - min(b[2] for b in bbox)
    width_x = max(b[0] for b in bbox) - min(b[0] for b in bbox)
    depth_y = max(b[1] for b in bbox) - min(b[1] for b in bbox)
    status = "OK" if total_tris <= max_tris else f"EXCEDIDO ({total_tris} > {max_tris})"
    print(f"\n>>> MODELO: {name}")
    print(f"    Triángulos: {total_tris} (Presupuesto <= {max_tris}: {status})")
    print(f"    Dimensiones: Ancho {width_x:.2f}m x Prof {depth_y:.2f}m x Alto {height:.2f}m")

    # Luces y Cámara
    center_z = height * 0.5
    cam_dist = max(height, width_x, depth_y) * 2.1
    
    sun = bpy.data.lights.new(name="Sun", type='SUN')
    sun.energy = 3.5
    sun.color = (0.98, 0.96, 0.92)
    sun_obj = bpy.data.objects.new("Sun", sun)
    sun_obj.rotation_euler = (math.radians(50), math.radians(20), math.radians(-35))
    bpy.context.collection.objects.link(sun_obj)
    
    fill = bpy.data.lights.new(name="Fill", type='SUN')
    fill.energy = 1.2
    fill.color = (0.65, 0.70, 0.85)
    fill_obj = bpy.data.objects.new("Fill", fill)
    fill_obj.rotation_euler = (math.radians(-40), math.radians(-30), math.radians(145))
    bpy.context.collection.objects.link(fill_obj)

    cam = bpy.data.cameras.new("PreviewCam")
    cam.lens = 50
    cam_obj = bpy.data.objects.new("PreviewCam", cam)
    cam_obj.location = (cam_dist * 0.75, -cam_dist * 0.75, center_z + cam_dist * 0.35)
    target = Vector((0, 0, center_z))
    direction = target - cam_obj.location
    cam_obj.rotation_euler = direction.to_track_quat('-Z', 'Y').to_euler()
    bpy.context.collection.objects.link(cam_obj)
    bpy.context.scene.camera = cam_obj

    # Rutas
    glb_path = PROPS_DIR / f"{name}.glb"
    blend_path = SOURCE_DIR / f"{name}.blend"
    preview_path = SOURCE_DIR / f"{name}_preview.png"
    drive_dir = DRIVE_BASE / target_dir_name
    drive_dir.mkdir(parents=True, exist_ok=True)
    drive_glb = drive_dir / f"{name}.glb"
    drive_blend = drive_dir / f"{name}.blend"
    drive_preview = drive_dir / f"{name}_preview.png"
    artifact_preview = ARTIFACTS_DIR / f"{name}_preview.png"

    bpy.ops.object.select_all(action='DESELECT')
    final_obj.select_set(True)
    bpy.ops.export_scene.gltf(
        filepath=str(glb_path),
        export_format='GLB',
        use_selection=True,
        export_apply=True,
        export_yup=True
    )
    bpy.ops.wm.save_as_mainfile(filepath=str(blend_path))

    bpy.context.scene.render.resolution_x = 800
    bpy.context.scene.render.resolution_y = 800
    bpy.context.scene.render.filepath = str(preview_path)
    bpy.ops.render.render(write_still=True)

    shutil.copy2(glb_path, drive_glb)
    shutil.copy2(blend_path, drive_blend)
    shutil.copy2(preview_path, drive_preview)
    shutil.copy2(preview_path, artifact_preview)


# ==============================================================================
# 1. marco_puerta.glb (Hueco 3.0 x 3.0 m, profundidad 1.0 m)
# ==============================================================================
def build_marco_puerta():
    clean_scene()
    
    opening_w = 3.00
    opening_h = 3.00
    wall_depth = 1.00
    frame_thick = 0.20
    
    mat_wood = get_material("WoodMedium")
    mat_trim = get_material("WoodDark")
    
    # Jambas laterales (izquierda y derecha)
    for sx in [-1, 1]:
        jx = sx * (opening_w * 0.5 + frame_thick * 0.5)
        bpy.ops.mesh.primitive_cube_add(size=1.0, location=(jx, 0, opening_h * 0.5))
        jamb = bpy.context.active_object
        jamb.scale = (frame_thick, wall_depth, opening_h)
        bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
        jamb.data.materials.append(mat_wood)
        
        # Molduras en las esquinas frontales y traseras
        for sy in [-1, 1]:
            bpy.ops.mesh.primitive_cube_add(size=1.0, location=(jx + sx * 0.03, sy * (wall_depth * 0.5), opening_h * 0.5))
            mould = bpy.context.active_object
            mould.scale = (frame_thick + 0.08, 0.08, opening_h + 0.04)
            bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
            mould.data.materials.append(mat_trim)

    # Dintel superior
    lintel_z = opening_h + frame_thick * 0.5
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0, 0, lintel_z))
    lintel = bpy.context.active_object
    lintel.scale = (opening_w + frame_thick * 2, wall_depth, frame_thick)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    lintel.data.materials.append(mat_wood)
    
    # Molduras del dintel
    for sy in [-1, 1]:
        bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0, sy * (wall_depth * 0.5), lintel_z + 0.03))
        lmould = bpy.context.active_object
        lmould.scale = (opening_w + frame_thick * 2 + 0.16, 0.08, frame_thick + 0.06)
        bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
        lmould.data.materials.append(mat_trim)

    finalize_model("marco_puerta", "marco_puerta", max_tris=800)


# ==============================================================================
# 2. puerta_barrotes.glb (3.0 x 3.0 x 0.3 m)
# ==============================================================================
def build_puerta_barrotes():
    clean_scene()
    
    gate_w = 2.98
    gate_h = 2.98
    gate_d = 0.28
    
    mat_wood = get_material("WoodLight")
    mat_trim = get_material("WoodMedium")
    mat_brass = get_material("GoldBrass")
    
    # Bastidor exterior de la puerta
    # Largueros laterales
    stile_w = 0.18
    for sx in [-1, 1]:
        bpy.ops.mesh.primitive_cube_add(size=1.0, location=(sx * (gate_w * 0.5 - stile_w * 0.5), 0, gate_h * 0.5))
        stile = bpy.context.active_object
        stile.scale = (stile_w, gate_d, gate_h)
        bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
        stile.data.materials.append(mat_trim)

    # Travesaño inferior y superior
    rail_h = 0.20
    for z in [rail_h * 0.5, gate_h - rail_h * 0.5]:
        bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0, 0, z))
        rail = bpy.context.active_object
        rail.scale = (gate_w - stile_w * 2, gate_d, rail_h)
        bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
        rail.data.materials.append(mat_trim)
        
    # Travesaño intermedio
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0, 0, gate_h * 0.5))
    mid_rail = bpy.context.active_object
    mid_rail.scale = (gate_w - stile_w * 2, gate_d * 0.85, 0.14)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    mid_rail.data.materials.append(mat_trim)

    # Barrotes verticales de cuna (estilo barrotillos cilíndricos)
    num_bars = 10
    bar_span = gate_w - stile_w * 2 - 0.20
    step_x = bar_span / (num_bars - 1)
    
    for i in range(num_bars):
        bx = -bar_span * 0.5 + i * step_x
        # Sección inferior
        bpy.ops.mesh.primitive_cylinder_add(vertices=6, radius=0.045, depth=gate_h * 0.5 - rail_h - 0.04, location=(bx, 0, (rail_h + gate_h * 0.5) * 0.5))
        b_dn = bpy.context.active_object
        set_smooth(b_dn)
        b_dn.data.materials.append(mat_wood)
        
        # Sección superior
        bpy.ops.mesh.primitive_cylinder_add(vertices=6, radius=0.045, depth=gate_h * 0.5 - rail_h - 0.04, location=(bx, 0, (gate_h * 0.5 + gate_h - rail_h) * 0.5))
        b_up = bpy.context.active_object
        set_smooth(b_up)
        b_up.data.materials.append(mat_wood)

    # Cerrojo central de cuna en latón dorado
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0, gate_d * 0.5 + 0.02, gate_h * 0.5))
    latch = bpy.context.active_object
    latch.scale = (0.24, 0.05, 0.16)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    latch.data.materials.append(mat_brass)

    finalize_model("puerta_barrotes", "puerta_barrotes", max_tris=800)


# ==============================================================================
# 3. columna_esquina.glb (0.6 x 0.6 x 3.0 m)
# ==============================================================================
def build_columna_esquina():
    clean_scene()
    
    col_w = 0.58
    col_d = 0.58
    col_h = 3.00
    
    mat_wood = get_material("WoodLight")
    mat_base = get_material("WoodMedium")
    mat_gold = get_material("GoldBrass")
    
    # Columna esquinera diseñada para encajar en el ángulo de 90°
    # Zócalo inferior
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0, 0, 0.16))
    base = bpy.context.active_object
    base.scale = (col_w, col_d, 0.32)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    base.data.materials.append(mat_base)
    
    # Capitel superior (que toca el techo a 3.0m)
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0, 0, col_h - 0.16))
    cap = bpy.context.active_object
    cap.scale = (col_w, col_d, 0.32)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    cap.data.materials.append(mat_base)

    # Fuste central (cilíndrico acanalado o biselado al frente, recto atrás)
    bpy.ops.mesh.primitive_cylinder_add(vertices=10, radius=col_w * 0.44, depth=col_h - 0.64, location=(0, 0, col_h * 0.5))
    shaft = bpy.context.active_object
    set_smooth(shaft)
    shaft.data.materials.append(mat_wood)

    # Anillos de latón decorativos
    for z in [0.35, col_h * 0.5, col_h - 0.35]:
        bpy.ops.mesh.primitive_cylinder_add(vertices=10, radius=col_w * 0.46, depth=0.06, location=(0, 0, z))
        ring = bpy.context.active_object
        ring.data.materials.append(mat_gold)

    finalize_model("columna_esquina", "columna_esquina", max_tris=800)


# ==============================================================================
# 4. mecedora_gigante.glb (~3.0 m alto, <= 1200 tris)
# ==============================================================================
def build_mecedora_gigante():
    clean_scene()
    
    mat_wood = get_material("WoodDark")
    mat_slats = get_material("WoodMedium")
    mat_cushion = get_material("PastelRed")
    mat_gold = get_material("GoldBrass")
    
    # 1. Patines curvados de mecedora apoyados en el suelo (arcos curvados de madera)
    # Modelados como vigas de sección rectangular ligeramente curvadas
    skate_len = 2.40
    for sx in [-0.75, 0.75]:
        # Patín central curvado
        bpy.ops.mesh.primitive_cube_add(size=1.0, location=(sx, 0, 0.08))
        skate_mid = bpy.context.active_object
        skate_mid.scale = (0.10, 1.60, 0.08)
        bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
        skate_mid.data.materials.append(mat_wood)
        # Extremo frontal levantado (+Y)
        bpy.ops.mesh.primitive_cube_add(size=1.0, location=(sx, 0.95, 0.18), rotation=(math.radians(-16), 0, 0))
        skate_f = bpy.context.active_object
        skate_f.scale = (0.10, 0.45, 0.08)
        bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
        skate_f.data.materials.append(mat_wood)
        # Extremo trasero levantado (-Y)
        bpy.ops.mesh.primitive_cube_add(size=1.0, location=(sx, -0.95, 0.18), rotation=(math.radians(16), 0, 0))
        skate_b = bpy.context.active_object
        skate_b.scale = (0.10, 0.45, 0.08)
        bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
        skate_b.data.materials.append(mat_wood)

    # 4 patas torneadas de madera
    leg_h = 0.82
    for sx in [-0.65, 0.65]:
        for sy, angle in [(-0.55, 10), (0.55, -10)]:
            bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=0.075, depth=leg_h, location=(sx, sy, 0.48), rotation=(math.radians(angle), 0, 0))
            leg = bpy.context.active_object
            leg.data.materials.append(mat_wood)

    # Travesaños entre patas
    for sx in [-0.65, 0.65]:
        bpy.ops.mesh.primitive_cylinder_add(vertices=6, radius=0.045, depth=1.15, location=(sx, 0, 0.32), rotation=(math.radians(90), 0, 0))
        rung = bpy.context.active_object
        rung.data.materials.append(mat_wood)
    bpy.ops.mesh.primitive_cylinder_add(vertices=6, radius=0.045, depth=1.30, location=(0, 0, 0.38), rotation=(0, math.radians(90), 0))
    cross_rung = bpy.context.active_object
    cross_rung.data.materials.append(mat_wood)

    # Asiento amplio a ~0.90 m
    seat_z = 0.90
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0, -0.05, seat_z))
    seat = bpy.context.active_object
    seat.scale = (1.55, 1.35, 0.12)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    seat.data.materials.append(mat_wood)
    
    # Cojín de felpa rojo envejecido en el asiento
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0, -0.05, seat_z + 0.09))
    cush = bpy.context.active_object
    cush.scale = (1.40, 1.20, 0.08)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    set_smooth(cush)
    cush.data.materials.append(mat_cushion)

    # Apoyabrazos
    arm_z = seat_z + 0.48
    for sx in [-0.72, 0.72]:
        # Poste de apoyo
        bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=0.065, depth=0.48, location=(sx, 0.42, seat_z + 0.24))
        post = bpy.context.active_object
        post.data.materials.append(mat_wood)
        # Brazo horizontal
        bpy.ops.mesh.primitive_cube_add(size=1.0, location=(sx, -0.05, arm_z))
        arm = bpy.context.active_object
        arm.scale = (0.16, 1.25, 0.07)
        bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
        arm.data.materials.append(mat_wood)

    # Respaldo alto imponente que llega hasta los ~2.98 m (unos 3 m)
    back_h = 2.05
    back_y = -0.65
    back_tilt = 5  # grados hacia atrás
    # Montantes laterales del respaldo
    for sx in [-0.70, 0.70]:
        bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=0.075, depth=back_h, location=(sx, back_y - 0.08, seat_z + back_h * 0.5), rotation=(math.radians(back_tilt), 0, 0))
        b_post = bpy.context.active_object
        b_post.data.materials.append(mat_wood)
        # Remate de perilla esférica dorada
        bpy.ops.mesh.primitive_uv_sphere_add(segments=8, ring_count=5, radius=0.12, location=(sx, back_y - 0.18, seat_z + back_h + 0.08))
        finial = bpy.context.active_object
        finial.data.materials.append(mat_gold)

    # Corona / Travesaño superior del respaldo con remate
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0, back_y - 0.17, seat_z + back_h - 0.04), rotation=(math.radians(back_tilt), 0, 0))
    top_crest = bpy.context.active_object
    top_crest.scale = (1.50, 0.12, 0.24)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    top_crest.data.materials.append(mat_wood)

    # Barrotes verticales del respaldo
    for i in range(7):
        bx = -0.52 + i * (1.04 / 6)
        bpy.ops.mesh.primitive_cylinder_add(vertices=6, radius=0.038, depth=back_h - 0.30, location=(bx, back_y - 0.10, seat_z + back_h * 0.5 - 0.10), rotation=(math.radians(back_tilt), 0, 0))
        spindle = bpy.context.active_object
        spindle.data.materials.append(mat_slats)

    finalize_model("mecedora_gigante", "mecedora_gigante", max_tris=1200)


# ==============================================================================
# 5. cuna_gigante.glb (~3.5 m alto, <= 1200 tris)
# ==============================================================================
def build_cuna_gigante():
    clean_scene()
    
    mat_wood = get_material("WoodDark")
    mat_bars = get_material("WoodMedium")
    mat_gold = get_material("GoldBrass")
    mat_mattress = get_material("CreamWhite")
    
    crib_w = 2.20
    crib_l = 3.20
    crib_h = 1.80
    post_h = 3.45  # altura total monumental
    
    # 4 Postes esquineros monumentales con dosel superior
    for sx in [-1, 1]:
        for sy in [-1, 1]:
            px = sx * (crib_w * 0.5)
            py = sy * (crib_l * 0.5)
            bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=0.10, depth=post_h, location=(px, py, post_h * 0.5))
            post = bpy.context.active_object
            post.data.materials.append(mat_wood)
            # Remate esférico dorado en cada poste
            bpy.ops.mesh.primitive_uv_sphere_add(segments=8, ring_count=5, radius=0.15, location=(px, py, post_h + 0.08))
            orb = bpy.context.active_object
            orb.data.materials.append(mat_gold)

    # Estructura del dosel superior (vigas que unen los postes arriba)
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0, 0, post_h - 0.10))
    canopy_rim = bpy.context.active_object
    canopy_rim.scale = (crib_w + 0.20, crib_l + 0.20, 0.14)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    canopy_rim.data.materials.append(mat_wood)

    # Base y colchón
    base_z = 0.55
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0, 0, base_z))
    base = bpy.context.active_object
    base.scale = (crib_w, crib_l, 0.16)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    base.data.materials.append(mat_wood)

    # Colchón enorme
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0, 0, base_z + 0.22))
    matt = bpy.context.active_object
    matt.scale = (crib_w - 0.15, crib_l - 0.15, 0.30)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    set_smooth(matt)
    matt.data.materials.append(mat_mattress)

    # Pasamanos superior de los barrotes (a 1.80m de altura)
    rail_z = base_z + crib_h
    # Laterales largos (+X y -X)
    for sx in [-1, 1]:
        bpy.ops.mesh.primitive_cube_add(size=1.0, location=(sx * (crib_w * 0.5), 0, rail_z))
        rail = bpy.context.active_object
        rail.scale = (0.12, crib_l, 0.12)
        bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
        rail.data.materials.append(mat_wood)
        
        # Barrotes laterales
        num_side_bars = 10
        step_y = (crib_l - 0.40) / (num_side_bars - 1)
        for i in range(num_side_bars):
            by = -(crib_l - 0.40) * 0.5 + i * step_y
            bpy.ops.mesh.primitive_cylinder_add(vertices=5, radius=0.035, depth=crib_h - 0.15, location=(sx * (crib_w * 0.5), by, (base_z + rail_z) * 0.5))
            bar = bpy.context.active_object
            bar.data.materials.append(mat_bars)

    # Cabecero y piecero (+Y y -Y)
    for sy in [-1, 1]:
        bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0, sy * (crib_l * 0.5), rail_z + 0.15))
        end_rail = bpy.context.active_object
        end_rail.scale = (crib_w, 0.12, 0.18)
        bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
        end_rail.data.materials.append(mat_wood)
        
        num_end_bars = 6
        step_x = (crib_w - 0.35) / (num_end_bars - 1)
        for i in range(num_end_bars):
            bx = -(crib_w - 0.35) * 0.5 + i * step_x
            bpy.ops.mesh.primitive_cylinder_add(vertices=5, radius=0.038, depth=crib_h, location=(bx, sy * (crib_l * 0.5), (base_z + rail_z) * 0.5 + 0.08))
            bar = bpy.context.active_object
            bar.data.materials.append(mat_bars)

    finalize_model("cuna_gigante", "cuna_gigante", max_tris=1200)


# ==============================================================================
# 6. pedestal_tesoro.glb (~0.6 m alto, 1.0 m diámetro, <= 800 tris)
# ==============================================================================
def build_pedestal_tesoro():
    clean_scene()
    
    mat_stone = get_material("WoodDark")
    mat_gold = get_material("GoldBrass")
    mat_cushion = get_material("PastelRed")
    
    # Base octogonal escalonada
    # Nivel 1: Plinto inferior
    bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=0.52, depth=0.12, location=(0, 0, 0.06), rotation=(0, 0, math.radians(22.5)))
    p1 = bpy.context.active_object
    p1.data.materials.append(mat_stone)
    
    # Nivel 2: Toro moldurado
    bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=0.46, depth=0.08, location=(0, 0, 0.16), rotation=(0, 0, math.radians(22.5)))
    p2 = bpy.context.active_object
    p2.data.materials.append(mat_gold)

    # Nivel 3: Fuste central acanalado
    bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=0.38, depth=0.28, location=(0, 0, 0.34), rotation=(0, 0, math.radians(22.5)))
    p3 = bpy.context.active_object
    set_smooth(p3)
    p3.data.materials.append(mat_stone)

    # Nivel 4: Capitel superior moldurado de latón dorado
    bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=0.48, depth=0.08, location=(0, 0, 0.52), rotation=(0, 0, math.radians(22.5)))
    p4 = bpy.context.active_object
    p4.data.materials.append(mat_gold)

    # Nivel 5: Almohadilla / plato de terciopelo rojo donde flota el objeto coleccionable
    bpy.ops.mesh.primitive_cylinder_add(vertices=12, radius=0.42, depth=0.05, location=(0, 0, 0.585))
    cush = bpy.context.active_object
    set_smooth(cush)
    cush.data.materials.append(mat_cushion)
    
    # Ribete dorado alrededor del plato
    bpy.ops.mesh.primitive_cylinder_add(vertices=12, radius=0.44, depth=0.025, location=(0, 0, 0.58))
    ribbon = bpy.context.active_object
    ribbon.data.materials.append(mat_gold)

    finalize_model("pedestal_tesoro", "pedestal_tesoro", max_tris=800)


if __name__ == "__main__":
    print("\n================== GENERANDO PIEZAS ESTRUCTURALES Y SALA JEFE/TESORO ==================")
    build_marco_puerta()
    build_puerta_barrotes()
    build_columna_esquina()
    build_mecedora_gigante()
    build_cuna_gigante()
    build_pedestal_tesoro()
    print("\n================== PIEZAS COMPLETADAS CON ÉXITO ==================")
