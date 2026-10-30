"""Generador de Ambientación - Lote 2 (The Cradle Props) para LULLABY:
Modelos incluidos:
1. baul_juguetes.glb (~1 m alto) - Baúl de juguetes infantil de madera con tapa abovedada semiabierta y herrajes
2. tren_juguete.glb (~2 m largo) - Tren de juguete de madera con locomotora y dos vagones
3. castillo_bloques.glb (~1.5 m alto) - Castillo armado con bloques geométricos, torres y almenas
4. estanteria_gigante.glb (~3 m alto) - Estantería infantil para pared con juguetes y libros en baldas
5. cortina_ventana.glb (~3 m alto) - Ventana infantil de noche con marco, luna, estrellas y cortinas recogidas

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
    # Neutros y Noche
    "CreamWhite": (0.92, 0.89, 0.82),
    "WarmParchment": (0.86, 0.80, 0.70),
    "NightBlue": (0.12, 0.14, 0.26),
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

    # Calcular Z mínimo en mundo
    all_min_z = min(min([(o.matrix_world @ v.co).z for v in o.data.vertices]) for o in mesh_objs)
    for o in mesh_objs:
        o.location.z -= all_min_z
        bpy.context.view_layer.objects.active = o
        o.select_set(True)
        bpy.ops.object.transform_apply(location=True, rotation=False, scale=False)

    # Unir en un prop único
    bpy.ops.object.select_all(action='DESELECT')
    for o in mesh_objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = mesh_objs[0]
    if len(mesh_objs) > 1:
        bpy.ops.object.join()
    final_obj = bpy.context.active_object
    final_obj.name = name

    # Conteo de triángulos y dimensiones
    total_tris = sum(len(p.vertices) - 2 for p in final_obj.data.polygons)
    bbox = final_obj.bound_box
    height = max(b[2] for b in bbox) - min(b[2] for b in bbox)
    width_x = max(b[0] for b in bbox) - min(b[0] for b in bbox)
    depth_y = max(b[1] for b in bbox) - min(b[1] for b in bbox)
    print(f"\n>>> MODELO: {name}")
    print(f"    Triángulos: {total_tris} (Presupuesto <= 800: {'OK' if total_tris <= 800 else 'EXCEDIDO'})")
    print(f"    Dimensiones: Ancho {width_x:.2f}m x Prof {depth_y:.2f}m x Alto {height:.2f}m")

    # Iluminación
    center_z = height * 0.5
    cam_dist = max(height, width_x, depth_y) * 2.2
    
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

    # Cámara Iso 3/4
    cam = bpy.data.cameras.new("PreviewCam")
    cam.lens = 50
    cam_obj = bpy.data.objects.new("PreviewCam", cam)
    cam_obj.location = (cam_dist * 0.75, -cam_dist * 0.75, center_z + cam_dist * 0.40)
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

    # Respaldos
    shutil.copy2(glb_path, drive_glb)
    shutil.copy2(blend_path, drive_blend)
    shutil.copy2(preview_path, drive_preview)
    shutil.copy2(preview_path, artifact_preview)


def create_curved_lid(width, depth, arch_height, mat_wood, mat_gold):
    bpy.ops.mesh.primitive_cylinder_add(
        vertices=10, radius=depth * 0.5, depth=width, location=(0, 0, 0), rotation=(0, math.radians(90), 0)
    )
    cyl = bpy.context.active_object
    cyl.scale = (1.0, 1.0, arch_height / (depth * 0.5))
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    
    bpy.ops.object.mode_set(mode='EDIT')
    import bmesh
    bm = bmesh.from_edit_mesh(cyl.data)
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if v.co.z < -0.001], context='VERTS')
    bottom_verts = [v for v in bm.verts if abs(v.co.z) < 0.01]
    if len(bottom_verts) >= 4:
        bmesh.ops.edgeloop_fill(bm, edges=[e for e in bm.edges if e.verts[0] in bottom_verts and e.verts[1] in bottom_verts])
    bmesh.update_edit_mesh(cyl.data)
    bpy.ops.object.mode_set(mode='OBJECT')
    
    set_smooth(cyl)
    cyl.data.materials.append(mat_wood)
    return cyl


# ==============================================================================
# MODELO 6: baul_juguetes.glb (~1 m alto)
# ==============================================================================
def build_baul_juguetes():
    clean_scene()
    
    bw = 1.30  # ancho
    bd = 0.75  # profundidad
    bh = 0.55  # altura del cajón base
    lid_arch = 0.26
    
    mat_wood = get_material("WoodMedium")
    mat_trim = get_material("WoodDark")
    mat_gold = get_material("GoldBrass")
    
    # 1. Cajón principal del baúl
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0, 0, bh * 0.5 + 0.06))
    box = bpy.context.active_object
    box.scale = (bw, bd, bh)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    box.data.materials.append(mat_wood)
    
    for sx in [-1, 1]:
        for sy in [-1, 1]:
            bpy.ops.mesh.primitive_cylinder_add(
                vertices=6, radius=0.065, depth=0.10,
                location=(sx * (bw * 0.44), sy * (bd * 0.42), 0.05)
            )
            leg = bpy.context.active_object
            leg.data.materials.append(mat_trim)

    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0, 0, 0.08))
    trim_bot = bpy.context.active_object
    trim_bot.scale = (bw + 0.04, bd + 0.04, 0.06)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    trim_bot.data.materials.append(mat_trim)

    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0, 0, bh + 0.04))
    trim_top = bpy.context.active_object
    trim_top.scale = (bw + 0.04, bd + 0.04, 0.05)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    trim_top.data.materials.append(mat_trim)

    for sx in [-1, 1]:
        for sy in [-1, 1]:
            bpy.ops.mesh.primitive_cube_add(size=1.0, location=(sx * (bw * 0.49), sy * (bd * 0.49), bh * 0.5 + 0.06))
            corner = bpy.context.active_object
            corner.scale = (0.05, 0.05, bh - 0.02)
            bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
            corner.data.materials.append(mat_trim)

    for sx in [-bw * 0.32, bw * 0.32]:
        bpy.ops.mesh.primitive_cube_add(size=1.0, location=(sx, bd * 0.5 + 0.012, bh * 0.5 + 0.06))
        strap = bpy.context.active_object
        strap.scale = (0.04, 0.015, bh - 0.04)
        bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
        strap.data.materials.append(mat_gold)

    # 2. Tapa abovedada curvada
    lid = create_curved_lid(bw + 0.02, bd + 0.02, lid_arch, mat_wood, mat_gold)
    hinge_z = bh + 0.065
    lid.location = (0, 0, hinge_z)
    lid.rotation_euler = (math.radians(-12), 0, 0)

    for sx in [-bw * 0.32, 0.0, bw * 0.32]:
        bpy.ops.mesh.primitive_cylinder_add(
            vertices=10, radius=bd * 0.5 + 0.015, depth=0.035,
            location=(sx, 0, 0), rotation=(0, math.radians(90), 0)
        )
        l_strap = bpy.context.active_object
        l_strap.scale = (1.0, 1.0, (lid_arch + 0.01) / (bd * 0.5))
        bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
        
        bpy.ops.object.mode_set(mode='EDIT')
        import bmesh
        bm = bmesh.from_edit_mesh(l_strap.data)
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if v.co.z < -0.001], context='VERTS')
        bmesh.update_edit_mesh(l_strap.data)
        bpy.ops.object.mode_set(mode='OBJECT')
        
        set_smooth(l_strap)
        l_strap.data.materials.append(mat_gold)
        l_strap.location = (sx, 0, hinge_z)
        l_strap.rotation_euler = (math.radians(-12), 0, 0)

    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0, bd * 0.5 + 0.025, bh * 0.76))
    lock_plate = bpy.context.active_object
    lock_plate.scale = (0.13, 0.02, 0.15)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    lock_plate.data.materials.append(mat_gold)
    
    bpy.ops.mesh.primitive_cylinder_add(vertices=6, radius=0.025, depth=0.03, location=(0, bd * 0.5 + 0.035, bh * 0.74))
    keyhole = bpy.context.active_object
    keyhole.data.materials.append(get_material("DarkMetal"))

    for sx in [-1, 1]:
        bpy.ops.mesh.primitive_cube_add(size=1.0, location=(sx * (bw * 0.5 + 0.015), 0, bh * 0.65))
        h_plate = bpy.context.active_object
        h_plate.scale = (0.015, 0.14, 0.08)
        bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
        h_plate.data.materials.append(mat_gold)
        bpy.ops.mesh.primitive_cylinder_add(vertices=6, radius=0.06, depth=0.018, location=(sx * (bw * 0.5 + 0.035), 0, bh * 0.62), rotation=(0, math.radians(90), 0))
        h_ring = bpy.context.active_object
        h_ring.data.materials.append(mat_gold)

    bpy.ops.mesh.primitive_uv_sphere_add(segments=8, ring_count=5, radius=0.10, location=(-0.30, bd * 0.32, bh + 0.04))
    toy_ball = bpy.context.active_object
    set_smooth(toy_ball)
    toy_ball.data.materials.append(get_material("PastelCyan"))

    finalize_model("baul_juguetes", "baul_juguetes")


# ==============================================================================
# MODELO 7: tren_juguete.glb (~2 m largo)
# ==============================================================================
def build_tren_juguete():
    clean_scene()
    
    mat_wood = get_material("WoodLight")
    mat_wheel = get_material("WoodDark")
    mat_red = get_material("PastelRed")
    mat_blue = get_material("PastelBlue")
    mat_green = get_material("PastelGreen")
    mat_yellow = get_material("PastelYellow")
    mat_gold = get_material("GoldBrass")
    
    def add_wheel_axle(x_pos, y_pos, z_pos, r=0.10, w=0.04):
        for sx in [-1, 1]:
            bpy.ops.mesh.primitive_cylinder_add(vertices=6, radius=r, depth=w, location=(sx * (x_pos), y_pos, z_pos), rotation=(0, math.radians(90), 0))
            wh = bpy.context.active_object
            wh.data.materials.append(mat_wheel)
            # Tapacubos amarillo
            bpy.ops.mesh.primitive_cylinder_add(vertices=5, radius=0.03, depth=w + 0.015, location=(sx * (x_pos), y_pos, z_pos), rotation=(0, math.radians(90), 0))
            hub = bpy.context.active_object
            hub.data.materials.append(mat_yellow)

    # 1. Locomotora (al frente: +Y)
    loco_y = 0.55
    chassis_z = 0.12
    # Chasis de madera
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0, loco_y, chassis_z))
    c1 = bpy.context.active_object
    c1.scale = (0.36, 0.75, 0.06)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    c1.data.materials.append(mat_wood)
    
    # Caldera cilíndrica roja
    bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=0.16, depth=0.45, location=(0, loco_y + 0.12, chassis_z + 0.18), rotation=(math.radians(90), 0, 0))
    boiler = bpy.context.active_object
    boiler.data.materials.append(mat_red)
    
    # Faro delantero
    bpy.ops.mesh.primitive_cylinder_add(vertices=6, radius=0.06, depth=0.06, location=(0, loco_y + 0.36, chassis_z + 0.18), rotation=(math.radians(90), 0, 0))
    light = bpy.context.active_object
    light.data.materials.append(mat_yellow)
    
    # Chimenea cónica
    bpy.ops.mesh.primitive_cone_add(vertices=6, radius1=0.04, radius2=0.065, depth=0.18, location=(0, loco_y + 0.22, chassis_z + 0.40))
    funnel = bpy.context.active_object
    funnel.data.materials.append(mat_gold)
    
    # Cabina de conducción azul con tejado curvado
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0, loco_y - 0.22, chassis_z + 0.24))
    cab = bpy.context.active_object
    cab.scale = (0.34, 0.28, 0.38)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    cab.data.materials.append(mat_blue)
    
    # Techo de cabina curvado
    bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=0.20, depth=0.32, location=(0, loco_y - 0.22, chassis_z + 0.44), rotation=(math.radians(90), 0, 0))
    roof = bpy.context.active_object
    roof.scale = (1.0, 1.0, 0.35)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    roof.data.materials.append(mat_red)

    add_wheel_axle(0.19, loco_y + 0.20, chassis_z - 0.02)
    add_wheel_axle(0.19, loco_y - 0.20, chassis_z - 0.02)

    # 2. Vagón 1: Carga abierta verde con bloques geométricos
    vag1_y = -0.15
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0, vag1_y, chassis_z))
    c2 = bpy.context.active_object
    c2.scale = (0.34, 0.55, 0.06)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    c2.data.materials.append(mat_wood)
    
    # Caja del vagón
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0, vag1_y, chassis_z + 0.14))
    box_v1 = bpy.context.active_object
    box_v1.scale = (0.32, 0.52, 0.22)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    box_v1.data.materials.append(mat_green)
    
    # Carga de bloques de juguete
    bpy.ops.mesh.primitive_cube_add(size=0.14, location=(-0.05, vag1_y + 0.10, chassis_z + 0.26))
    bl1 = bpy.context.active_object
    bl1.data.materials.append(mat_yellow)
    bpy.ops.mesh.primitive_cylinder_add(vertices=6, radius=0.07, depth=0.14, location=(0.06, vag1_y - 0.08, chassis_z + 0.26))
    bl2 = bpy.context.active_object
    bl2.data.materials.append(mat_red)

    add_wheel_axle(0.18, vag1_y + 0.16, chassis_z - 0.02)
    add_wheel_axle(0.18, vag1_y - 0.16, chassis_z - 0.02)

    # 3. Vagón 2: Caboose amarillo con ventanas
    vag2_y = -0.78
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0, vag2_y, chassis_z))
    c3 = bpy.context.active_object
    c3.scale = (0.34, 0.55, 0.06)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    c3.data.materials.append(mat_wood)
    
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0, vag2_y, chassis_z + 0.18))
    box_v2 = bpy.context.active_object
    box_v2.scale = (0.32, 0.50, 0.30)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    box_v2.data.materials.append(mat_yellow)
    
    # Techo caboose
    bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=0.18, depth=0.54, location=(0, vag2_y, chassis_z + 0.34), rotation=(math.radians(90), 0, 0))
    roof2 = bpy.context.active_object
    roof2.scale = (1.0, 1.0, 0.35)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    roof2.data.materials.append(mat_red)

    add_wheel_axle(0.18, vag2_y + 0.16, chassis_z - 0.02)
    add_wheel_axle(0.18, vag2_y - 0.16, chassis_z - 0.02)

    # Barras de enganche entre vagones
    for y_link in [(loco_y - 0.38 + vag1_y + 0.28) * 0.5, (vag1_y - 0.28 + vag2_y + 0.28) * 0.5]:
        bpy.ops.mesh.primitive_cylinder_add(vertices=4, radius=0.015, depth=0.14, location=(0, y_link, chassis_z), rotation=(math.radians(90), 0, 0))
        link = bpy.context.active_object
        link.data.materials.append(mat_gold)

    finalize_model("tren_juguete", "tren_juguete")


# ==============================================================================
# MODELO 8: castillo_bloques.glb (~1.5 m alto)
# ==============================================================================
def build_castillo_bloques():
    clean_scene()
    
    mat_wood = get_material("WoodLight")
    mat_blue = get_material("PastelBlue")
    mat_red = get_material("PastelRed")
    mat_yellow = get_material("PastelYellow")
    mat_green = get_material("PastelGreen")
    
    base_h = 0.55
    
    # 1. Muro central y portal
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0, 0, base_h * 0.5))
    wall = bpy.context.active_object
    wall.scale = (0.75, 0.30, base_h)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    wall.data.materials.append(mat_wood)
    
    # Almenas en el muro central
    for x in [-0.26, 0.0, 0.26]:
        bpy.ops.mesh.primitive_cube_add(size=1.0, location=(x, 0, base_h + 0.06))
        merlon = bpy.context.active_object
        merlon.scale = (0.12, 0.30, 0.12)
        bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
        merlon.data.materials.append(mat_wood)

    # Puerta en arco rebajada en el muro
    bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=0.16, depth=0.34, location=(0, 0, 0.22), rotation=(math.radians(90), 0, 0))
    door = bpy.context.active_object
    door.data.materials.append(mat_red)

    # 2. Dos torres laterales cilíndricas
    t_radius = 0.22
    t_height = 0.95
    for sx, cone_mat in [(-1, mat_red), (1, mat_blue)]:
        x_pos = sx * 0.55
        # Columna de la torre
        bpy.ops.mesh.primitive_cylinder_add(vertices=10, radius=t_radius, depth=t_height, location=(x_pos, 0, t_height * 0.5))
        tower = bpy.context.active_object
        tower.data.materials.append(mat_wood)
        
        # Anillo superior de la torre
        bpy.ops.mesh.primitive_cylinder_add(vertices=10, radius=t_radius + 0.04, depth=0.08, location=(x_pos, 0, t_height + 0.04))
        rim = bpy.context.active_object
        rim.data.materials.append(mat_yellow)
        
        # Tejado cónico puntiagudo
        bpy.ops.mesh.primitive_cone_add(vertices=10, radius1=t_radius + 0.05, radius2=0.0, depth=0.38, location=(x_pos, 0, t_height + 0.27))
        roof = bpy.context.active_object
        roof.data.materials.append(cone_mat)
        
        # Banderín en la cúspide
        bpy.ops.mesh.primitive_cylinder_add(vertices=4, radius=0.008, depth=0.15, location=(x_pos, 0, t_height + 0.52))
        flag_pole = bpy.context.active_object
        flag_pole.data.materials.append(mat_yellow)

    # 3. Torre central del homenaje (más alta)
    ch_h = 1.05
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0, -0.05, base_h + ch_h * 0.5 - 0.15))
    center_t = bpy.context.active_object
    center_t.scale = (0.42, 0.35, ch_h)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    center_t.data.materials.append(mat_wood)
    
    # Tejado piramidal amarillo en la torre central
    bpy.ops.mesh.primitive_cone_add(vertices=4, radius1=0.34, radius2=0.0, depth=0.45, location=(0, -0.05, base_h + ch_h - 0.15 + 0.225), rotation=(0, 0, math.radians(45)))
    main_roof = bpy.context.active_object
    main_roof.data.materials.append(mat_yellow)
    
    # Remate esférico dorado en el castillo
    bpy.ops.mesh.primitive_uv_sphere_add(segments=6, ring_count=4, radius=0.06, location=(0, -0.05, base_h + ch_h - 0.15 + 0.48))
    finial = bpy.context.active_object
    finial.data.materials.append(get_material("GoldBrass"))

    finalize_model("castillo_bloques", "castillo_bloques")


# ==============================================================================
# MODELO 9: estanteria_gigante.glb (~3 m alto, max 0.3m relieve de pared)
# ==============================================================================
def build_estanteria_gigante():
    clean_scene()
    
    sh_w = 1.80  # ancho
    sh_d = 0.28  # profundidad máxima <= 0.30m según medidas reales de Alejo
    sh_h = 2.95  # altura
    
    mat_wood = get_material("WoodLight")
    mat_wood_dark = get_material("WoodMedium")
    mat_red = get_material("PastelRed")
    mat_blue = get_material("PastelBlue")
    mat_green = get_material("PastelGreen")
    mat_yellow = get_material("PastelYellow")
    
    # 1. Montantes laterales de madera
    t_thick = 0.05
    for sx in [-1, 1]:
        bpy.ops.mesh.primitive_cube_add(size=1.0, location=(sx * (sh_w * 0.5 - t_thick * 0.5), 0, sh_h * 0.5))
        side = bpy.context.active_object
        side.scale = (t_thick, sh_d, sh_h)
        bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
        side.data.materials.append(mat_wood)

    # Fondo de la estantería (plano contra la pared en -Y)
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0, -sh_d * 0.5 + 0.01, sh_h * 0.5))
    back = bpy.context.active_object
    back.scale = (sh_w - t_thick * 2, 0.02, sh_h)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    back.data.materials.append(mat_wood_dark)

    # 4 Baldas horizontales
    shelf_z_list = [0.12, 0.85, 1.55, 2.25, sh_h - 0.04]
    for z in shelf_z_list:
        bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0, 0, z))
        shelf = bpy.context.active_object
        shelf.scale = (sh_w - t_thick * 1.9, sh_d - 0.01, 0.04)
        bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
        shelf.data.materials.append(mat_wood)

    # Cornisa decorativa
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0, 0, sh_h + 0.05))
    crown = bpy.context.active_object
    crown.scale = (sh_w + 0.04, sh_d + 0.02, 0.10)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    crown.data.materials.append(mat_wood_dark)

    # 2. Objetos sencillos en las baldas adaptados al fondo de 0.28m
    # Balda 1 (z = 0.85): libros grandes apoyados
    for bx, col, rot_y in [(-0.45, mat_red, 0), (-0.36, mat_blue, 0), (-0.24, mat_yellow, -18)]:
        bpy.ops.mesh.primitive_cube_add(size=1.0, location=(bx, 0.02, 0.85 + 0.20))
        book = bpy.context.active_object
        book.scale = (0.06, 0.18, 0.36)
        book.rotation_euler = (0, math.radians(rot_y), 0)
        bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
        book.data.materials.append(col)
        
    # Balda 1: patito de juguete
    bpy.ops.mesh.primitive_uv_sphere_add(segments=6, ring_count=4, radius=0.10, location=(0.40, 0.03, 0.85 + 0.12))
    duck_b = bpy.context.active_object
    duck_b.data.materials.append(mat_yellow)
    bpy.ops.mesh.primitive_uv_sphere_add(segments=5, ring_count=3, radius=0.06, location=(0.40, 0.08, 0.85 + 0.22))
    duck_h = bpy.context.active_object
    duck_h.data.materials.append(mat_yellow)
    bpy.ops.mesh.primitive_cone_add(vertices=5, radius1=0.025, radius2=0.0, depth=0.06, location=(0.40, 0.14, 0.85 + 0.21), rotation=(math.radians(90), 0, 0))
    duck_beak = bpy.context.active_object
    duck_beak.data.materials.append(get_material("PastelOrange"))

    # Balda 2 (z = 1.55): cajas de juguetes / bloques apilados
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(-0.35, 0.01, 1.55 + 0.14))
    box1 = bpy.context.active_object
    box1.scale = (0.24, 0.18, 0.24)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    box1.data.materials.append(mat_green)
    
    bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=0.10, depth=0.24, location=(0.32, 0.01, 1.55 + 0.14))
    jar = bpy.context.active_object
    jar.data.materials.append(mat_blue)

    finalize_model("estanteria_gigante", "estanteria_gigante")


# ==============================================================================
# MODELO 10: cortina_ventana.glb (~3 m alto, max 0.3m relieve de pared)
# ==============================================================================
def build_cortina_ventana():
    clean_scene()
    
    win_w = 1.85
    win_h = 2.40
    frame_d = 0.06
    base_z = 0.35
    
    mat_frame = get_material("CreamWhite")
    mat_night = get_material("NightBlue")
    mat_curtain = get_material("PastelBlue")
    mat_tie = get_material("PastelPink")
    mat_gold = get_material("GoldBrass")
    
    # 1. Marco exterior de madera de la ventana (fondo plano contra pared)
    # Alféizar inferior
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0, 0, base_z))
    sill = bpy.context.active_object
    sill.scale = (win_w + 0.25, frame_d + 0.06, 0.07)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    sill.data.materials.append(mat_frame)
    
    # Marco rectangular
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0, -0.01, base_z + win_h * 0.5))
    frame_bg = bpy.context.active_object
    frame_bg.scale = (win_w, frame_d, win_h)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    frame_bg.data.materials.append(mat_frame)

    # Fondo de noche estrellada
    bpy.ops.mesh.primitive_plane_add(size=1.0, location=(0, 0.015, base_z + win_h * 0.5), rotation=(math.radians(90), 0, 0))
    sky = bpy.context.active_object
    sky.scale = (win_w - 0.18, win_h - 0.18, 1.0)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    sky.data.materials.append(mat_night)

    # Luna creciente en relieve amarillo dorado
    bpy.ops.mesh.primitive_cylinder_add(vertices=12, radius=0.25, depth=0.02, location=(0.32, 0.025, base_z + win_h * 0.68), rotation=(math.radians(90), 0, 0))
    moon = bpy.context.active_object
    moon.data.materials.append(mat_gold)
    
    # Sombra que recorta la luna
    bpy.ops.mesh.primitive_cylinder_add(vertices=10, radius=0.22, depth=0.025, location=(0.39, 0.027, base_z + win_h * 0.72), rotation=(math.radians(90), 0, 0))
    moon_cut = bpy.context.active_object
    moon_cut.data.materials.append(mat_night)

    # Estrellas en relieve
    for sp in [(-0.35, base_z + win_h * 0.75), (-0.15, base_z + win_h * 0.55)]:
        bpy.ops.mesh.primitive_cylinder_add(vertices=4, radius=0.06, depth=0.02, location=(sp[0], 0.025, sp[1]), rotation=(math.radians(90), 0, math.radians(45)))
        star = bpy.context.active_object
        star.data.materials.append(mat_gold)

    # Cruz de montantes de la ventana
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0, 0.02, base_z + win_h * 0.5))
    m_v = bpy.context.active_object
    m_v.scale = (0.04, 0.02, win_h - 0.15)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    m_v.data.materials.append(mat_frame)
    
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0, 0.02, base_z + win_h * 0.5))
    m_h = bpy.context.active_object
    m_h.scale = (win_w - 0.15, 0.02, 0.04)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    m_h.data.materials.append(mat_frame)

    # 2. Barra de cortina superior con remates dorados
    bar_z = base_z + win_h + 0.08
    bar_len = win_w + 0.45
    bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=0.02, depth=bar_len, location=(0, 0.07, bar_z), rotation=(0, math.radians(90), 0))
    rod = bpy.context.active_object
    rod.data.materials.append(mat_gold)
    
    for sx in [-1, 1]:
        bpy.ops.mesh.primitive_uv_sphere_add(segments=6, ring_count=4, radius=0.045, location=(sx * (bar_len * 0.5), 0.07, bar_z))
        fin = bpy.context.active_object
        fin.data.materials.append(mat_gold)

    # 3. Dos cortinas laterales recogidas con lazo (profundidad total <= 0.24m)
    for sx in [-1, 1]:
        cx = sx * (win_w * 0.44)
        bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=0.14, depth=win_h * 0.55, location=(cx, 0.08, bar_z - win_h * 0.28))
        curt_t = bpy.context.active_object
        curt_t.scale = (0.75, 0.35, 1.0)
        bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
        set_smooth(curt_t)
        curt_t.data.materials.append(mat_curtain)
        
        tie_z = base_z + win_h * 0.42
        bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=0.11, depth=0.05, location=(cx, 0.08, tie_z))
        tie = bpy.context.active_object
        tie.scale = (0.72, 0.38, 1.0)
        bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
        tie.data.materials.append(mat_tie)
        
        bpy.ops.mesh.primitive_cone_add(vertices=8, radius1=0.18, radius2=0.09, depth=win_h * 0.42, location=(cx, 0.08, base_z + win_h * 0.20))
        curt_b = bpy.context.active_object
        curt_b.scale = (0.80, 0.38, 1.0)
        bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
        set_smooth(curt_b)
        curt_b.data.materials.append(mat_curtain)

    finalize_model("cortina_ventana", "cortina_ventana")


if __name__ == "__main__":
    print("\n================== GENERANDO LOTE 2 DE AMBIENTACIÓN ==================")
    build_baul_juguetes()
    build_tren_juguete()
    build_castillo_bloques()
    build_estanteria_gigante()
    build_cortina_ventana()
    print("\n================== LOTE 2 COMPLETADO CON ÉXITO ==================")
