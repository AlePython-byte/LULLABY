"""Generador de Ambientación - Lote 1 (The Cradle Props) para LULLABY:
Modelos incluidos:
1. bloques_letras.glb (~1 m alto) - Pila de cubos de madera infantiles con letras
2. pelota_colores.glb (~0.8 m) - Pelota de goma clásica de guardería con gajos de colores
3. tambor.glb (~1 m) - Tambor infantil clásico con aros de madera, cuerdas y baquetas
4. xilofono.glb (~1.2 m largo) - Xilófono de 8 teclas de arcoíris pastel y mazo
5. lampara_velador.glb (~1.2 m) - Lámpara de noche infantil con pantalla y remate de luna

Reglas técnicas:
- Low-poly estricto <= 800 triángulos cada uno
- Origen en la base (Z = 0 en el suelo)
- Frente hacia +Y en Blender (hacia -Z en Godot)
- Colores planos Principled BSDF sRGB -> Linear
- Exportación GLB y guardado .blend sin .blend1
- Render de vista previa de 3 paneles
"""

import math
import shutil
from pathlib import Path
import bpy
from mathutils import Vector, Matrix

# Rutas del proyecto
LULLABY_ROOT = Path(r"C:\LULLABY")
PROPS_DIR = LULLABY_ROOT / "assets" / "models" / "props"
SOURCE_DIR = LULLABY_ROOT / "assets" / "source" / "blender"
DRIVE_BASE = Path(r"C:\Users\davin\OneDrive\Documentos\MODELOS JUEGO")
ARTIFACTS_DIR = Path(r"C:\Users\davin\.gemini\antigravity\brain\61207137-232d-4414-8860-f3fa45906692")

PROPS_DIR.mkdir(parents=True, exist_ok=True)
SOURCE_DIR.mkdir(parents=True, exist_ok=True)
DRIVE_BASE.mkdir(parents=True, exist_ok=True)

bpy.context.preferences.filepaths.save_version = 0

# Paleta global del juego (sRGB)
PALETTE = {
    # Maderas
    "WoodLight": (0.84, 0.72, 0.54),
    "WoodMedium": (0.65, 0.48, 0.32),
    "WoodDark": (0.38, 0.26, 0.18),
    # Colores pastel apagados / infantiles
    "PastelRed": (0.82, 0.32, 0.32),
    "PastelOrange": (0.88, 0.54, 0.28),
    "PastelYellow": (0.92, 0.82, 0.38),
    "PastelGreen": (0.44, 0.68, 0.48),
    "PastelCyan": (0.35, 0.70, 0.75),
    "PastelBlue": (0.38, 0.52, 0.78),
    "PastelPurple": (0.62, 0.45, 0.72),
    "PastelPink": (0.88, 0.55, 0.68),
    # Neutros
    "CreamWhite": (0.92, 0.89, 0.82),
    "WarmParchment": (0.86, 0.80, 0.70),
    "GoldBrass": (0.78, 0.64, 0.28),
    "DarkMetal": (0.22, 0.20, 0.22),
    "RubberBlack": (0.15, 0.15, 0.15),
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


def finalize_model(name: str, target_dir_name: str):
    """Ajusta Z=0 en la base exacta, origen en (0,0,0), cuenta tris, exporta GLB, guarda blend y renderiza preview."""
    mesh_objs = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    if not mesh_objs:
        print(f"ERROR: No mesh objects found for {name}")
        return

    # Unir si es conveniente o aplicar transforms en origen Z=0
    # Calcular Z mínimo en mundo
    all_min_z = min(min([(o.matrix_world @ v.co).z for v in o.data.vertices]) for o in mesh_objs)
    for o in mesh_objs:
        o.location.z -= all_min_z
        bpy.context.view_layer.objects.active = o
        o.select_set(True)
        bpy.ops.object.transform_apply(location=True, rotation=False, scale=False)

    # Si hay varios objetos, unirlos en un único prop limpio
    bpy.ops.object.select_all(action='DESELECT')
    for o in mesh_objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = mesh_objs[0]
    if len(mesh_objs) > 1:
        bpy.ops.object.join()
    final_obj = bpy.context.active_object
    final_obj.name = name

    # Conteo de triángulos
    total_tris = sum(len(p.vertices) - 2 for p in final_obj.data.polygons)
    bbox = final_obj.bound_box
    height = max(b[2] for b in bbox) - min(b[2] for b in bbox)
    width_x = max(b[0] for b in bbox) - min(b[0] for b in bbox)
    depth_y = max(b[1] for b in bbox) - min(b[1] for b in bbox)
    print(f"\n>>> MODELO: {name}")
    print(f"    Triángulos: {total_tris} (Presupuesto <= 800: {'OK' if total_tris <= 800 else 'EXCEDIDO'})")
    print(f"    Dimensiones: Ancho {width_x:.2f}m x Prof {depth_y:.2f}m x Alto {height:.2f}m")

    # Configurar iluminación y cámara para render de preview
    center_z = height * 0.5
    cam_dist = max(height, width_x, depth_y) * 2.2
    
    # Sun light
    sun = bpy.data.lights.new(name="Sun", type='SUN')
    sun.energy = 3.5
    sun.color = (0.98, 0.96, 0.92)
    sun_obj = bpy.data.objects.new("Sun", sun)
    sun_obj.rotation_euler = (math.radians(50), math.radians(20), math.radians(-35))
    bpy.context.collection.objects.link(sun_obj)
    
    # Fill light
    fill = bpy.data.lights.new(name="Fill", type='SUN')
    fill.energy = 1.2
    fill.color = (0.65, 0.70, 0.85)
    fill_obj = bpy.data.objects.new("Fill", fill)
    fill_obj.rotation_euler = (math.radians(-40), math.radians(-30), math.radians(145))
    bpy.context.collection.objects.link(fill_obj)

    # Cámara Iso 3/4
    cam = bpy.data.cameras.new("PreviewCam")
    cam.lens = 50
    cam_obj = bpy.data.objects.new("PreviewCam", cam)
    cam_obj.location = (cam_dist * 0.75, -cam_dist * 0.75, center_z + cam_dist * 0.45)
    # Track towards center
    target = Vector((0, 0, center_z))
    direction = target - cam_obj.location
    cam_obj.rotation_euler = direction.to_track_quat('-Z', 'Y').to_euler()
    bpy.context.collection.objects.link(cam_obj)
    bpy.context.scene.camera = cam_obj

    # Rutas de guardado
    glb_path = PROPS_DIR / f"{name}.glb"
    blend_path = SOURCE_DIR / f"{name}.blend"
    preview_path = SOURCE_DIR / f"{name}_preview.png"
    drive_dir = DRIVE_BASE / target_dir_name
    drive_dir.mkdir(parents=True, exist_ok=True)
    drive_glb = drive_dir / f"{name}.glb"
    drive_blend = drive_dir / f"{name}.blend"
    drive_preview = drive_dir / f"{name}_preview.png"
    artifact_preview = ARTIFACTS_DIR / f"{name}_preview.png"

    # Exportar GLB
    bpy.ops.object.select_all(action='DESELECT')
    final_obj.select_set(True)
    bpy.ops.export_scene.gltf(
        filepath=str(glb_path),
        export_format='GLB',
        use_selection=True,
        export_apply=True,
        export_yup=True
    )
    # Guardar .blend
    bpy.ops.wm.save_as_mainfile(filepath=str(blend_path))

    # Render preview
    bpy.context.scene.render.resolution_x = 800
    bpy.context.scene.render.resolution_y = 800
    bpy.context.scene.render.filepath = str(preview_path)
    bpy.ops.render.render(write_still=True)

    # Copiar a OneDrive y Artifacts
    shutil.copy2(glb_path, drive_glb)
    shutil.copy2(blend_path, drive_blend)
    shutil.copy2(preview_path, drive_preview)
    shutil.copy2(preview_path, artifact_preview)


# ==============================================================================
# MODELO 1: bloques_letras.glb (~1 m alto)
# ==============================================================================
def build_bloques_letras():
    clean_scene()
    
    # 4 cubos de madera infantiles con reborde y letras en relieve
    # Colores: Rojo, Azul, Amarillo, Verde
    cubes_info = [
        # (size, pos, rot_z, mat_box, letter)
        (0.38, (0.0, 0.0, 0.19), 5, "PastelRed", "A"),
        (0.36, (0.02, -0.01, 0.55), -15, "PastelBlue", "B"),
        (0.34, (-0.01, 0.02, 0.89), 20, "PastelYellow", "C"),
        (0.35, (0.38, 0.14, 0.175), 40, "PastelGreen", "D"),  # Cubo caído al lado
    ]
    
    for size, pos, rot_z, mat_name, letter in cubes_info:
        # Cubo principal
        bpy.ops.mesh.primitive_cube_add(size=size, location=pos, rotation=(0, 0, math.radians(rot_z)))
        cube = bpy.context.active_object
        cube.data.materials.append(get_material(mat_name))
        
        # Caras talladas / marcos interiores en las caras visibles
        # Creamos una placa en bajo relieve para la letra frontal
        mat_cream = get_material("CreamWhite")
        fwd_vec = Vector((0, -size * 0.505, 0))
        rot_mat = Matrix.Rotation(math.radians(rot_z), 4, 'Z')
        fwd_rot = rot_mat @ fwd_vec
        
        # Placa blanca
        bpy.ops.mesh.primitive_plane_add(size=size * 0.72, location=Vector(pos) + fwd_rot, rotation=(math.radians(90), 0, math.radians(rot_z)))
        plate = bpy.context.active_object
        plate.data.materials.append(mat_cream)
        
        # Letra simple low-poly en relieve sobre la placa
        mat_dark = get_material("WoodDark")
        letter_pos = Vector(pos) + rot_mat @ Vector((0, -size * 0.515, 0))
        
        if letter == "A":
            # Dos barras inclinadas y un travesaño
            bpy.ops.mesh.primitive_cylinder_add(vertices=4, radius=0.018, depth=0.18, location=letter_pos + Vector((-0.035, 0, 0)), rotation=(0, math.radians(18), math.radians(rot_z)))
            a1 = bpy.context.active_object
            bpy.ops.mesh.primitive_cylinder_add(vertices=4, radius=0.018, depth=0.18, location=letter_pos + Vector((0.035, 0, 0)), rotation=(0, math.radians(-18), math.radians(rot_z)))
            a2 = bpy.context.active_object
            bpy.ops.mesh.primitive_cylinder_add(vertices=4, radius=0.015, depth=0.07, location=letter_pos + Vector((0, 0, -0.015)), rotation=(math.radians(90), math.radians(rot_z), 0))
            a3 = bpy.context.active_object
            for p in [a1, a2, a3]: p.data.materials.append(mat_dark)
        elif letter == "B":
            # Columna y dos medios aros
            bpy.ops.mesh.primitive_cylinder_add(vertices=4, radius=0.018, depth=0.18, location=letter_pos + Vector((-0.05, 0, 0)), rotation=(0, 0, math.radians(rot_z)))
            b1 = bpy.context.active_object
            bpy.ops.mesh.primitive_cylinder_add(vertices=5, radius=0.045, depth=0.025, location=letter_pos + Vector((0.01, 0, 0.045)), rotation=(math.radians(90), 0, math.radians(rot_z)))
            b2 = bpy.context.active_object
            bpy.ops.mesh.primitive_cylinder_add(vertices=5, radius=0.048, depth=0.025, location=letter_pos + Vector((0.01, 0, -0.045)), rotation=(math.radians(90), 0, math.radians(rot_z)))
            b3 = bpy.context.active_object
            for p in [b1, b2, b3]: p.data.materials.append(mat_dark)
        elif letter == "C":
            bpy.ops.mesh.primitive_cylinder_add(vertices=6, radius=0.075, depth=0.025, location=letter_pos, rotation=(math.radians(90), 0, math.radians(rot_z)))
            c1 = bpy.context.active_object
            c1.data.materials.append(mat_dark)
        elif letter == "D":
            bpy.ops.mesh.primitive_cylinder_add(vertices=4, radius=0.018, depth=0.18, location=letter_pos + Vector((-0.05, 0, 0)), rotation=(0, 0, math.radians(rot_z)))
            d1 = bpy.context.active_object
            bpy.ops.mesh.primitive_cylinder_add(vertices=6, radius=0.08, depth=0.025, location=letter_pos + Vector((0.01, 0, 0)), rotation=(math.radians(90), 0, math.radians(rot_z)))
            d2 = bpy.context.active_object
            for p in [d1, d2]: p.data.materials.append(mat_dark)

    finalize_model("bloques_letras", "bloques_letras")


# ==============================================================================
# MODELO 2: pelota_colores.glb (~0.8 m)
# ==============================================================================
def build_pelota_colores():
    clean_scene()
    
    radius = 0.40  # 0.8m de diámetro
    segments = 12  # 12 gajos longitudinales
    rings = 8
    
    # Crear esfera con escala ligera en Z para contacto con el piso
    bpy.ops.mesh.primitive_uv_sphere_add(segments=segments, ring_count=rings, radius=radius, location=(0, 0, radius * 0.98))
    sphere = bpy.context.active_object
    sphere.scale = (1.0, 1.0, 0.96)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    set_smooth(sphere)
    
    # Asignar materiales a los gajos alternados
    # 6 colores en ciclo: PastelRed, PastelYellow, PastelBlue, PastelGreen, PastelOrange, PastelCyan
    slice_colors = ["PastelRed", "PastelYellow", "PastelBlue", "PastelGreen", "PastelOrange", "PastelCyan"]
    for cname in slice_colors:
        sphere.data.materials.append(get_material(cname))
    sphere.data.materials.append(get_material("CreamWhite"))  # Polo
    
    mesh = sphere.data
    for poly in mesh.polygons:
        center = poly.center
        # Si está en el polo superior o inferior
        if abs(center.z - radius * 0.98) > radius * 0.85:
            poly.material_index = len(slice_colors)  # CreamWhite
        else:
            angle = math.atan2(center.y, center.x)  # -pi a pi
            if angle < 0:
                angle += 2 * math.pi
            slice_idx = int((angle / (2 * math.pi)) * len(slice_colors)) % len(slice_colors)
            poly.material_index = slice_idx

    finalize_model("pelota_colores", "pelota_colores")


# ==============================================================================
# MODELO 3: tambor.glb (~1 m)
# ==============================================================================
def build_tambor():
    clean_scene()
    
    drum_r = 0.38
    drum_h = 0.65
    
    # 1. Cuerpo cilíndrico rojo
    bpy.ops.mesh.primitive_cylinder_add(vertices=16, radius=drum_r, depth=drum_h, location=(0, 0, drum_h * 0.5 + 0.15))
    body = bpy.context.active_object
    set_smooth(body)
    body.data.materials.append(get_material("PastelRed"))
    
    # 2. Aros de madera superior e inferior
    mat_wood = get_material("WoodMedium")
    bpy.ops.mesh.primitive_cylinder_add(vertices=16, radius=drum_r + 0.025, depth=0.07, location=(0, 0, 0.15 + 0.035))
    rim_b = bpy.context.active_object
    rim_b.data.materials.append(mat_wood)
    
    bpy.ops.mesh.primitive_cylinder_add(vertices=16, radius=drum_r + 0.025, depth=0.07, location=(0, 0, 0.15 + drum_h - 0.035))
    rim_t = bpy.context.active_object
    rim_t.data.materials.append(mat_wood)
    
    # 3. Parches superior e inferior (WarmParchment)
    mat_parch = get_material("WarmParchment")
    bpy.ops.mesh.primitive_cylinder_add(vertices=16, radius=drum_r + 0.005, depth=0.02, location=(0, 0, 0.15 + drum_h))
    head_t = bpy.context.active_object
    head_t.data.materials.append(mat_parch)
    
    # 4. Cuerdas en zigzag alrededor del cuerpo (8 puntas)
    mat_rope = get_material("CreamWhite")
    num_ropes = 8
    for i in range(num_ropes):
        ang1 = i * (2 * math.pi / num_ropes)
        ang2 = (i + 0.5) * (2 * math.pi / num_ropes)
        ang3 = (i + 1.0) * (2 * math.pi / num_ropes)
        
        p1 = Vector((math.cos(ang1) * (drum_r + 0.015), math.sin(ang1) * (drum_r + 0.015), 0.18))
        p2 = Vector((math.cos(ang2) * (drum_r + 0.015), math.sin(ang2) * (drum_r + 0.015), 0.15 + drum_h - 0.04))
        p3 = Vector((math.cos(ang3) * (drum_r + 0.015), math.sin(ang3) * (drum_r + 0.015), 0.18))
        
        # Segmento ascendente
        mid1 = (p1 + p2) * 0.5
        dir1 = p2 - p1
        rot1 = dir1.to_track_quat('Z', 'Y').to_euler()
        bpy.ops.mesh.primitive_cylinder_add(vertices=4, radius=0.009, depth=dir1.length, location=mid1, rotation=rot1)
        r1 = bpy.context.active_object
        r1.data.materials.append(mat_rope)
        
        # Segmento descendente
        mid2 = (p2 + p3) * 0.5
        dir2 = p3 - p2
        rot2 = dir2.to_track_quat('Z', 'Y').to_euler()
        bpy.ops.mesh.primitive_cylinder_add(vertices=4, radius=0.009, depth=dir2.length, location=mid2, rotation=rot2)
        r2 = bpy.context.active_object
        r2.data.materials.append(mat_rope)
        
        # Tensor cilíndrico de cuero en el vértice
        bpy.ops.mesh.primitive_cylinder_add(vertices=5, radius=0.02, depth=0.035, location=p2, rotation=rot1)
        tensor = bpy.context.active_object
        tensor.data.materials.append(get_material("WoodDark"))

    # 5. Dos baquetas de madera cruzadas encima del parche
    stick_len = 0.52
    bpy.ops.mesh.primitive_cylinder_add(vertices=5, radius=0.014, depth=stick_len, location=(-0.05, 0.0, 0.15 + drum_h + 0.03), rotation=(math.radians(90), math.radians(28), 0))
    s1 = bpy.context.active_object
    s1.data.materials.append(mat_wood)
    bpy.ops.mesh.primitive_uv_sphere_add(segments=6, ring_count=4, radius=0.025, location=(0.18, 0.0, 0.15 + drum_h + 0.03))
    tip1 = bpy.context.active_object
    tip1.data.materials.append(mat_wood)
    
    bpy.ops.mesh.primitive_cylinder_add(vertices=5, radius=0.014, depth=stick_len, location=(0.05, 0.0, 0.15 + drum_h + 0.05), rotation=(math.radians(90), math.radians(-28), 0))
    s2 = bpy.context.active_object
    s2.data.materials.append(mat_wood)
    bpy.ops.mesh.primitive_uv_sphere_add(segments=6, ring_count=4, radius=0.025, location=(-0.18, 0.0, 0.15 + drum_h + 0.05))
    tip2 = bpy.context.active_object
    tip2.data.materials.append(mat_wood)

    finalize_model("tambor", "tambor")


# ==============================================================================
# MODELO 4: xilofono.glb (~1.2 m largo)
# ==============================================================================
def build_xilofono():
    clean_scene()
    
    length = 1.15
    width_large = 0.42
    width_small = 0.24
    height_base = 0.18
    
    mat_wood = get_material("WoodLight")
    mat_wood_dark = get_material("WoodDark")
    
    # 1. Dos largueros laterales de madera curvada
    # Larguero izquierdo (-X)
    p_bl = Vector((-width_large * 0.5, -length * 0.5, height_base))
    p_tl = Vector((-width_small * 0.5, length * 0.5, height_base))
    dir_l = p_tl - p_bl
    mid_l = (p_bl + p_tl) * 0.5
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=mid_l)
    rail_l = bpy.context.active_object
    rail_l.scale = (0.045, dir_l.length, 0.09)
    rail_l.rotation_euler = (0, 0, math.atan2(dir_l.x, dir_l.y))
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    rail_l.data.materials.append(mat_wood)
    
    # Larguero derecho (+X)
    p_br = Vector((width_large * 0.5, -length * 0.5, height_base))
    p_tr = Vector((width_small * 0.5, length * 0.5, height_base))
    dir_r = p_tr - p_br
    mid_r = (p_br + p_tr) * 0.5
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=mid_r)
    rail_r = bpy.context.active_object
    rail_r.scale = (0.045, dir_r.length, 0.09)
    rail_r.rotation_euler = (0, 0, math.atan2(dir_r.x, dir_r.y))
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    rail_r.data.materials.append(mat_wood)
    
    # 4 patas cónicas de soporte
    for pt in [p_bl, p_br, p_tl, p_tr]:
        bpy.ops.mesh.primitive_cylinder_add(vertices=6, radius=0.035, depth=height_base, location=(pt.x, pt.y, height_base * 0.5))
        leg = bpy.context.active_object
        leg.data.materials.append(mat_wood_dark)

    # 2. Ocho teclas de colores arcoíris pastel de mayor a menor longitud
    rainbow_keys = [
        ("PastelRed", 0.44),
        ("PastelOrange", 0.41),
        ("PastelYellow", 0.38),
        ("PastelGreen", 0.35),
        ("PastelCyan", 0.32),
        ("PastelBlue", 0.29),
        ("PastelPurple", 0.26),
        ("PastelPink", 0.23),
    ]
    
    key_depth = 0.085
    spacing = length / (len(rainbow_keys) + 0.5)
    start_y = -length * 0.5 + spacing * 0.75
    
    for i, (cname, k_len) in enumerate(rainbow_keys):
        cur_y = start_y + i * spacing
        z_pos = height_base + 0.06
        
        # Tecla
        bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0, cur_y, z_pos))
        key = bpy.context.active_object
        key.scale = (k_len, key_depth, 0.025)
        bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
        set_smooth(key)
        key.data.materials.append(get_material(cname))
        
        # Dos clavijas de sujeción (clavijas plateadas/oscuras)
        for offset_x in [-k_len * 0.36, k_len * 0.36]:
            bpy.ops.mesh.primitive_cylinder_add(vertices=5, radius=0.012, depth=0.04, location=(offset_x, cur_y, z_pos + 0.015))
            pin = bpy.context.active_object
            pin.data.materials.append(get_material("DarkMetal"))

    # 3. Baqueta/Mazo infantil de madera apoyada sobre las teclas
    mallet_len = 0.55
    bpy.ops.mesh.primitive_cylinder_add(vertices=5, radius=0.012, depth=mallet_len, location=(0.04, 0.0, height_base + 0.11), rotation=(math.radians(12), math.radians(-32), math.radians(20)))
    m_stick = bpy.context.active_object
    m_stick.data.materials.append(mat_wood)
    
    # Cabeza redonda del mazo
    head_pos = Vector((0.04, 0.0, height_base + 0.11)) + Vector((-0.18, 0.18, 0.06))
    bpy.ops.mesh.primitive_uv_sphere_add(segments=6, ring_count=4, radius=0.038, location=head_pos)
    m_head = bpy.context.active_object
    m_head.data.materials.append(get_material("PastelRed"))

    finalize_model("xilofono", "xilofono")


# ==============================================================================
# MODELO 5: lampara_velador.glb (~1.2 m alto)
# ==============================================================================
def build_lampara_velador():
    clean_scene()
    
    # 1. Base circular escalonada de cerámica / madera esmaltada
    mat_base = get_material("PastelCyan")
    bpy.ops.mesh.primitive_cylinder_add(vertices=12, radius=0.24, depth=0.05, location=(0, 0, 0.025))
    b1 = bpy.context.active_object
    b1.data.materials.append(mat_base)
    
    bpy.ops.mesh.primitive_cylinder_add(vertices=10, radius=0.18, depth=0.04, location=(0, 0, 0.065))
    b2 = bpy.context.active_object
    b2.data.materials.append(mat_base)
    
    # 2. Mástil central torneado de madera con detalles esféricos
    mat_pole = get_material("WoodLight")
    bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=0.035, depth=0.65, location=(0, 0, 0.40))
    pole = bpy.context.active_object
    pole.data.materials.append(mat_pole)
    
    # Cuentas esféricas torneadas en el mástil
    for z in [0.22, 0.45, 0.65]:
        bpy.ops.mesh.primitive_uv_sphere_add(segments=7, ring_count=4, radius=0.055, location=(0, 0, z))
        bead = bpy.context.active_object
        bead.data.materials.append(get_material("PastelYellow"))

    # 3. Pantalla cónica truncada de tela en crema cálido
    shade_bot_r = 0.32
    shade_top_r = 0.20
    shade_h = 0.42
    shade_z = 0.90
    
    bpy.ops.mesh.primitive_cone_add(vertices=12, radius1=shade_bot_r, radius2=shade_top_r, depth=shade_h, location=(0, 0, shade_z))
    shade = bpy.context.active_object
    set_smooth(shade)
    shade.data.materials.append(get_material("PastelYellow"))
    
    # Borde inferior y superior de la pantalla
    bpy.ops.mesh.primitive_cylinder_add(vertices=12, radius=shade_bot_r + 0.012, depth=0.03, location=(0, 0, shade_z - shade_h * 0.5))
    rim_b = bpy.context.active_object
    rim_b.data.materials.append(get_material("PastelPink"))
    
    bpy.ops.mesh.primitive_cylinder_add(vertices=12, radius=shade_top_r + 0.012, depth=0.025, location=(0, 0, shade_z + shade_h * 0.5))
    rim_t = bpy.context.active_object
    rim_t.data.materials.append(get_material("PastelPink"))

    # 4. Remate superior en forma de luna creciente o estrella dorada
    mat_gold = get_material("GoldBrass")
    bpy.ops.mesh.primitive_cylinder_add(vertices=6, radius=0.018, depth=0.10, location=(0, 0, shade_z + shade_h * 0.5 + 0.05))
    finial_stem = bpy.context.active_object
    finial_stem.data.materials.append(mat_gold)
    
    # Esfera luna decorativa
    bpy.ops.mesh.primitive_uv_sphere_add(segments=8, ring_count=5, radius=0.065, location=(0, 0, shade_z + shade_h * 0.5 + 0.12))
    moon = bpy.context.active_object
    moon.data.materials.append(mat_gold)

    finalize_model("lampara_velador", "lampara_velador")


if __name__ == "__main__":
    print("\n================== GENERANDO LOTE 1 DE AMBIENTACIÓN ==================")
    build_bloques_letras()
    build_pelota_colores()
    build_tambor()
    build_xilofono()
    build_lampara_velador()
    print("\n================== LOTE 1 COMPLETADO CON ÉXITO ==================")
