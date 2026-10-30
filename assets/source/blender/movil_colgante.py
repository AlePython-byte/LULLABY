"""Generador oficial de movil_colgante.glb (Prop #3 de The Cradle) para LULLABY:
Referencia visual exacta (Nano Banana aprobada):
  - Gancho superior de madera estilizado en forma de interrogación con base anillada
  - Cuerdas de suspensión convergentes a un nudo central
  - Estructura de radios de madera oscura en estrella (6 brazos a 60°)
  - Cuerdas finas colgantes desde los extremos y el centro
  - Luna creciente central de felpa en color crema/marfil
  - 3 estrellas de felpa en amarillo pastel
  - 3 nubes mullidas en verde azulado/menta pastel
  - Disposición alternada (estrella, nube, estrella, nube, estrella, nube) con alturas escalonadas

Reglas técnicas:
  - Presupuesto: <= 800 triángulos (estricto)
  - Origen en la base (Z = 0 en el punto más bajo de las figuras colgantes)
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
DRIVE_DIR = Path(r"C:\Users\davin\OneDrive\Documentos\MODELOS JUEGO\movil_colgante")
ARTIFACTS_DIR = Path(r"C:\Users\davin\.gemini\antigravity\brain\61207137-232d-4414-8860-f3fa45906692")

PROPS_MODELS_DIR.mkdir(parents=True, exist_ok=True)
SOURCE_DIR.mkdir(parents=True, exist_ok=True)
DRIVE_DIR.mkdir(parents=True, exist_ok=True)

GLB_PATH = PROPS_MODELS_DIR / "movil_colgante.glb"
BLEND_PATH = SOURCE_DIR / "movil_colgante.blend"
PY_PATH = SOURCE_DIR / "movil_colgante.py"
PREVIEW_PATH = SOURCE_DIR / "movil_colgante_preview.png"
ARTIFACT_PREVIEW = ARTIFACTS_DIR / "movil_colgante_preview.png"

RENDER_SIZE = 800

# Paleta exacta de la referencia (sRGB)
PALETTE = {
    "DarkWood": (0.34, 0.31, 0.35),       # Madera envejecida oscura para brazos y gancho
    "CordColor": (0.48, 0.44, 0.40),      # Cuerdas de hilo/yute
    "MoonCream": (0.90, 0.88, 0.84),      # Luna creciente crema/marfil suave
    "StarYellow": (0.92, 0.86, 0.56),     # Estrellas amarillo pastel
    "CloudTeal": (0.56, 0.76, 0.74),      # Nubes verde azulado/menta pastel
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


def create_cylinder(name, radius, depth, location, rotation=(0, 0, 0), vertices=6, material=None):
    bpy.ops.mesh.primitive_cylinder_add(vertices=vertices, radius=radius, depth=depth, location=location, rotation=rotation)
    obj = bpy.context.active_object
    obj.name = name
    if material:
        obj.data.materials.append(material)
    return obj


def build_hook(name, location, scale=1.0, material=None):
    """Construye el gancho de madera superior con forma de interrogación curvada."""
    # 7 segmentos curvados formando el arco y el pie
    verts = []
    faces = []
    
    # Perfil central 2D en el plano XZ
    points = [
        (0.00, 0.00),    # Anillo/base inferior
        (0.00, 0.06),    # Cuello vertical
        (0.02, 0.12),    # Comienzo curva
        (0.07, 0.16),    # Pico derecho
        (0.07, 0.23),    # Pico superior
        (0.00, 0.27),    # Cima del gancho
        (-0.06, 0.25),   # Parte superior izquierda
        (-0.08, 0.19),   # Final hacia abajo
    ]
    
    thick = 0.028 * scale
    depth = 0.032 * scale
    
    for i, (px, pz) in enumerate(points):
        x = px * scale + location[0]
        z = pz * scale + location[1]
        y = location[2]
        
        # 4 vértices por segmento
        verts.append((x - thick / 2.0, y - depth / 2.0, z))
        verts.append((x + thick / 2.0, y - depth / 2.0, z))
        verts.append((x + thick / 2.0, y + depth / 2.0, z))
        verts.append((x - thick / 2.0, y + depth / 2.0, z))
        
    for i in range(len(points) - 1):
        idx0 = i * 4
        idx1 = (i + 1) * 4
        faces.append((idx0 + 0, idx1 + 0, idx1 + 1, idx0 + 1))
        faces.append((idx0 + 1, idx1 + 1, idx1 + 2, idx0 + 2))
        faces.append((idx0 + 2, idx1 + 2, idx1 + 3, idx0 + 3))
        faces.append((idx0 + 3, idx1 + 3, idx1 + 0, idx0 + 0))
        
    # Tapas
    faces.append((0, 1, 2, 3))
    last = (len(points) - 1) * 4
    faces.append((last + 0, last + 3, last + 2, last + 1))
    
    mesh = bpy.data.meshes.new(f"{name}_Mesh")
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    if material:
        obj.data.materials.append(material)
    return obj


def build_crescent_moon(name, location, scale=0.18, material=None):
    """Construye una luna creciente 3D de felpa low-poly."""
    verts = []
    faces = []
    
    # 7 segmentos a lo largo de la curva
    outer_angles = np.linspace(-math.pi * 0.45, math.pi * 0.45, 7)
    inner_angles = np.linspace(-math.pi * 0.45, math.pi * 0.45, 7)
    
    r_outer = 1.0 * scale
    r_inner = 0.72 * scale
    offset_inner = 0.35 * scale
    depth = 0.07 * scale
    
    # Capa frontal (+Y) y capa trasera (-Y)
    pts_outer = [(math.cos(a) * r_outer, math.sin(a) * r_outer) for a in outer_angles]
    pts_inner = [(math.cos(a) * r_inner + offset_inner, math.sin(a) * r_inner) for a in inner_angles]
    
    # Generar prisma extruido
    n = len(pts_outer)
    # Front vertices (z=depth/2)
    for x, z in pts_outer:
        verts.append((location[0] + x - offset_inner * 0.5, location[1] + depth / 2.0, location[2] + z))
    for x, z in pts_inner:
        verts.append((location[0] + x - offset_inner * 0.5, location[1] + depth / 2.0, location[2] + z))
    # Back vertices (z=-depth/2)
    for x, z in pts_outer:
        verts.append((location[0] + x - offset_inner * 0.5, location[1] - depth / 2.0, location[2] + z))
    for x, z in pts_inner:
        verts.append((location[0] + x - offset_inner * 0.5, location[1] - depth / 2.0, location[2] + z))
        
    # Caras frontales
    for i in range(n - 1):
        # outer i, outer i+1, inner i+1, inner i
        faces.append((i, i + 1, n + i + 1, n + i))
    # Caras traseras
    offset_back = 2 * n
    for i in range(n - 1):
        faces.append((offset_back + n + i, offset_back + n + i + 1, offset_back + i + 1, offset_back + i))
    # Borde exterior
    for i in range(n - 1):
        faces.append((i, offset_back + i, offset_back + i + 1, i + 1))
    # Borde interior
    for i in range(n - 1):
        faces.append((n + i + 1, offset_back + n + i + 1, offset_back + n + i, n + i))
    # Tapas en las puntas (unir extremos superior e inferior)
    faces.append((0, n, offset_back + n, offset_back + 0))
    faces.append((n - 1, offset_back + n - 1, offset_back + 2 * n - 1, 2 * n - 1))
    
    mesh = bpy.data.meshes.new(f"{name}_Mesh")
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    if material:
        obj.data.materials.append(material)
    return obj


def build_plush_star(name, location, scale=0.12, thickness=0.065, material=None):
    """Construye una estrella de felpa 3D de 5 puntas low-poly."""
    verts = []
    faces = []
    
    num_points = 5
    r_outer = scale
    r_inner = scale * 0.52
    
    angles = []
    for i in range(num_points * 2):
        a = i * (math.pi / num_points) + math.pi / 2.0
        r = r_outer if i % 2 == 0 else r_inner
        angles.append((math.cos(a) * r, math.sin(a) * r))
        
    half_t = thickness / 2.0
    center_front = len(verts)
    verts.append((location[0], location[1] + half_t * 1.25, location[2]))  # Centro inflado frontal
    center_back = len(verts)
    verts.append((location[0], location[1] - half_t * 1.25, location[2]))  # Centro inflado trasero
    
    base_idx = len(verts)
    # Vértices del contorno medio (con grosor)
    for x, z in angles:
        verts.append((location[0] + x, location[1] + half_t, location[2] + z))
    for x, z in angles:
        verts.append((location[0] + x, location[1] - half_t, location[2] + z))
        
    n = len(angles)
    # Caras frontales hacia el centro
    for i in range(n):
        nxt = (i + 1) % n
        faces.append((center_front, base_idx + i, base_idx + nxt))
    # Caras traseras hacia el centro
    for i in range(n):
        nxt = (i + 1) % n
        faces.append((center_back, base_idx + n + nxt, base_idx + n + i))
    # Borde lateral
    for i in range(n):
        nxt = (i + 1) % n
        faces.append((base_idx + i, base_idx + n + i, base_idx + n + nxt, base_idx + nxt))
        
    mesh = bpy.data.meshes.new(f"{name}_Mesh")
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    if material:
        obj.data.materials.append(material)
    return obj


def build_plush_cloud(name, location, scale=(0.20, 0.08, 0.11), material=None):
    """Construye una nube de felpa de 3 lóbulos suaves low-poly."""
    parts = []
    
    # 1. Lóbulo central
    bpy.ops.mesh.primitive_uv_sphere_add(segments=6, ring_count=4, radius=scale[2], location=location)
    c_mid = bpy.context.active_object
    c_mid.scale = (scale[0] * 0.6, scale[1] * 0.9, scale[2])
    bpy.ops.object.transform_apply(scale=True)
    parts.append(c_mid)
    
    # 2. Lóbulo izquierdo
    loc_l = (location[0] - scale[0] * 0.38, location[1], location[2] - scale[2] * 0.18)
    bpy.ops.mesh.primitive_uv_sphere_add(segments=5, ring_count=3, radius=scale[2] * 0.75, location=loc_l)
    c_l = bpy.context.active_object
    c_l.scale = (scale[0] * 0.5, scale[1] * 0.85, scale[2] * 0.75)
    bpy.ops.object.transform_apply(scale=True)
    parts.append(c_l)
    
    # 3. Lóbulo derecho
    loc_r = (location[0] + scale[0] * 0.38, location[1], location[2] - scale[2] * 0.18)
    bpy.ops.mesh.primitive_uv_sphere_add(segments=5, ring_count=3, radius=scale[2] * 0.75, location=loc_r)
    c_r = bpy.context.active_object
    c_r.scale = (scale[0] * 0.5, scale[1] * 0.85, scale[2] * 0.75)
    bpy.ops.object.transform_apply(scale=True)
    parts.append(c_r)
    
    bpy.ops.object.select_all(action='DESELECT')
    for p in parts:
        p.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.join()
    cloud = bpy.context.active_object
    cloud.name = name
    if material:
        cloud.data.materials.append(material)
    return cloud


def build_hanging_mobile(materials):
    """Construye el móvil completo con origen en el suelo (Z = 0 en el punto más bajo)."""
    parts_frame = []
    parts_stars = []
    parts_clouds = []
    
    # Parámetros generales:
    # Altura del gancho superior: Z = 1.05 a 1.30 m
    # Nudo/ápice de suspensión superior: Z = 1.05 m
    # Estructura de radios de madera: Z = 0.85 m
    # Longitud de cada radio de madera: radio exterior R = 0.40 m (diámetro 0.80 m)
    # Figuras colgantes:
    #   - Nubes: Z = 0.40 a 0.48 m
    #   - Luna: Z = 0.30 a 0.52 m
    #   - Estrellas: Z = 0.08 a 0.28 m (punto más bajo toca Z = 0.00 m)
    
    z_spokes = 0.85
    z_apex = 1.05
    z_hook_base = 1.08
    r_spokes = 0.40
    
    # 1. Gancho de madera superior
    hook = build_hook("Mobile_Hook", location=(0.0, z_hook_base, 0.0), scale=1.0, material=materials["DarkWood"])
    parts_frame.append(hook)
    
    # Cuerda vertical que conecta el gancho al nudo ápice
    cord_top = create_cylinder("Cord_Top", radius=0.007, depth=z_hook_base - z_apex, location=(0, 0, (z_hook_base + z_apex) / 2.0), vertices=4, material=materials["CordColor"])
    parts_frame.append(cord_top)
    
    # Nudo central de madera / tope
    knot = create_cylinder("Knot_Apex", radius=0.022, depth=0.028, location=(0, 0, z_apex), vertices=6, material=materials["DarkWood"])
    parts_frame.append(knot)
    
    # 2. Brazos de madera en estrella (3 vigas continuas cruzadas a 0°, 60° y 120°)
    beam_w = 0.038
    beam_h = 0.042
    beam_len = 2 * r_spokes
    
    for i, angle_deg in enumerate([0, 60, 120]):
        angle_rad = math.radians(angle_deg)
        # Ligero desfase vertical para el encastre de madera
        z_offset = (i - 1) * 0.006
        beam = create_box(f"Spoke_Beam_{i}", (beam_len, beam_w, beam_h), (0, 0, z_spokes + z_offset), rotation=(0, 0, angle_rad), material=materials["DarkWood"])
        # Tapitas en los extremos
        parts_frame.append(beam)
        
    # Anillo/tope central de encastre
    hub = create_cylinder("Spoke_Hub", radius=0.045, depth=0.055, location=(0, 0, z_spokes), vertices=6, material=materials["DarkWood"])
    parts_frame.append(hub)
    
    # 3. Cuerdas de suspensión superiores (del nudo z_apex a los 6 extremos de los radios)
    for i in range(6):
        a = math.radians(i * 60)
        end_x = math.cos(a) * (r_spokes - 0.04)
        end_y = math.sin(a) * (r_spokes - 0.04)
        mid_x = end_x / 2.0
        mid_y = end_y / 2.0
        mid_z = (z_apex + z_spokes) / 2.0
        
        dx = end_x
        dy = end_y
        dz = z_spokes - z_apex
        dist = math.sqrt(dx * dx + dy * dy + dz * dz)
        
        # Orientar cilindro hacia el vector
        rot_y = math.atan2(math.sqrt(dx * dx + dy * dy), dz)
        rot_z = math.atan2(dy, dx)
        
        cord_up = create_cylinder(f"Cord_Upper_{i}", radius=0.004, depth=dist, location=(mid_x, mid_y, mid_z), vertices=4, material=materials["CordColor"])
        # Rotación para alinear el cilindro (eje Z local)
        direction = Vector((dx, dy, dz)).normalized()
        rot_quat = Vector((0, 0, 1)).rotation_difference(direction)
        cord_up.rotation_euler = rot_quat.to_euler()
        parts_frame.append(cord_up)
        
    # 4. Figuras colgantes y sus cuerdas inferiores
    # Alternancia: i=0 (Estrella), i=1 (Nube), i=2 (Estrella), i=3 (Nube), i=4 (Estrella), i=5 (Nube)
    # Estrella 1 apunta hacia el frente (+Y)
    
    for i in range(6):
        a = math.radians(i * 60 + 30)  # Rotación ligera para centrar mejor
        arm_x = math.cos(a) * (r_spokes - 0.03)
        arm_y = math.sin(a) * (r_spokes - 0.03)
        
        is_star = (i % 2 == 0)
        if is_star:
            # Estrella: cuelga más baja
            # Centro de la estrella a Z = 0.14m (su punta más baja toca Z = 0.02m)
            fig_z = 0.16
            cord_len = z_spokes - fig_z - 0.10
            cord_z = z_spokes - cord_len / 2.0
            
            cord = create_cylinder(f"Cord_Star_{i}", radius=0.0035, depth=cord_len, location=(arm_x, arm_y, cord_z), vertices=4, material=materials["CordColor"])
            parts_frame.append(cord)
            
            star = build_plush_star(f"Star_{i}", location=(arm_x, arm_y, fig_z), scale=0.13, thickness=0.065, material=materials["StarYellow"])
            # Orientar la estrella hacia el exterior
            star.rotation_euler = (0, 0, a + math.radians(90))
            parts_stars.append(star)
        else:
            # Nube: cuelga a media altura
            fig_z = 0.42
            cord_len = z_spokes - fig_z - 0.08
            cord_z = z_spokes - cord_len / 2.0
            
            cord = create_cylinder(f"Cord_Cloud_{i}", radius=0.0035, depth=cord_len, location=(arm_x, arm_y, cord_z), vertices=4, material=materials["CordColor"])
            parts_frame.append(cord)
            
            cloud = build_plush_cloud(f"Cloud_{i}", location=(arm_x, arm_y, fig_z), scale=(0.18, 0.075, 0.09), material=materials["CloudTeal"])
            cloud.rotation_euler = (0, 0, a + math.radians(90))
            parts_clouds.append(cloud)
            
    # 5. Luna Creciente Central
    moon_z = 0.36
    cord_moon_len = z_spokes - moon_z - 0.12
    cord_moon_z = z_spokes - cord_moon_len / 2.0
    cord_moon = create_cylinder("Cord_Moon", radius=0.004, depth=cord_moon_len, location=(0, 0, cord_moon_z), vertices=4, material=materials["CordColor"])
    parts_frame.append(cord_moon)
    
    moon = build_crescent_moon("Mobile_Moon", location=(0, 0, moon_z), scale=0.18, material=materials["MoonCream"])
    
    # 6. Unir piezas en partes semánticas
    # Mobile_Frame (Gancho, radios, cuerdas y nudos)
    bpy.ops.object.select_all(action='DESELECT')
    for p in parts_frame:
        p.select_set(True)
    bpy.context.view_layer.objects.active = parts_frame[0]
    bpy.ops.object.join()
    frame = bpy.context.active_object
    frame.name = "Mobile_Frame"
    
    # Mobile_Stars
    bpy.ops.object.select_all(action='DESELECT')
    for p in parts_stars:
        p.select_set(True)
    bpy.context.view_layer.objects.active = parts_stars[0]
    bpy.ops.object.join()
    stars = bpy.context.active_object
    stars.name = "Mobile_Stars"
    
    # Mobile_Clouds
    bpy.ops.object.select_all(action='DESELECT')
    for p in parts_clouds:
        p.select_set(True)
    bpy.context.view_layer.objects.active = parts_clouds[0]
    bpy.ops.object.join()
    clouds = bpy.context.active_object
    clouds.name = "Mobile_Clouds"
    
    # Ajuste final: asegurar que la base exacta esté en Z = 0
    all_objs = [frame, moon, stars, clouds]
    min_z = min(obj.bound_box[0][2] + obj.location[2] for obj in all_objs)
    if abs(min_z) > 0.001:
        for obj in all_objs:
            obj.location.z -= min_z
            
    return [frame, moon, stars, clouds]


def setup_preview_scene(parts):
    light_data = bpy.data.lights.new(name="KeyLight", type='SUN')
    light_data.energy = 3.5
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
    rim_obj.location = (0, -1.8, 1.8)
    bpy.context.collection.objects.link(rim_obj)

    center = (0.0, 0.0, 0.65)
    dist = 2.6

    cams = []

    # 1. Cámara frontal
    cam_front = bpy.data.cameras.new("Cam_Front")
    cam_front.lens = 50
    o_front = bpy.data.objects.new("Cam_Front_Obj", cam_front)
    o_front.location = (0.0, dist + 0.1, center[2])
    o_front.rotation_euler = (math.radians(85), 0.0, math.radians(180))
    bpy.context.collection.objects.link(o_front)
    cams.append(("front", o_front))

    # 2. Cámara isométrica 3/4
    cam_iso = bpy.data.cameras.new("Cam_Iso")
    cam_iso.lens = 50
    o_iso = bpy.data.objects.new("Cam_Iso_Obj", cam_iso)
    iso_dist = 2.5
    o_iso.location = (iso_dist * 0.7, iso_dist * 0.7, 1.6)
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
    out = bpy.data.images.new("movil_preview", sheet.shape[1], sheet.shape[0], alpha=False)
    out.pixels.foreach_set(sheet.ravel())
    out.filepath_raw = str(output_path)
    out.file_format = 'PNG'
    out.save()
    bpy.data.images.remove(out)


def main():
    print("[movil_colgante] Iniciando modelado oficial del Móvil Colgante (Prop #3)...")
    clean_scene()
    materials = make_materials()
    parts = build_hanging_mobile(materials)

    part_names = [p.name for p in parts]
    print(f"[movil_colgante] Partes construidas ({len(parts)}): {', '.join(part_names)}")

    total_tris = 0
    for p in parts:
        p.data.calc_loop_triangles()
        t = len(p.data.loop_triangles)
        total_tris += t
        print(f"  • {p.name}: {t} tris, loc={p.location}, dims={p.dimensions}")
    print(f"[movil_colgante] Triángulos totales: {total_tris} (Límite: <= 800)")
    assert total_tris <= 800, f"Excede límite de 800 tris: {total_tris}"

    # Exportar GLB oficial en assets/models/props/movil_colgante.glb
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
    print(f"[movil_colgante] Exportado GLB oficial -> {GLB_PATH}")

    # Renderizar vista previa de 3 paneles
    cameras = setup_preview_scene(parts)
    render_preview_sheet(cameras, PREVIEW_PATH)
    print(f"[movil_colgante] Vista previa generada -> {PREVIEW_PATH}")

    # Guardar archivo .blend oficial
    bpy.ops.wm.save_as_mainfile(filepath=str(BLEND_PATH), compress=True)
    print(f"[movil_colgante] Archivo .blend oficial -> {BLEND_PATH}")

    # Copiar script fuente a assets/source/blender/movil_colgante.py
    this_script = Path(__file__).resolve()
    shutil.copy2(this_script, PY_PATH)
    print(f"[movil_colgante] Script fuente guardado en -> {PY_PATH}")

    # Copiar entrega a la carpeta designada de David: MODELOS JUEGO/movil_colgante/
    shutil.copy2(GLB_PATH, DRIVE_DIR / "movil_colgante.glb")
    shutil.copy2(BLEND_PATH, DRIVE_DIR / "movil_colgante.blend")
    shutil.copy2(PY_PATH, DRIVE_DIR / "movil_colgante.py")
    shutil.copy2(PREVIEW_PATH, DRIVE_DIR / "movil_colgante_preview.png")
    shutil.copy2(PREVIEW_PATH, ARTIFACT_PREVIEW)
    print(f"[movil_colgante] Copia guardada en carpeta de David -> {DRIVE_DIR}")

    print("[movil_colgante] ¡Modelado de móvil colgante completado con éxito!")


if __name__ == "__main__":
    main()
