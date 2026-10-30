"""Generador oficial de los 5 objetos (Items) de LULLABY para David:
  1. biberon.glb          (Azul)
  2. fotografia_rota.glb  (Naranja)
  3. chupete.glb          (Rosa)
  4. oso_peluche.glb      (Marrón)
  5. manta_vieja.glb      (Verde)

Reglas del Plan de Trabajo de Alejandro:
  - Carpeta destino: assets/models/items/
  - Tamaño: ~0.5 m de escala
  - Presupuesto: hasta 400 triángulos por objeto
  - Un solo material plano sin texturas
  - Origen en la base (z=0 en el suelo)
  - Frente hacia +Y en Blender (queda como -Z en Godot)
  - Sin residuos (.blend1)
"""

import math
import os
import shutil
from pathlib import Path

import bpy
from mathutils import Matrix, Vector

REPO_ROOT = Path(r"C:\LULLABY")
MODELS_ITEMS_DIR = REPO_ROOT / "assets" / "models" / "items"
SOURCE_ITEMS_DIR = REPO_ROOT / "assets" / "source" / "blender" / "items"
DRIVE_ITEMS_DIR = Path(r"C:\Users\davin\OneDrive\Documentos\MODELOS JUEGO\items")

MODELS_ITEMS_DIR.mkdir(parents=True, exist_ok=True)
SOURCE_ITEMS_DIR.mkdir(parents=True, exist_ok=True)
DRIVE_ITEMS_DIR.mkdir(parents=True, exist_ok=True)

bpy.context.preferences.filepaths.save_version = 0  # Desactivar .blend1


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


def make_single_material(name: str, hex_color: str, roughness: float = 0.55):
    srgb = [int(hex_color[i:i + 2], 16) / 255.0 for i in (1, 3, 5)]
    linear = tuple(srgb_to_linear(c) for c in srgb)
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
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
                inp.default_value = roughness
            elif inp.name == "Metallic":
                inp.default_value = 0.0
            elif inp.name in ("Specular IOR Level", "Specular"):
                inp.default_value = 0.25
    mat.diffuse_color = (*linear, 1.0)
    return mat


def set_origin_to_base(obj):
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    # Hallar el punto mínimo en Z de la malla
    min_z = min((obj.matrix_world @ Vector(v.co)).z for v in obj.data.vertices)
    current_cursor = bpy.context.scene.cursor.location.copy()
    bpy.context.scene.cursor.location = (0.0, 0.0, min_z)
    bpy.ops.object.origin_set(type='ORIGIN_CURSOR', center='MEDIAN')
    obj.location = (0.0, 0.0, 0.0)
    bpy.context.scene.cursor.location = current_cursor


def get_triangle_count(obj) -> int:
    obj.data.calc_loop_triangles()
    return len(obj.data.loop_triangles)


def render_preview(obj, output_png: Path, item_color_hex: str):
    scene = bpy.context.scene

    # Crear suelo circular oscuro
    bpy.ops.mesh.primitive_cylinder_add(vertices=16, radius=0.6, depth=0.02, location=(0, 0, -0.01))
    pedestal = bpy.context.object
    mat_ped = bpy.data.materials.new("Pedestal")
    mat_ped.diffuse_color = (0.15, 0.13, 0.18, 1.0)
    pedestal.data.materials.append(mat_ped)

    # Luz
    light_data = bpy.data.lights.new("ItemSun", type='SUN')
    light_data.energy = 3.5
    light_obj = bpy.data.objects.new("ItemSun", light_data)
    light_obj.rotation_euler = (math.radians(55), 0, math.radians(-35))
    scene.collection.objects.link(light_obj)

    light_fill = bpy.data.lights.new("ItemFill", type='SUN')
    light_fill.energy = 1.2
    light_fill_obj = bpy.data.objects.new("ItemFill", light_fill)
    light_fill_obj.rotation_euler = (math.radians(60), 0, math.radians(145))
    scene.collection.objects.link(light_fill_obj)

    # Cámara a 60° (cámara de juego / vista 3/4)
    cam_data = bpy.data.cameras.new("ItemCam")
    cam_data.lens = 45.0
    cam_obj = bpy.data.objects.new("ItemCam", cam_data)
    dist = 1.35
    pitch_rad = math.radians(55)
    cam_obj.location = (0.0, -dist * math.cos(pitch_rad), dist * math.sin(pitch_rad) + 0.25)
    target = Vector((0.0, 0.0, 0.22))
    direction = target - Vector(cam_obj.location)
    cam_obj.rotation_euler = direction.to_track_quat('-Z', 'Y').to_euler()
    scene.collection.objects.link(cam_obj)
    scene.camera = cam_obj

    # Render
    scene.render.engine = 'BLENDER_EEVEE'
    scene.render.resolution_x = 800
    scene.render.resolution_y = 800
    scene.render.filepath = str(output_png)
    scene.render.image_settings.file_format = 'PNG'
    bpy.ops.render.render(write_still=True)


# =============================================================================
# 1. BIBERÓN (Azul) - Recupera 2 vidas
# =============================================================================
def build_biberon():
    clean_scene()
    mat = make_single_material("Mat_Biberon", "#4A90E2")  # Azul

    # Cuerpo cilíndrico del biberón (10 lados para low-poly estilizado)
    bpy.ops.mesh.primitive_cylinder_add(vertices=12, radius=0.11, depth=0.28, location=(0, 0, 0.14))
    body = bpy.context.object
    body.name = "Biberon"

    # Rosca / Anillo del biberón
    bpy.ops.mesh.primitive_cylinder_add(vertices=12, radius=0.125, depth=0.06, location=(0, 0, 0.31))
    ring = bpy.context.object

    # Tetina cónica redondeada
    bpy.ops.mesh.primitive_cone_add(vertices=10, radius1=0.08, radius2=0.035, depth=0.12, location=(0, 0, 0.38))
    nipple = bpy.context.object

    # Punta esférica de la tetina
    bpy.ops.mesh.primitive_uv_sphere_add(segments=8, ring_count=6, radius=0.035, location=(0, 0, 0.44))
    tip = bpy.context.object

    # Unir todo
    bpy.context.view_layer.objects.active = body
    for p in [ring, nipple, tip]:
        p.select_set(True)
    body.select_set(True)
    bpy.ops.object.join()

    biberon = bpy.context.object
    biberon.data.materials.append(mat)
    set_origin_to_base(biberon)
    bpy.ops.object.shade_smooth()
    return biberon


# =============================================================================
# 2. FOTOGRAFÍA ROTA (Naranja) - +25% daño
# Estilo portarretratos de madera con soporte trasero y fotografía rasgada
# por la mitad con corte vertical irregular (según la referencia).
# =============================================================================
def build_fotografia_rota():
    clean_scene()
    mat = make_single_material("Mat_FotografiaRota", "#E67E22", roughness=0.70)  # Naranja cálido

    # Marco de madera (dimensiones: ~0.38m de ancho x ~0.38m de alto)
    # Barra superior
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0, 0, 0.36))
    frame_top = bpy.context.object
    frame_top.name = "FotografiaRota"
    frame_top.scale = (0.38, 0.035, 0.04)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)

    # Barra inferior
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0, 0, 0.04))
    frame_bot = bpy.context.object
    frame_bot.scale = (0.38, 0.035, 0.04)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)

    # Barra izquierda
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(-0.17, 0, 0.20))
    frame_l = bpy.context.object
    frame_l.scale = (0.04, 0.035, 0.32)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)

    # Barra derecha
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0.17, 0, 0.20))
    frame_r = bpy.context.object
    frame_r.scale = (0.04, 0.035, 0.32)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)

    # Tablero trasero del marco
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0, -0.015, 0.20))
    backboard = bpy.context.object
    backboard.scale = (0.32, 0.01, 0.30)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)

    # Fotografía rasgada mitad izquierda (con borde quebrado central)
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(-0.075, 0.005, 0.20))
    photo_l = bpy.context.object
    photo_l.scale = (0.13, 0.008, 0.28)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)

    # Fotografía rasgada mitad derecha (separada con brecha irregular)
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0.075, 0.008, 0.195))
    photo_r = bpy.context.object
    photo_r.scale = (0.13, 0.008, 0.27)
    photo_r.rotation_euler = (0, 0, math.radians(-2))
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)

    # Hendidura / rasgón visible en el centro (prisma triangular que marca el desgarro irregular)
    bpy.ops.mesh.primitive_cone_add(vertices=3, radius1=0.03, radius2=0.005, depth=0.28, location=(0.005, 0.01, 0.20))
    tear_gap = bpy.context.object
    tear_gap.rotation_euler = (0, 0, math.radians(5))
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)

    # Esquina faltante superior izquierda arrancada
    bpy.ops.mesh.primitive_cone_add(vertices=3, radius1=0.04, radius2=0.01, depth=0.08, location=(-0.11, 0.01, 0.31))
    corner_tear = bpy.context.object
    corner_tear.rotation_euler = (0, math.radians(45), 0)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)

    # Soporte / pata trasera triangular inclinada que sostiene el portarretratos
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0, -0.09, 0.12))
    easel_leg = bpy.context.object
    easel_leg.scale = (0.045, 0.16, 0.22)
    easel_leg.rotation_euler = (math.radians(32), 0, 0)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)

    # Unir todas las piezas en un solo objeto
    bpy.context.view_layer.objects.active = frame_top
    for p in [frame_bot, frame_l, frame_r, backboard, photo_l, photo_r, tear_gap, corner_tear, easel_leg]:
        p.select_set(True)
    frame_top.select_set(True)
    bpy.ops.object.join()

    # Inclinar ligeramente todo el portarretratos hacia atrás (como en la foto real apoyada en la mesa)
    frame_top.rotation_euler = (math.radians(-10), 0, 0)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)

    foto = bpy.context.object
    foto.data.materials.append(mat)
    set_origin_to_base(foto)
    bpy.ops.object.shade_smooth()
    return foto


# =============================================================================
# 3. CHUPETE (Rosa) - +20% cadencia
# Estilo chupete clásico infantil (referencia: platillo curvo, tetina bulbosa,
# collarín y anilla colgante).
# =============================================================================
def build_chupete():
    clean_scene()
    mat = make_single_material("Mat_Chupete", "#FF5277", roughness=0.45)  # Rosa vibrante estilizado

    # 1. Platillo curvo / escudo del chupete (disco con borde grueso tipo platillo)
    bpy.ops.mesh.primitive_cylinder_add(vertices=10, radius=0.19, depth=0.03, location=(0, 0, 0.18))
    shield = bpy.context.object
    shield.name = "Chupete"
    shield.scale = (1.12, 0.96, 1.0)
    shield.rotation_euler = (math.radians(-30), 0, 0)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)

    # Reborde redondeado del platillo
    bpy.ops.mesh.primitive_torus_add(major_segments=10, minor_segments=4, major_radius=0.18, minor_radius=0.02, location=(0, 0, 0.18))
    rim = bpy.context.object
    rim.scale = (1.12, 0.96, 1.0)
    rim.rotation_euler = (math.radians(-30), 0, 0)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)

    # 2. Collarín / botón posterior donde encaja la anilla
    bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=0.08, depth=0.05, location=(0, -0.04, 0.11))
    collar = bpy.context.object
    collar.rotation_euler = (math.radians(-30), 0, 0)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)

    # 3. Anilla colgante del chupete (colgando hacia abajo como en la referencia)
    bpy.ops.mesh.primitive_torus_add(major_segments=10, minor_segments=4, major_radius=0.085, minor_radius=0.018, location=(0, -0.09, 0.07))
    ring = bpy.context.object
    ring.rotation_euler = (math.radians(45), 0, 0)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)

    # 4. Tetina bulbosa clásica (tallo cónico que se ensancha en bulbo esférico redondeado)
    bpy.ops.mesh.primitive_cone_add(vertices=8, radius1=0.045, radius2=0.06, depth=0.13, location=(0, 0.08, 0.28))
    stem = bpy.context.object
    stem.rotation_euler = (math.radians(-30), 0, 0)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)

    # Cabeza bulbosa redondeada (la pera clásica de goma)
    bpy.ops.mesh.primitive_uv_sphere_add(segments=8, ring_count=5, radius=0.085, location=(0, 0.13, 0.36))
    bulb = bpy.context.object
    bulb.scale = (1.0, 1.0, 1.15)
    bulb.rotation_euler = (math.radians(-30), 0, 0)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)

    # Unir todas las piezas en un solo objeto compacto
    bpy.context.view_layer.objects.active = shield
    for p in [rim, collar, ring, stem, bulb]:
        p.select_set(True)
    shield.select_set(True)
    bpy.ops.object.join()

    chupete = bpy.context.object
    chupete.data.materials.append(mat)
    set_origin_to_base(chupete)
    bpy.ops.object.shade_smooth()
    return chupete


# =============================================================================
# 4. OSO DE PELUCHE (Marrón) - +1 vida máxima
# Estilo osito de peluche clásico sentado (referencia: cuerpo regordete aperado,
# cabeza grande, hocico saliente, orejas redondas, patitas redondas al frente).
# =============================================================================
def build_oso_peluche():
    clean_scene()
    mat = make_single_material("Mat_OsoPeluche", "#6E473B", roughness=0.65)  # Marrón osito cálido

    # 1. Cuerpo aperado y regordete sentado
    bpy.ops.mesh.primitive_uv_sphere_add(segments=8, ring_count=5, radius=0.15, location=(0, 0, 0.16))
    body = bpy.context.object
    body.name = "OsoPeluche"
    body.scale = (1.05, 0.95, 1.15)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)

    # Barriguita acolchada sobresaliente al frente (+Y)
    bpy.ops.mesh.primitive_uv_sphere_add(segments=5, ring_count=3, radius=0.09, location=(0, 0.08, 0.16))
    belly = bpy.context.object
    belly.scale = (0.9, 0.45, 1.0)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)

    # 2. Cabeza redonda y tierna
    bpy.ops.mesh.primitive_uv_sphere_add(segments=8, ring_count=5, radius=0.13, location=(0, 0.02, 0.35))
    head = bpy.context.object
    head.scale = (1.1, 0.98, 1.0)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)

    # Hocico ovalado saliente hacia el frente (+Y)
    bpy.ops.mesh.primitive_uv_sphere_add(segments=6, ring_count=4, radius=0.055, location=(0, 0.12, 0.33))
    snout = bpy.context.object
    snout.scale = (1.15, 0.9, 0.85)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)

    # Nariz triangular en la punta del hocico
    bpy.ops.mesh.primitive_cone_add(vertices=3, radius1=0.020, radius2=0.005, depth=0.02, location=(0, 0.17, 0.345))
    nose = bpy.context.object
    nose.rotation_euler = (math.radians(-90), 0, 0)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)

    # Ojos redondos de botón
    bpy.ops.mesh.primitive_uv_sphere_add(segments=4, ring_count=3, radius=0.015, location=(-0.045, 0.13, 0.37))
    eye_l = bpy.context.object
    bpy.ops.mesh.primitive_uv_sphere_add(segments=4, ring_count=3, radius=0.015, location=(0.045, 0.13, 0.37))
    eye_r = bpy.context.object

    # Orejas redondeadas características de osito
    bpy.ops.mesh.primitive_uv_sphere_add(segments=5, ring_count=3, radius=0.048, location=(-0.11, 0.02, 0.45))
    ear_l = bpy.context.object
    ear_l.scale = (1.0, 0.45, 1.0)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)

    bpy.ops.mesh.primitive_uv_sphere_add(segments=5, ring_count=3, radius=0.048, location=(0.11, 0.02, 0.45))
    ear_r = bpy.context.object
    ear_r.scale = (1.0, 0.45, 1.0)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)

    # 3. Brazos / patas delanteras curvadas hacia los costados del torso
    bpy.ops.mesh.primitive_cylinder_add(vertices=5, radius=0.04, depth=0.14, location=(-0.14, 0.03, 0.21))
    arm_l = bpy.context.object
    arm_l.rotation_euler = (math.radians(-25), math.radians(-20), 0)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)

    bpy.ops.mesh.primitive_cylinder_add(vertices=5, radius=0.04, depth=0.14, location=(0.14, 0.03, 0.21))
    arm_r = bpy.context.object
    arm_r.rotation_euler = (math.radians(-25), math.radians(20), 0)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)

    # 4. Patas traseras / almohadillas redondas mirando hacia adelante (+Y)
    bpy.ops.mesh.primitive_cylinder_add(vertices=6, radius=0.065, depth=0.05, location=(-0.11, 0.11, 0.07))
    foot_l = bpy.context.object
    foot_l.rotation_euler = (math.radians(90), 0, 0)
    foot_l.scale = (0.95, 1.15, 1.0)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)

    bpy.ops.mesh.primitive_cylinder_add(vertices=6, radius=0.065, depth=0.05, location=(0.11, 0.11, 0.07))
    foot_r = bpy.context.object
    foot_r.rotation_euler = (math.radians(90), 0, 0)
    foot_r.scale = (0.95, 1.15, 1.0)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)

    # Unir todo en una sola pieza
    bpy.context.view_layer.objects.active = body
    for p in [belly, head, snout, nose, eye_l, eye_r, ear_l, ear_r, arm_l, arm_r, foot_l, foot_r]:
        p.select_set(True)
    body.select_set(True)
    bpy.ops.object.join()

    oso = bpy.context.object
    oso.data.materials.append(mat)
    set_origin_to_base(oso)
    bpy.ops.object.shade_smooth()
    return oso


# =============================================================================
# 5. MANTA VIEJA (Verde) - -20% daño recibido
# Estilo manta clásica enrollada (referencia: rollo cilíndrico de tela con
# núcleo en espiral y extremo rectangular desenrollado con flecos).
# =============================================================================
def build_manta_vieja():
    clean_scene()
    # Verde salvia / oliva suave estilizado (como la referencia)
    mat = make_single_material("Mat_MantaVieja", "#5C946E", roughness=0.75)

    # 1. Rollo principal exterior de la manta (cilindro horizontal)
    bpy.ops.mesh.primitive_cylinder_add(vertices=12, radius=0.13, depth=0.38, location=(-0.10, 0, 0.13))
    roll = bpy.context.object
    roll.name = "MantaVieja"
    roll.rotation_euler = (math.radians(90), 0, 0)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)

    # 2. Núcleo interior / hendidura de espiral (para dar el efecto visual de manta enrollada en los extremos)
    bpy.ops.mesh.primitive_cylinder_add(vertices=10, radius=0.075, depth=0.384, location=(-0.08, 0, 0.12))
    inner_core = bpy.context.object
    inner_core.rotation_euler = (math.radians(90), 0, 0)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)

    # 3. Manto / extremo desplegado que sale de la base del rollo hacia la derecha
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0.10, 0, 0.02))
    tail = bpy.context.object
    tail.scale = (0.24, 0.38, 0.03)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)

    # 4. Borde con flecos / remate al final del extremo desplegado (estilo manta artesanal)
    for i in range(5):
        fy = -0.15 + i * 0.075
        bpy.ops.mesh.primitive_cylinder_add(vertices=4, radius=0.008, depth=0.05, location=(0.24, fy, 0.015))
        fringe = bpy.context.object
        fringe.rotation_euler = (0, math.radians(75), 0)
        bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
        bpy.context.view_layer.objects.active = tail
        fringe.select_set(True)
        tail.select_set(True)
        bpy.ops.object.join()

    # Unir todo
    bpy.context.view_layer.objects.active = roll
    for p in [inner_core, tail]:
        p.select_set(True)
    roll.select_set(True)
    bpy.ops.object.join()

    manta = bpy.context.object
    manta.data.materials.append(mat)
    set_origin_to_base(manta)
    bpy.ops.object.shade_smooth()
    return manta


# =============================================================================
# Proceso principal: generar los 5 objetos, validar tris, exportar y guardar
# =============================================================================
ITEMS = [
    ("biberon", build_biberon, "#4A90E2"),
    ("fotografia_rota", build_fotografia_rota, "#E67E22"),
    ("chupete", build_chupete, "#E91E63"),
    ("oso_peluche", build_oso_peluche, "#8D6E63"),
    ("manta_vieja", build_manta_vieja, "#48C9B0"),
]


def process_all_items():
    print("=== GENERANDO LOS 5 OBJETOS DE LULLABY ===")
    results = []

    for name, builder, color_hex in ITEMS:
        print(f"\n--- Construyendo {name} ---")
        item_obj = builder()
        tris = get_triangle_count(item_obj)
        dims = item_obj.dimensions
        print(f"[{name}] Triángulos: {tris} (Límite: 400)")
        print(f"[{name}] Dimensiones: {dims.x:.3f}m x {dims.y:.3f}m x {dims.z:.3f}m")
        assert tris <= 400, f"¡ERROR! {name} excede el presupuesto de 400 triángulos: {tris}"

        # 1. Exportar GLB
        glb_path = MODELS_ITEMS_DIR / f"{name}.glb"
        bpy.ops.object.select_all(action='DESELECT')
        item_obj.select_set(True)
        bpy.context.view_layer.objects.active = item_obj

        bpy.ops.export_scene.gltf(
            filepath=str(glb_path),
            export_format='GLB',
            use_selection=True,
            export_yup=True,
            export_apply=False,
        )

        # 2. Renderizar preview PNG
        preview_path = SOURCE_ITEMS_DIR / f"{name}_preview.png"
        render_preview(item_obj, preview_path, color_hex)

        # 3. Guardar escena .blend
        blend_path = SOURCE_ITEMS_DIR / f"{name}.blend"
        bpy.ops.wm.save_as_mainfile(filepath=str(blend_path), compress=True)

        # 4. Copiar a la carpeta de David en OneDrive
        drive_glb = DRIVE_ITEMS_DIR / f"{name}.glb"
        drive_blend = DRIVE_ITEMS_DIR / f"{name}.blend"
        drive_preview = DRIVE_ITEMS_DIR / f"{name}_preview.png"
        shutil.copy2(glb_path, drive_glb)
        shutil.copy2(blend_path, drive_blend)
        shutil.copy2(preview_path, drive_preview)

        results.append({
            "name": name,
            "tris": tris,
            "dims": f"{dims.x:.2f}x{dims.y:.2f}x{dims.z:.2f}m",
            "glb_size": glb_path.stat().st_size,
        })

    # Copiar también el script a assets/source/blender/items/
    this_script = Path(__file__).resolve()
    shutil.copy2(this_script, SOURCE_ITEMS_DIR / "generate_items.py")
    shutil.copy2(this_script, DRIVE_ITEMS_DIR / "generate_items.py")

    print("\n=== RESUMEN DE ENTREGA DE OBJETOS ===")
    for r in results:
        print(f"  • {r['name']}.glb: {r['tris']} tris, {r['dims']}, tamaño GLB={r['glb_size']} B")
    print(f"\nGuardados en:\n  - Repo: {MODELS_ITEMS_DIR}\n  - Sources: {SOURCE_ITEMS_DIR}\n  - OneDrive: {DRIVE_ITEMS_DIR}")


if __name__ == "__main__":
    process_all_items()
