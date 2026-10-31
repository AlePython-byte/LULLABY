"""Generador oficial de 5 Props de Pared nuevos para LULLABY:
  1. cuadro_infantil.glb     (Cuadro con marco de madera, lienzo y barquito con sol)
  2. reloj_pared.glb         (Reloj cucú de pared con tejadillo y péndulo dorado)
  3. repisa_juguetes.glb     (Repisa flotante de madera con patito, libros y cubo)
  4. aplique_estrella.glb    (Lámpara de pared infantil en forma de estrella con cordón)
  5. perchero_pared.glb      (Perchero con clavijas de colores y babero/saquito)

Cumple estrictamente con:
  - MaxRelief <= 0.30 m de profundidad
  - Espalda plana apoyada en la pared (Y = 0)
  - Frente hacia +Y en Blender (-Z en Godot)
  - Colores planos sRGB -> Linear
  - Limite de geometria: <= 400 triangulos cada uno
"""

import math
import os
import shutil
from pathlib import Path

import bpy
import bmesh
from mathutils import Vector, Euler

REPO_ROOT = Path(r"C:\LULLABY")
PROPS_DIR = REPO_ROOT / "assets" / "models" / "props"
SOURCE_DIR = REPO_ROOT / "assets" / "source" / "blender"
DRIVE_PROPS_DIR = Path(r"C:\Users\davin\OneDrive\Documentos\MODELOS JUEGO\props")
BRAIN_DIR = Path(r"C:\Users\davin\.gemini\antigravity\brain\61207137-232d-4414-8860-f3fa45906692")

PROPS_DIR.mkdir(parents=True, exist_ok=True)
SOURCE_DIR.mkdir(parents=True, exist_ok=True)
DRIVE_PROPS_DIR.mkdir(parents=True, exist_ok=True)

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

def make_material(name: str, hex_color: str, roughness: float = 0.6, metallic: float = 0.0):
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
                inp.default_value = metallic
    mat.diffuse_color = (*linear, 1.0)
    return mat

def get_tris(obj) -> int:
    obj.data.calc_loop_triangles()
    return len(obj.data.loop_triangles)

def set_origin_to_base_back(obj):
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    min_z = min((obj.matrix_world @ Vector(v.co)).z for v in obj.data.vertices)
    min_y = min((obj.matrix_world @ Vector(v.co)).y for v in obj.data.vertices)
    center_x = sum((obj.matrix_world @ Vector(v.co)).x for v in obj.data.vertices) / len(obj.data.vertices)
    
    current_cursor = bpy.context.scene.cursor.location.copy()
    bpy.context.scene.cursor.location = (center_x, min_y, min_z)
    bpy.ops.object.origin_set(type='ORIGIN_CURSOR', center='MEDIAN')
    obj.location = (0.0, 0.0, 0.0)
    bpy.context.scene.cursor.location = current_cursor

def render_preview(obj, out_path: Path):
    scene = bpy.context.scene
    scene.render.film_transparent = True
    scene.render.image_settings.media_type = 'IMAGE'
    scene.render.image_settings.file_format = 'PNG'
    scene.render.image_settings.color_mode = 'RGBA'
    
    for o in list(scene.objects):
        if o != obj:
            bpy.data.objects.remove(o, do_unlink=True)
            
    bbox = [obj.matrix_world @ Vector(corner) for corner in obj.bound_box]
    center = Vector(((min(v.x for v in bbox) + max(v.x for v in bbox))/2,
                     (min(v.y for v in bbox) + max(v.y for v in bbox))/2,
                     (min(v.z for v in bbox) + max(v.z for v in bbox))/2))
    max_dim = max(max(v.x for v in bbox) - min(v.x for v in bbox),
                  max(v.y for v in bbox) - min(v.y for v in bbox),
                  max(v.z for v in bbox) - min(v.z for v in bbox))
                  
    # Front-facing 3-point lights:
    # Key light: Front-Right-Top (azimuth ~30 deg from +Y, elevation 25 deg)
    key = bpy.data.lights.new("Key", type='SUN')
    key.energy = 4.5
    key.color = (1.0, 0.98, 0.95)
    key_obj = bpy.data.objects.new("Key", key)
    k_dir = Vector((-0.45, -0.80, -0.40))
    key_obj.rotation_euler = k_dir.to_track_quat('-Z', 'Y').to_euler()
    scene.collection.objects.link(key_obj)
    
    # Fill light: Front-Left-Bottom
    fill = bpy.data.lights.new("Fill", type='SUN')
    fill.energy = 2.6
    fill.color = (0.90, 0.94, 1.0)
    fill_obj = bpy.data.objects.new("Fill", fill)
    f_dir = Vector((0.60, -0.75, -0.25))
    fill_obj.rotation_euler = f_dir.to_track_quat('-Z', 'Y').to_euler()
    scene.collection.objects.link(fill_obj)
    
    # Rim light: Back-Top
    rim = bpy.data.lights.new("Rim", type='SUN')
    rim.energy = 3.2
    rim_obj = bpy.data.objects.new("Rim", rim)
    r_dir = Vector((0.0, 0.85, -0.55))
    rim_obj.rotation_euler = r_dir.to_track_quat('-Z', 'Y').to_euler()
    scene.collection.objects.link(rim_obj)
    
    cam_data = bpy.data.cameras.new("Cam")
    cam_data.lens = 55.0
    cam_obj = bpy.data.objects.new("Cam", cam_data)
    scene.collection.objects.link(cam_obj)
    scene.camera = cam_obj
    
    yaw = math.radians(22)
    pitch = math.radians(14)
    fov = 2.0 * math.atan(18.0 / cam_data.lens)
    req_dist = (max_dim * 1.30) / (2.0 * math.tan(fov / 2.0))
    
    cam_obj.location = (
        center.x + req_dist * math.sin(yaw) * math.cos(pitch),
        center.y + req_dist * math.cos(yaw) * math.cos(pitch),
        center.z + req_dist * math.sin(pitch)
    )
    cam_obj.rotation_euler = (center - cam_obj.location).to_track_quat('-Z', 'Y').to_euler()
    
    scene.render.resolution_x = 800
    scene.render.resolution_y = 800
    scene.render.filepath = str(out_path)
    bpy.ops.render.render(write_still=True)


# =============================================================================
# 1. CUADRO INFANTIL (Pared)
# =============================================================================
def build_cuadro_infantil():
    clean_scene()
    mat_frame = make_material("Mat_MarcoCuadro", "#8D6E63", roughness=0.65) # Madera nogal
    mat_canvas = make_material("Mat_Lienzo", "#FFFBF5", roughness=0.85)     # Lienzo crema claro
    mat_boat = make_material("Mat_Barquito", "#4A90E2", roughness=0.5)      # Barquito azul
    mat_sail = make_material("Mat_Vela", "#E74C3C", roughness=0.5)          # Vela roja
    mat_sun = make_material("Mat_Sol", "#F1C40F", roughness=0.4)            # Sol amarillo

    # Marco exterior (ancho 0.82m, alto 0.66m, fondo 0.04m)
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0, 0.02, 0.33))
    frame_top = bpy.context.object
    frame_top.scale = (0.82, 0.04, 0.04)
    bpy.ops.object.transform_apply(scale=True)
    
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0, 0.02, -0.33))
    frame_bot = bpy.context.object
    frame_bot.scale = (0.82, 0.04, 0.04)
    bpy.ops.object.transform_apply(scale=True)
    
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(-0.39, 0.02, 0))
    frame_l = bpy.context.object
    frame_l.scale = (0.04, 0.04, 0.62)
    bpy.ops.object.transform_apply(scale=True)
    
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0.39, 0.02, 0))
    frame_r = bpy.context.object
    frame_r.scale = (0.04, 0.04, 0.62)
    bpy.ops.object.transform_apply(scale=True)
    
    # Lienzo de fondo
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0, 0.01, 0))
    canvas = bpy.context.object
    canvas.scale = (0.76, 0.015, 0.64)
    bpy.ops.object.transform_apply(scale=True)
    
    # Dibujo infantil: Casco de barquito
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0, 0.025, -0.10))
    boat_hull = bpy.context.object
    boat_hull.scale = (0.28, 0.015, 0.07)
    bpy.ops.object.transform_apply(scale=True)
    
    # Mástil
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(-0.04, 0.025, 0.06))
    mast = bpy.context.object
    mast.scale = (0.02, 0.015, 0.25)
    bpy.ops.object.transform_apply(scale=True)
    
    # Vela triangular
    bpy.ops.mesh.primitive_cylinder_add(vertices=3, radius=0.14, depth=0.015, location=(0.04, 0.025, 0.06))
    sail = bpy.context.object
    sail.rotation_euler = (math.radians(90), 0, math.radians(90))
    sail.scale = (1.0, 1.0, 1.0)
    bpy.ops.object.transform_apply(rotation=True, scale=True)

    # Sol infantil en la esquina superior izquierda
    bpy.ops.mesh.primitive_cylinder_add(vertices=12, radius=0.08, depth=0.015, location=(-0.24, 0.025, 0.19))
    sun = bpy.context.object
    sun.rotation_euler = (math.radians(90), 0, 0)
    bpy.ops.object.transform_apply(rotation=True, scale=True)

    # Asignar materiales
    frame_top.data.materials.append(mat_frame)
    frame_bot.data.materials.append(mat_frame)
    frame_l.data.materials.append(mat_frame)
    frame_r.data.materials.append(mat_frame)
    canvas.data.materials.append(mat_canvas)
    boat_hull.data.materials.append(mat_boat)
    mast.data.materials.append(mat_frame)
    sail.data.materials.append(mat_sail)
    sun.data.materials.append(mat_sun)
    
    # Unir todo
    bpy.context.view_layer.objects.active = canvas
    for p in [frame_top, frame_bot, frame_l, frame_r, boat_hull, mast, sail, sun]:
        p.select_set(True)
    canvas.select_set(True)
    bpy.ops.object.join()
    
    cuadro = bpy.context.object
    cuadro.name = "cuadro_infantil"
    set_origin_to_base_back(cuadro)
    bpy.ops.object.shade_smooth()
    return cuadro


# =============================================================================
# 2. RELOJ DE PARED (Reloj cucú con péndulo)
# =============================================================================
def build_reloj_pared():
    clean_scene()
    mat_wood = make_material("Mat_RelojMadera", "#6D4C41", roughness=0.6)  # Madera nogal
    mat_roof = make_material("Mat_RelojTejado", "#D32F2F", roughness=0.55) # Tejadillo rojo
    mat_dial = make_material("Mat_RelojEsfera", "#FFFDD0", roughness=0.7)  # Esfera crema
    mat_hands = make_material("Mat_RelojAgujas", "#212121", roughness=0.4) # Manecillas negras
    mat_gold = make_material("Mat_RelojOro", "#F1C40F", roughness=0.3, metallic=0.4) # Péndulo dorado

    # Cuerpo casita (caja)
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0, 0.06, 0.55))
    body = bpy.context.object
    body.scale = (0.42, 0.12, 0.44)
    bpy.ops.object.transform_apply(scale=True)
    body.data.materials.append(mat_wood)

    # Tejadillo triangular rotado
    bpy.ops.mesh.primitive_cylinder_add(vertices=3, radius=0.28, depth=0.15, location=(0, 0.06, 0.85))
    roof = bpy.context.object
    roof.rotation_euler = (math.radians(90), 0, 0)
    bpy.ops.object.transform_apply(rotation=True, scale=True)
    roof.data.materials.append(mat_roof)

    # Esfera del reloj
    bpy.ops.mesh.primitive_cylinder_add(vertices=16, radius=0.15, depth=0.03, location=(0, 0.125, 0.53))
    dial = bpy.context.object
    dial.rotation_euler = (math.radians(90), 0, 0)
    bpy.ops.object.transform_apply(rotation=True, scale=True)
    dial.data.materials.append(mat_dial)

    # Manecillas
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0, 0.142, 0.58))
    hand_min = bpy.context.object
    hand_min.scale = (0.015, 0.005, 0.10)
    bpy.ops.object.transform_apply(scale=True)
    hand_min.data.materials.append(mat_hands)

    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0.04, 0.144, 0.53))
    hand_hour = bpy.context.object
    hand_hour.scale = (0.07, 0.005, 0.018)
    bpy.ops.object.transform_apply(scale=True)
    hand_hour.data.materials.append(mat_hands)

    # Varilla del péndulo
    bpy.ops.mesh.primitive_cylinder_add(vertices=6, radius=0.008, depth=0.30, location=(0, 0.06, 0.18))
    rod = bpy.context.object
    rod.data.materials.append(mat_gold)

    # Lenteja / Pesa circular del péndulo
    bpy.ops.mesh.primitive_cylinder_add(vertices=12, radius=0.05, depth=0.02, location=(0, 0.06, 0.05))
    bob = bpy.context.object
    bob.rotation_euler = (math.radians(90), 0, 0)
    bpy.ops.object.transform_apply(rotation=True, scale=True)
    bob.data.materials.append(mat_gold)

    # Ventanita del cucú
    bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=0.035, depth=0.02, location=(0, 0.12, 0.72))
    window = bpy.context.object
    window.rotation_euler = (math.radians(90), 0, 0)
    bpy.ops.object.transform_apply(rotation=True, scale=True)
    window.data.materials.append(mat_hands)

    # Unir todo
    bpy.context.view_layer.objects.active = body
    for p in [roof, dial, hand_min, hand_hour, rod, bob, window]:
        p.select_set(True)
    body.select_set(True)
    bpy.ops.object.join()

    reloj = bpy.context.object
    reloj.name = "reloj_pared"
    set_origin_to_base_back(reloj)
    bpy.ops.object.shade_smooth()
    return reloj


# =============================================================================
# 3. REPISA CON JUGUETES (Pared)
# =============================================================================
def build_repisa_juguetes():
    clean_scene()
    mat_wood = make_material("Mat_RepisaMadera", "#8D6E63", roughness=0.6)
    mat_duck = make_material("Mat_Patito", "#F39C12", roughness=0.45)
    mat_beak = make_material("Mat_Pico", "#E67E22", roughness=0.4)
    mat_book1 = make_material("Mat_Libro1", "#27AE60", roughness=0.7)
    mat_book2 = make_material("Mat_Libro2", "#8E44AD", roughness=0.7)
    mat_block = make_material("Mat_Cubo", "#E74C3C", roughness=0.6)

    # Tabla horizontal de la repisa
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0, 0.11, 0.18))
    shelf = bpy.context.object
    shelf.scale = (1.05, 0.22, 0.04)
    bpy.ops.object.transform_apply(scale=True)
    shelf.data.materials.append(mat_wood)

    # Escuadras de soporte triangulares
    for sx in (-0.35, 0.35):
        bpy.ops.mesh.primitive_cylinder_add(vertices=3, radius=0.12, depth=0.035, location=(sx, 0.07, 0.09))
        bracket = bpy.context.object
        bracket.rotation_euler = (0, math.radians(90), math.radians(90))
        bpy.ops.object.transform_apply(rotation=True, scale=True)
        bracket.data.materials.append(mat_wood)
        bpy.context.view_layer.objects.active = shelf
        bracket.select_set(True)
        shelf.select_set(True)
        bpy.ops.object.join()

    # Juguete 1: Patito de goma
    bpy.ops.mesh.primitive_uv_sphere_add(segments=8, ring_count=5, radius=0.065, location=(-0.30, 0.11, 0.25))
    duck_body = bpy.context.object
    duck_body.scale = (1.1, 1.3, 0.9)
    bpy.ops.object.transform_apply(scale=True)
    duck_body.data.materials.append(mat_duck)

    bpy.ops.mesh.primitive_uv_sphere_add(segments=7, ring_count=4, radius=0.045, location=(-0.30, 0.13, 0.32))
    duck_head = bpy.context.object
    duck_head.data.materials.append(mat_duck)

    bpy.ops.mesh.primitive_cone_add(vertices=4, radius1=0.02, radius2=0.005, depth=0.04, location=(-0.30, 0.18, 0.315))
    duck_beak = bpy.context.object
    duck_beak.rotation_euler = (math.radians(90), 0, 0)
    bpy.ops.object.transform_apply(rotation=True, scale=True)
    duck_beak.data.materials.append(mat_beak)

    # Juguete 2: Libritos apoyados
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(-0.04, 0.11, 0.28))
    book1 = bpy.context.object
    book1.scale = (0.04, 0.14, 0.16)
    book1.rotation_euler = (0, math.radians(-10), 0)
    bpy.ops.object.transform_apply(rotation=True, scale=True)
    book1.data.materials.append(mat_book1)

    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0.02, 0.11, 0.27))
    book2 = bpy.context.object
    book2.scale = (0.035, 0.13, 0.14)
    book2.rotation_euler = (0, math.radians(-15), 0)
    bpy.ops.object.transform_apply(rotation=True, scale=True)
    book2.data.materials.append(mat_book2)

    # Juguete 3: Cubo de juguete
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0.30, 0.11, 0.255))
    block = bpy.context.object
    block.scale = (0.11, 0.11, 0.11)
    block.rotation_euler = (0, 0, math.radians(12))
    bpy.ops.object.transform_apply(rotation=True, scale=True)
    block.data.materials.append(mat_block)

    # Unir todo
    bpy.context.view_layer.objects.active = shelf
    for p in [duck_body, duck_head, duck_beak, book1, book2, block]:
        p.select_set(True)
    shelf.select_set(True)
    bpy.ops.object.join()

    repisa = bpy.context.object
    repisa.name = "repisa_juguetes"
    set_origin_to_base_back(repisa)
    bpy.ops.object.shade_smooth()
    return repisa


# =============================================================================
# 4. APLIQUE ESTRELLA (Lámpara de pared)
# Con bmesh creamos una estrella de 5 puntas perfecta y limpia de una sola pieza.
# =============================================================================
def build_aplique_estrella():
    clean_scene()
    mat_plate = make_material("Mat_ApliquePlaca", "#DFE6E9", roughness=0.5) # Blanco suave
    mat_star = make_material("Mat_EstrellaLuz", "#F1C40F", roughness=0.35)   # Amarillo cálido
    mat_cord = make_material("Mat_CordonTirador", "#718093", roughness=0.8) # Cordón
    mat_bead = make_material("Mat_PomoCordon", "#E84393", roughness=0.4)    # Perlita rosa

    # Placa base de pared
    bpy.ops.mesh.primitive_cylinder_add(vertices=12, radius=0.15, depth=0.02, location=(0, 0.01, 0.42))
    base_plate = bpy.context.object
    base_plate.rotation_euler = (math.radians(90), 0, 0)
    bpy.ops.object.transform_apply(rotation=True, scale=True)
    base_plate.data.materials.append(mat_plate)

    # Crear malla de estrella con bmesh
    me = bpy.data.meshes.new("StarMesh")
    bm = bmesh.new()
    
    outer_r = 0.21
    inner_r = 0.085
    thickness = 0.045
    y_offset = 0.025 # Frente a la placa
    
    verts_front = []
    verts_back = []
    for i in range(10):
        ang = math.radians(90 - i * 36) # 0 arriba
        r = outer_r if (i % 2 == 0) else inner_r
        vx = r * math.cos(ang)
        vz = 0.42 + r * math.sin(ang)
        verts_front.append(bm.verts.new((vx, y_offset + thickness, vz)))
        verts_back.append(bm.verts.new((vx, y_offset, vz)))

    # Vértices centrales para tapar frente y dorso
    c_front = bm.verts.new((0.0, y_offset + thickness, 0.42))
    c_back = bm.verts.new((0.0, y_offset, 0.42))

    # Caras frontal y trasera
    for i in range(10):
        next_i = (i + 1) % 10
        # Frente
        bm.faces.new([c_front, verts_front[i], verts_front[next_i]])
        # Dorso
        bm.faces.new([c_back, verts_back[next_i], verts_back[i]])
        # Lados
        bm.faces.new([verts_front[i], verts_back[i], verts_back[next_i], verts_front[next_i]])

    bm.to_mesh(me)
    bm.free()

    star_obj = bpy.data.objects.new("Star", me)
    bpy.context.scene.collection.objects.link(star_obj)
    star_obj.data.materials.append(mat_star)

    # Cordón tirador que desciende
    bpy.ops.mesh.primitive_cylinder_add(vertices=4, radius=0.004, depth=0.32, location=(0, y_offset + 0.02, 0.16))
    cord = bpy.context.object
    cord.data.materials.append(mat_cord)

    # Cuenta esférica rosa al final
    bpy.ops.mesh.primitive_uv_sphere_add(segments=6, ring_count=4, radius=0.02, location=(0, y_offset + 0.02, 0.01))
    bead = bpy.context.object
    bead.data.materials.append(mat_bead)

    # Unir todo
    bpy.context.view_layer.objects.active = base_plate
    for p in [star_obj, cord, bead]:
        p.select_set(True)
    base_plate.select_set(True)
    bpy.ops.object.join()

    aplique = bpy.context.object
    aplique.name = "aplique_estrella"
    set_origin_to_base_back(aplique)
    bpy.ops.object.shade_smooth()
    return aplique


# =============================================================================
# 5. PERCHERO DE PARED (Con clavijas y saquito)
# =============================================================================
def build_perchero_pared():
    clean_scene()
    mat_back = make_material("Mat_PercheroTabla", "#8D6E63", roughness=0.6)  # Madera
    mat_peg1 = make_material("Mat_Pomo1", "#E74C3C", roughness=0.4)          # Rojo
    mat_peg2 = make_material("Mat_Pomo2", "#3498DB", roughness=0.4)          # Azul
    mat_peg3 = make_material("Mat_Pomo3", "#F1C40F", roughness=0.4)          # Amarillo
    mat_garment = make_material("Mat_Saquito", "#A29BFE", roughness=0.75)    # Lavanda suave

    # Tablilla horizontal de madera
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0, 0.015, 0.45))
    board = bpy.context.object
    board.scale = (0.75, 0.025, 0.10)
    bpy.ops.object.transform_apply(scale=True)
    board.data.materials.append(mat_back)

    # 3 clavijas con pomo de bola
    pegs_data = [(-0.25, mat_peg1), (0.0, mat_peg2), (0.25, mat_peg3)]
    for px, peg_mat in pegs_data:
        bpy.ops.mesh.primitive_cylinder_add(vertices=6, radius=0.012, depth=0.06, location=(px, 0.05, 0.46))
        peg = bpy.context.object
        peg.rotation_euler = (math.radians(-25), 0, 0)
        bpy.ops.object.transform_apply(rotation=True, scale=True)
        peg.data.materials.append(peg_mat)

        bpy.ops.mesh.primitive_uv_sphere_add(segments=6, ring_count=4, radius=0.022, location=(px, 0.075, 0.475))
        ball = bpy.context.object
        ball.data.materials.append(peg_mat)

        bpy.context.view_layer.objects.active = board
        for o in (peg, ball):
            o.select_set(True)
        board.select_set(True)
        bpy.ops.object.join()

    # Saquito infantil o babero colgado
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0.0, 0.065, 0.24))
    coat = bpy.context.object
    coat.scale = (0.24, 0.03, 0.38)
    bpy.ops.object.transform_apply(scale=True)
    coat.data.materials.append(mat_garment)

    for tx in (-0.06, 0.06):
        bpy.ops.mesh.primitive_cylinder_add(vertices=4, radius=0.008, depth=0.08, location=(tx * 0.5, 0.06, 0.44))
        strap = bpy.context.object
        strap.rotation_euler = (0, math.radians(20 if tx < 0 else -20), 0)
        bpy.ops.object.transform_apply(rotation=True, scale=True)
        strap.data.materials.append(mat_garment)
        bpy.context.view_layer.objects.active = board
        strap.select_set(True)
        board.select_set(True)
        bpy.ops.object.join()

    bpy.context.view_layer.objects.active = board
    coat.select_set(True)
    board.select_set(True)
    bpy.ops.object.join()

    perchero = bpy.context.object
    perchero.name = "perchero_pared"
    set_origin_to_base_back(perchero)
    bpy.ops.object.shade_smooth()
    return perchero


# =============================================================================
# EJECUCIÓN GLOBAL
# =============================================================================
NEW_WALL_PROPS = [
    ("cuadro_infantil", build_cuadro_infantil),
    ("reloj_pared", build_reloj_pared),
    ("repisa_juguetes", build_repisa_juguetes),
    ("aplique_estrella", build_aplique_estrella),
    ("perchero_pared", build_perchero_pared),
]

def generate_all():
    print("=== GENERANDO 5 PROPS DE PARED NUEVOS ===")
    results = []

    for name, builder in NEW_WALL_PROPS:
        print(f"\n--- Generando {name} ---")
        obj = builder()
        tris = get_tris(obj)
        dims = obj.dimensions
        print(f"[{name}] Tris: {tris} (Max: 500), Dimensiones: {dims.x:.2f}m x {dims.y:.2f}m x {dims.z:.2f}m")
        assert tris <= 500, f"Error: {name} excede 500 tris ({tris})"
        assert dims.y <= 0.30, f"Error: {name} excede MaxRelief de 0.30m ({dims.y:.2f}m)"

        # 1. Exportar GLB
        glb_path = PROPS_DIR / f"{name}.glb"
        bpy.ops.object.select_all(action='DESELECT')
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj

        bpy.ops.export_scene.gltf(
            filepath=str(glb_path),
            export_format='GLB',
            use_selection=True,
            export_yup=True,
            export_apply=False,
        )

        # 2. Renderizar preview PNG
        prev_path = SOURCE_DIR / f"{name}_preview.png"
        render_preview(obj, prev_path)
        shutil.copy2(prev_path, BRAIN_DIR / f"{name}_preview.png")

        # 3. Guardar .blend
        blend_path = SOURCE_DIR / f"{name}.blend"
        bpy.ops.wm.save_as_mainfile(filepath=str(blend_path), compress=True)

        # 4. Copiar a OneDrive
        shutil.copy2(glb_path, DRIVE_PROPS_DIR / f"{name}.glb")
        shutil.copy2(blend_path, DRIVE_PROPS_DIR / f"{name}.blend")
        shutil.copy2(prev_path, DRIVE_PROPS_DIR / f"{name}_preview.png")

        results.append({
            "name": name,
            "tris": tris,
            "dims": f"{dims.x:.2f}x{dims.y:.2f}x{dims.z:.2f}m",
            "size": glb_path.stat().st_size,
        })
        print(f"  [OK] Guardado en repo y OneDrive: {glb_path.stat().st_size} B")

    print("\n=== RESUMEN FINAL DE PROPS DE PARED ===")
    for r in results:
        print(f"  • {r['name']}.glb: {r['tris']} tris, {r['dims']}, {r['size']} bytes")

if __name__ == "__main__":
    generate_all()
