"""Rediseño limpio y sólido de baul_juguetes.glb para LULLABY:
Construcción geométrica matemática limpia:
- Cajón rectangular con zócalos y patas.
- Tapa abovedada generada por perfil de arco extruido estanco (watertight).
- Flejes dorados extruidos sobre el perfil de la tapa.
- Cerradura frontal con placa de latón y bocallave.
- Dos asas laterales de metal.
- Bloques de juguete infantiles en el suelo al lado del baúl.
- Presupuesto <= 800 tris.
- Origen en base Z = 0.
"""

import math
import shutil
from pathlib import Path
import bpy
import bmesh
from mathutils import Vector, Matrix

LULLABY_ROOT = Path(r"C:\LULLABY")
PROPS_DIR = LULLABY_ROOT / "assets" / "models" / "props"
SOURCE_DIR = LULLABY_ROOT / "assets" / "source" / "blender"
DRIVE_DIR = Path(r"C:\Users\davin\OneDrive\Documentos\MODELOS JUEGO\baul_juguetes")
ARTIFACTS_DIR = Path(r"C:\Users\davin\.gemini\antigravity\brain\61207137-232d-4414-8860-f3fa45906692")

PROPS_DIR.mkdir(parents=True, exist_ok=True)
SOURCE_DIR.mkdir(parents=True, exist_ok=True)
DRIVE_DIR.mkdir(parents=True, exist_ok=True)

bpy.context.preferences.filepaths.save_version = 0

PALETTE = {
    "WoodLight": (0.84, 0.72, 0.54),
    "WoodMedium": (0.65, 0.48, 0.32),
    "WoodDark": (0.38, 0.26, 0.18),
    "GoldBrass": (0.78, 0.64, 0.28),
    "PastelRed": (0.82, 0.32, 0.32),
    "PastelCyan": (0.35, 0.70, 0.75),
    "PastelYellow": (0.92, 0.82, 0.38),
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


def make_watertight_arch_mesh(name, width, depth, arch_height, num_segments=8, material=None):
    """Crea una malla estanca abovedada sin errores booleanos ni vértices sueltos."""
    mesh = bpy.data.meshes.new(name)
    bm = bmesh.new()

    # Puntos del arco en el plano Y-Z
    arc_points = []
    for i in range(num_segments + 1):
        theta = i * math.pi / num_segments
        y = -(depth * 0.5) * math.cos(theta)
        z = arch_height * math.sin(theta)
        arc_points.append((y, z))

    left_verts = []
    right_verts = []
    x_half = width * 0.5

    for y, z in arc_points:
        v_l = bm.verts.new((-x_half, y, z))
        v_r = bm.verts.new((x_half, y, z))
        left_verts.append(v_l)
        right_verts.append(v_r)

    bm.verts.ensure_lookup_table()

    # Caras del arco curvado
    for i in range(num_segments):
        bm.faces.new((left_verts[i], right_verts[i], right_verts[i + 1], left_verts[i + 1]))

    # Cara inferior plana (base de la tapa)
    bm.faces.new((left_verts[num_segments], right_verts[num_segments], right_verts[0], left_verts[0]))

    # Tapas laterales (planas)
    bm.faces.new(list(reversed(left_verts)))
    bm.faces.new(right_verts)

    bm.to_mesh(mesh)
    bm.free()

    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    set_smooth(obj)
    if material:
        obj.data.materials.append(material)
    return obj


def build_baul():
    clean_scene()
    
    bw = 1.30  # ancho
    bd = 0.75  # profundidad
    bh = 0.52  # altura del cajón base
    arch_h = 0.28  # altura del arco de la tapa
    
    mat_wood = get_material("WoodMedium")
    mat_trim = get_material("WoodDark")
    mat_gold = get_material("GoldBrass")
    
    # 1. Cajón principal del baúl
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0, 0, bh * 0.5 + 0.08))
    box = bpy.context.active_object
    box.scale = (bw, bd, bh)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    box.data.materials.append(mat_wood)
    
    # 4 Patas de soporte en las esquinas
    for sx in [-1, 1]:
        for sy in [-1, 1]:
            bpy.ops.mesh.primitive_cylinder_add(
                vertices=6, radius=0.065, depth=0.12,
                location=(sx * (bw * 0.44), sy * (bd * 0.42), 0.06)
            )
            leg = bpy.context.active_object
            leg.data.materials.append(mat_trim)

    # Zócalo inferior
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0, 0, 0.09))
    trim_bot = bpy.context.active_object
    trim_bot.scale = (bw + 0.04, bd + 0.04, 0.06)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    trim_bot.data.materials.append(mat_trim)

    # Zócalo superior del cajón
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0, 0, bh + 0.06))
    trim_top = bpy.context.active_object
    trim_top.scale = (bw + 0.04, bd + 0.04, 0.05)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    trim_top.data.materials.append(mat_trim)

    # Esquineros de refuerzo verticales en las 4 esquinas
    for sx in [-1, 1]:
        for sy in [-1, 1]:
            bpy.ops.mesh.primitive_cube_add(size=1.0, location=(sx * (bw * 0.49), sy * (bd * 0.49), bh * 0.5 + 0.08))
            corner = bpy.context.active_object
            corner.scale = (0.05, 0.05, bh - 0.02)
            bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
            corner.data.materials.append(mat_trim)

    # Flejes dorados verticales en el frente del cajón
    for sx in [-bw * 0.32, bw * 0.32]:
        bpy.ops.mesh.primitive_cube_add(size=1.0, location=(sx, bd * 0.5 + 0.012, bh * 0.5 + 0.08))
        strap = bpy.context.active_object
        strap.scale = (0.045, 0.015, bh - 0.04)
        bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
        strap.data.materials.append(mat_gold)

    # 2. Tapa abovedada curvada estanca y sólida
    lid = make_watertight_arch_mesh(
        name="Lid_Arched",
        width=bw + 0.03,
        depth=bd + 0.03,
        arch_height=arch_h,
        num_segments=8,
        material=mat_wood
    )
    lid.location = (0, 0, bh + 0.085)

    # 3 Flejes dorados que recorren el arco de la tapa
    for sx in [-bw * 0.32, 0.0, bw * 0.32]:
        strap_arch = make_watertight_arch_mesh(
            name=f"Strap_{sx}",
            width=0.045,
            depth=bd + 0.04,
            arch_height=arch_h + 0.012,
            num_segments=8,
            material=mat_gold
        )
        strap_arch.location = (sx, 0, bh + 0.085)

    # 3. Cerradura frontal con placa de latón y bocallave
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0, bd * 0.5 + 0.025, bh * 0.78))
    lock_plate = bpy.context.active_object
    lock_plate.scale = (0.14, 0.025, 0.16)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    lock_plate.data.materials.append(mat_gold)
    
    bpy.ops.mesh.primitive_cylinder_add(vertices=6, radius=0.025, depth=0.035, location=(0, bd * 0.5 + 0.035, bh * 0.76))
    keyhole = bpy.context.active_object
    keyhole.data.materials.append(get_material("DarkMetal"))

    # 4. Asas laterales de latón
    for sx in [-1, 1]:
        bpy.ops.mesh.primitive_cube_add(size=1.0, location=(sx * (bw * 0.5 + 0.015), 0, bh * 0.65))
        h_plate = bpy.context.active_object
        h_plate.scale = (0.015, 0.14, 0.08)
        bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
        h_plate.data.materials.append(mat_gold)
        bpy.ops.mesh.primitive_cylinder_add(vertices=6, radius=0.055, depth=0.02, location=(sx * (bw * 0.5 + 0.035), 0, bh * 0.62), rotation=(0, math.radians(90), 0))
        h_ring = bpy.context.active_object
        h_ring.data.materials.append(mat_gold)

    # 5. Juguetes de apoyo en el suelo al lado del baúl
    bpy.ops.mesh.primitive_cube_add(size=0.18, location=(bw * 0.5 + 0.18, bd * 0.20, 0.09), rotation=(0, 0, math.radians(18)))
    b1 = bpy.context.active_object
    b1.data.materials.append(get_material("PastelRed"))
    
    bpy.ops.mesh.primitive_cylinder_add(vertices=6, radius=0.09, depth=0.18, location=(-bw * 0.35, bd * 0.5 + 0.18, 0.09), rotation=(math.radians(90), 0, math.radians(25)))
    b2 = bpy.context.active_object
    b2.data.materials.append(get_material("PastelYellow"))

    # Finalizar modelo: ajustar Z=0 en base, unir en prop único y exportar
    mesh_objs = [o for o in bpy.context.scene.objects if o.type == 'MESH']
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
    bpy.ops.object.join()
    final_obj = bpy.context.active_object
    final_obj.name = "baul_juguetes"

    total_tris = sum(len(p.vertices) - 2 for p in final_obj.data.polygons)
    bbox = final_obj.bound_box
    height = max(b[2] for b in bbox) - min(b[2] for b in bbox)
    width_x = max(b[0] for b in bbox) - min(b[0] for b in bbox)
    depth_y = max(b[1] for b in bbox) - min(b[1] for b in bbox)
    print(f"\n>>> MODELO CORREGIDO Y SÓLIDO: baul_juguetes")
    print(f"    Triángulos: {total_tris} (Presupuesto <= 800: {'OK' if total_tris <= 800 else 'EXCEDIDO'})")
    print(f"    Dimensiones: Ancho {width_x:.2f}m x Prof {depth_y:.2f}m x Alto {height:.2f}m")

    # Luces y Cámara
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
    glb_path = PROPS_DIR / "baul_juguetes.glb"
    blend_path = SOURCE_DIR / "baul_juguetes.blend"
    preview_path = SOURCE_DIR / "baul_juguetes_preview.png"
    drive_glb = DRIVE_DIR / "baul_juguetes.glb"
    drive_blend = DRIVE_DIR / "baul_juguetes.blend"
    drive_preview = DRIVE_DIR / "baul_juguetes_preview.png"
    artifact_preview = ARTIFACTS_DIR / "baul_juguetes_preview.png"

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


if __name__ == "__main__":
    build_baul()
