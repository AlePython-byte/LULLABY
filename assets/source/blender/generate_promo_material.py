"""Generador de Material Promocional y de Sustentación para LULLABY:
Genera:
1. Ocho (8) renders individuales de personajes en 1200x1200 sobre fondo liso de estudio tétrico:
   - promo_noah.png
   - promo_the_mother.png
   - promo_basic_enemy.png
   - promo_exploder_enemy.png
   - promo_shooter_enemy.png
   - promo_flyer_enemy.png
   - promo_runner_enemy.png
   - promo_digger_enemy.png
2. Portada oficial panorámica en 1920x1080:
   - portada.png (composición épica/creepy con Noah, enemigos y The Mother)

Reglas:
- Destino oficial: assets/promo/
- Copias a OneDrive: MODELOS JUEGO/promo/
- Copias a Artifacts para visualización en chat
"""

import math
import shutil
from pathlib import Path
import bpy
from mathutils import Vector, Matrix

LULLABY_ROOT = Path(r"C:\LULLABY")
PROMO_DIR = LULLABY_ROOT / "assets" / "promo"
SOURCE_DIR = LULLABY_ROOT / "assets" / "source" / "blender"
MODELS_DIR = LULLABY_ROOT / "assets" / "models"
DRIVE_PROMO = Path(r"C:\Users\davin\OneDrive\Documentos\MODELOS JUEGO\promo")
ARTIFACTS_DIR = Path(r"C:\Users\davin\.gemini\antigravity\brain\61207137-232d-4414-8860-f3fa45906692")

PROMO_DIR.mkdir(parents=True, exist_ok=True)
DRIVE_PROMO.mkdir(parents=True, exist_ok=True)

bpy.context.preferences.filepaths.save_version = 0


def clean_scene():
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete(use_global=False)
    for block in [
        bpy.data.meshes,
        bpy.data.materials,
        bpy.data.cameras,
        bpy.data.lights,
        bpy.data.images,
    ]:
        for item in list(block):
            if item.users == 0:
                block.remove(item)


def srgb_to_linear(v: float) -> float:
    return v / 12.92 if v <= 0.04045 else ((v + 0.055) / 1.055) ** 2.4


def set_dark_studio_world():
    world = bpy.context.scene.world
    if not world:
        world = bpy.data.worlds.new("PromoWorld")
        bpy.context.scene.world = world
    world.use_nodes = True
    bg = next((n for n in world.node_tree.nodes if n.type == 'BACKGROUND'), None)
    if bg:
        # Azul noche grisáceo penumbra de guardería (#151822)
        srgb = (0.082, 0.094, 0.133)
        lin = tuple(srgb_to_linear(c) for c in srgb)
        bg.inputs[0].default_value = (*lin, 1.0)
        bg.inputs[1].default_value = 1.0


def render_character_promo(char_name, blend_file, center_offset=(0, 0, 0), cam_dist_mult=1.0, pitch_deg=16, yaw_deg=-32):
    print(f"\n>>> Generando Promo Character: promo_{char_name}.png")
    blend_path = SOURCE_DIR / blend_file
    if not blend_path.exists():
        print(f"ERROR: No existe {blend_path}")
        return

    bpy.ops.wm.open_mainfile(filepath=str(blend_path))
    set_dark_studio_world()

    # Ocultar o eliminar Floor previo para que quede fondo liso de estudio
    for o in list(bpy.context.scene.objects):
        if "floor" in o.name.lower() or "previewfloor" in o.name.lower():
            bpy.data.objects.remove(o, do_unlink=True)
        elif o.type in {'LIGHT', 'CAMERA'}:
            bpy.data.objects.remove(o, do_unlink=True)

    mesh_objs = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    if not mesh_objs:
        print("ERROR: No se encontraron mallas.")
        return

    # Calcular centro y dimensiones del personaje
    all_verts = [o.matrix_world @ v.co for o in mesh_objs for v in o.data.vertices]
    min_x = min(v.x for v in all_verts)
    max_x = max(v.x for v in all_verts)
    min_y = min(v.y for v in all_verts)
    max_y = max(v.y for v in all_verts)
    min_z = min(v.z for v in all_verts)
    max_z = max(v.z for v in all_verts)

    center = Vector(((min_x + max_x) * 0.5, (min_y + max_y) * 0.5, (min_z + max_z) * 0.5)) + Vector(center_offset)
    height = max_z - min_z
    max_dim = max(max_x - min_x, max_y - min_y, height)
    dist = max(max_dim * 2.3, 1.8) * cam_dist_mult

    # Luces de estudio de 3 puntos (Estilo terror infantil / coleccionable)
    # 1. Key Light (Cálida suave frontal-lateral)
    key_data = bpy.data.lights.new("KeyLight", type='SUN')
    key_data.energy = 4.2
    key_data.color = (0.98, 0.94, 0.88)
    key_obj = bpy.data.objects.new("KeyLight", key_data)
    key_obj.rotation_euler = (math.radians(52), math.radians(18), math.radians(-38))
    bpy.context.collection.objects.link(key_obj)

    # 2. Fill Light (Fría azulada de relleno)
    fill_data = bpy.data.lights.new("FillLight", type='SUN')
    fill_data.energy = 1.3
    fill_data.color = (0.45, 0.58, 0.82)
    fill_obj = bpy.data.objects.new("FillLight", fill_data)
    fill_obj.rotation_euler = (math.radians(-35), math.radians(-25), math.radians(145))
    bpy.context.collection.objects.link(fill_obj)

    # 3. Rim Light (Luz trasera de contorno para recortar la silueta en la sombra)
    rim_data = bpy.data.lights.new("RimLight", type='POINT')
    rim_data.energy = 120.0
    rim_data.color = (0.60, 0.78, 1.0)
    rim_obj = bpy.data.objects.new("RimLight", rim_data)
    rim_obj.location = (center.x - dist * 0.5, center.y + dist * 0.8, center.z + height * 0.8)
    bpy.context.collection.objects.link(rim_obj)

    # Cámara 3/4 a 1200x1200
    cam_data = bpy.data.cameras.new("PromoCam")
    cam_data.lens = 55
    cam_obj = bpy.data.objects.new("PromoCam", cam_data)
    
    yaw = math.radians(yaw_deg)
    pitch = math.radians(pitch_deg)
    cam_x = center.x + dist * math.sin(yaw) * math.cos(pitch)
    cam_y = center.y - dist * math.cos(yaw) * math.cos(pitch)
    cam_z = center.z + dist * math.sin(pitch)
    cam_obj.location = (cam_x, cam_y, cam_z)
    
    dir_to_target = center - cam_obj.location
    cam_obj.rotation_euler = dir_to_target.to_track_quat('-Z', 'Y').to_euler()
    bpy.context.collection.objects.link(cam_obj)
    bpy.context.scene.camera = cam_obj

    # Render
    out_name = f"promo_{char_name}.png"
    out_promo = PROMO_DIR / out_name
    out_drive = DRIVE_PROMO / out_name
    out_art = ARTIFACTS_DIR / out_name

    bpy.context.scene.render.resolution_x = 1200
    bpy.context.scene.render.resolution_y = 1200
    bpy.context.scene.render.filepath = str(out_promo)
    bpy.ops.render.render(write_still=True)

    shutil.copy2(out_promo, out_drive)
    shutil.copy2(out_promo, out_art)
    print(f"Guardado exitoso: {out_promo}")


def render_all_characters():
    chars = [
        # (name, blend_file, center_offset, cam_dist_mult, pitch, yaw)
        ("noah", "noah.blend", (0, 0, 0.05), 1.0, 14, -28),
        ("the_mother", "the_mother.blend", (0, 0, 0.15), 1.08, 12, -26),
        ("basic_enemy", "basic_enemy.blend", (0, 0, 0.0), 1.0, 15, -30),
        ("exploder_enemy", "exploder_enemy.blend", (0, 0, 0.05), 1.0, 15, -30),
        ("shooter_enemy", "shooter_enemy.blend", (0, 0, 0.0), 1.0, 15, -30),
        ("flyer_enemy", "flyer_enemy.blend", (0, 0, 0.10), 1.05, 14, -28),
        ("runner_enemy", "runner_enemy.blend", (0, 0, 0.0), 1.0, 15, -32),
        ("digger_enemy", "digger_enemy.blend", (0, 0, 0.0), 1.0, 15, -28),
    ]
    for cname, bfile, coff, cdist, cpitch, cyaw in chars:
        out_promo = PROMO_DIR / f"promo_{cname}.png"
        if not out_promo.exists():
            render_character_promo(cname, bfile, coff, cdist, cpitch, cyaw)
        else:
            print(f"Ya existe: {out_promo}, omitiendo re-render.")


def render_portada():
    print("\n================== GENERANDO PORTADA OFICIAL (1920x1080) ==================")
    clean_scene()
    set_dark_studio_world()

    # Importar los modelos GLB principales para armar la composición
    imported_assets = [
        ("TheMother", MODELS_DIR / "the_mother.glb", (0.0, 2.8, 0.4), (1.1, 1.1, 1.1), (0, 0, math.radians(180))),
        ("Noah", MODELS_DIR / "noah.glb", (0.0, -0.6, 0.0), (1.0, 1.0, 1.0), (0, 0, math.radians(0))),
        ("Shooter", MODELS_DIR / "shooter_enemy.glb", (-1.8, 0.6, 0.0), (0.9, 0.9, 0.9), (0, 0, math.radians(35))),
        ("Exploder", MODELS_DIR / "exploder_enemy.glb", (1.8, 0.6, 0.0), (0.9, 0.9, 0.9), (0, 0, math.radians(-35))),
        ("Flyer", MODELS_DIR / "flyer_enemy.glb", (-1.5, 1.8, 1.5), (0.85, 0.85, 0.85), (math.radians(-10), 0, math.radians(30))),
        ("Runner", MODELS_DIR / "runner_enemy.glb", (-2.6, 1.5, 0.0), (0.85, 0.85, 0.85), (0, 0, math.radians(45))),
        ("Digger", MODELS_DIR / "digger_enemy.glb", (1.6, 1.7, 0.0), (0.85, 0.85, 0.85), (0, 0, math.radians(-40))),
        ("Basic", MODELS_DIR / "basic_enemy.glb", (2.6, 1.4, 0.0), (0.85, 0.85, 0.85), (0, 0, math.radians(-45))),
    ]

    for name, path, loc, sc, rot in imported_assets:
        if path.exists():
            before_objs = set(bpy.context.scene.objects)
            bpy.ops.import_scene.gltf(filepath=str(path))
            new_objs = list(set(bpy.context.scene.objects) - before_objs)
            # Agrupar en un contenedor o posicionar
            empty = bpy.data.objects.new(name, None)
            empty.location = loc
            empty.scale = sc
            empty.rotation_euler = rot
            bpy.context.collection.objects.link(empty)
            for o in new_objs:
                o.parent = empty
        else:
            print(f"ADVERTENCIA: No se encontró {path}")

    # Suelo oscuro sutil para sombras de contacto
    bpy.ops.mesh.primitive_plane_add(size=25.0, location=(0, 0, 0))
    floor = bpy.context.active_object
    mat_floor = bpy.data.materials.new("FloorDark")
    mat_floor.use_nodes = True
    bsdf = next(n for n in mat_floor.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    srgb_f = (0.06, 0.07, 0.10)
    bsdf.inputs["Base Color"].default_value = (*tuple(srgb_to_linear(c) for c in srgb_f), 1.0)
    bsdf.inputs["Roughness"].default_value = 0.95
    floor.data.materials.append(mat_floor)

    # Iluminación Cinematográfica de Portada
    # 1. Luz de foco central sobre Noah (Inocencia en el peligro)
    spot_noah = bpy.data.lights.new("SpotNoah", type='SPOT')
    spot_noah.energy = 85.0
    spot_noah.spot_size = math.radians(45)
    spot_noah.spot_blend = 0.4
    spot_noah.color = (1.0, 0.95, 0.88)
    spot_noah_obj = bpy.data.objects.new("SpotNoah", spot_noah)
    spot_noah_obj.location = (0.0, -1.8, 3.2)
    # Apuntar hacia Noah
    dir_n = Vector((0, -0.6, 0.4)) - spot_noah_obj.location
    spot_noah_obj.rotation_euler = dir_n.to_track_quat('-Z', 'Y').to_euler()
    bpy.context.collection.objects.link(spot_noah_obj)

    # 2. Luz de atmósfera para The Mother (Cenital fría amenazante)
    spot_mother = bpy.data.lights.new("SpotMother", type='SPOT')
    spot_mother.energy = 160.0
    spot_mother.spot_size = math.radians(65)
    spot_mother.spot_blend = 0.5
    spot_mother.color = (0.65, 0.80, 1.0)
    spot_mother_obj = bpy.data.objects.new("SpotMother", spot_mother)
    spot_mother_obj.location = (0.0, 2.2, 5.0)
    dir_m = Vector((0, 2.8, 1.8)) - spot_mother_obj.location
    spot_mother_obj.rotation_euler = dir_m.to_track_quat('-Z', 'Y').to_euler()
    bpy.context.collection.objects.link(spot_mother_obj)

    # 3. Luz ambiental suave púrpura/noche
    sun_amb = bpy.data.lights.new("SunAmb", type='SUN')
    sun_amb.energy = 1.1
    sun_amb.color = (0.40, 0.45, 0.70)
    sun_amb_obj = bpy.data.objects.new("SunAmb", sun_amb)
    sun_amb_obj.rotation_euler = (math.radians(60), math.radians(10), math.radians(-30))
    bpy.context.collection.objects.link(sun_amb_obj)

    # Cámara Cinematográfica Panorámica 1920x1080
    cam = bpy.data.cameras.new("PortadaCam")
    cam.lens = 45  # Ángulo cinematográfico
    cam_obj = bpy.data.objects.new("PortadaCam", cam)
    cam_obj.location = (0.0, -4.6, 1.55)
    # Mirar ligeramente hacia arriba para dar majestuosidad
    target_cover = Vector((0.0, 1.2, 1.45))
    dir_cover = target_cover - cam_obj.location
    cam_obj.rotation_euler = dir_cover.to_track_quat('-Z', 'Y').to_euler()
    bpy.context.collection.objects.link(cam_obj)
    bpy.context.scene.camera = cam_obj

    # Render
    out_portada = PROMO_DIR / "portada.png"
    out_drive = DRIVE_PROMO / "portada.png"
    out_art = ARTIFACTS_DIR / "portada.png"

    bpy.context.scene.render.resolution_x = 1920
    bpy.context.scene.render.resolution_y = 1080
    bpy.context.scene.render.filepath = str(out_portada)
    bpy.ops.render.render(write_still=True)

    shutil.copy2(out_portada, out_drive)
    shutil.copy2(out_portada, out_art)
    
    # Guardar la escena de portada en blender para respaldo
    bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE_DIR / "portada.blend"))
    print(f"Portada generada con éxito: {out_portada}")


if __name__ == "__main__":
    print("\n================== INICIANDO GENERACIÓN DE PROMO ==================")
    render_all_characters()
    render_portada()
    print("\n================== MATERIAL PROMO COMPLETADO ==================")
