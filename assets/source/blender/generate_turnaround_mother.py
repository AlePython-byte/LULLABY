r"""Generador del Video Turnaround 360° oficial de The Mother para LULLABY:
Genera un video en bucle perfecto (loop) de 9 segundos a 30 fps (270 fotogramas)
en formato MP4 (H.264), resolucion 1080x1080, fondo de estudio oscuro y 3 puntos de luz.

Destinos:
- assets/promo/turnaround_the_mother.mp4
- C:\Users\davin\OneDrive\Documentos\MODELOS JUEGO\promo\turnaround_the_mother.mp4
- C:\Users\davin\OneDrive\Documentos\MODELOS JUEGO\the_mother\turnaround_the_mother.mp4
- Artifacts directory
"""

import math
import shutil
from pathlib import Path
import bpy
from mathutils import Vector

LULLABY_ROOT = Path(r"C:\LULLABY")
PROMO_DIR = LULLABY_ROOT / "assets" / "promo"
SOURCE_DIR = LULLABY_ROOT / "assets" / "source" / "blender"
DRIVE_PROMO = Path(r"C:\Users\davin\OneDrive\Documentos\MODELOS JUEGO\promo")
DRIVE_MOTHER = Path(r"C:\Users\davin\OneDrive\Documentos\MODELOS JUEGO\the_mother")
ARTIFACTS_DIR = Path(r"C:\Users\davin\.gemini\antigravity\brain\61207137-232d-4414-8860-f3fa45906692")

PROMO_DIR.mkdir(parents=True, exist_ok=True)
DRIVE_PROMO.mkdir(parents=True, exist_ok=True)
DRIVE_MOTHER.mkdir(parents=True, exist_ok=True)

bpy.context.preferences.filepaths.save_version = 0


def srgb_to_linear(v: float) -> float:
    return v / 12.92 if v <= 0.04045 else ((v + 0.055) / 1.055) ** 2.4


def generate_turnaround():
    print("\n================== GENERANDO TURNAROUND 360° THE MOTHER ==================")
    blend_path = SOURCE_DIR / "the_mother.blend"
    bpy.ops.wm.open_mainfile(filepath=str(blend_path))

    # Configurar fondo oscuro de estudio
    world = bpy.context.scene.world
    if not world:
        world = bpy.data.worlds.new("PromoWorld")
        bpy.context.scene.world = world
    world.use_nodes = True
    bg = next((n for n in world.node_tree.nodes if n.type == 'BACKGROUND'), None)
    if bg:
        srgb = (0.082, 0.094, 0.133)
        lin = tuple(srgb_to_linear(c) for c in srgb)
        bg.inputs[0].default_value = (*lin, 1.0)
        bg.inputs[1].default_value = 1.0

    # Limpiar pisos previos y cámaras/luces
    for o in list(bpy.context.scene.objects):
        if "floor" in o.name.lower() or "preview" in o.name.lower() or o.type in {'LIGHT', 'CAMERA'}:
            bpy.data.objects.remove(o, do_unlink=True)

    mesh_objs = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    all_verts = [o.matrix_world @ v.co for o in mesh_objs for v in o.data.vertices]
    min_x, max_x = min(v.x for v in all_verts), max(v.x for v in all_verts)
    min_y, max_y = min(v.y for v in all_verts), max(v.y for v in all_verts)
    min_z, max_z = min(v.z for v in all_verts), max(v.z for v in all_verts)

    center = Vector(((min_x + max_x) * 0.5, (min_y + max_y) * 0.5, (min_z + max_z) * 0.5 + 0.15))
    height = max_z - min_z
    max_dim = max(max_x - min_x, max_y - min_y, height)
    dist = max(max_dim * 2.3, 1.8) * 1.08

    # Iluminación de estudio
    key_data = bpy.data.lights.new("KeyLight", type='SUN')
    key_data.energy = 4.2
    key_data.color = (0.98, 0.94, 0.88)
    key_obj = bpy.data.objects.new("KeyLight", key_data)
    key_dir = Vector((-0.3, -0.7, -0.6)).normalized()
    key_obj.rotation_euler = key_dir.to_track_quat('-Z', 'Y').to_euler()
    bpy.context.collection.objects.link(key_obj)

    fill_data = bpy.data.lights.new("FillLight", type='SUN')
    fill_data.energy = 1.4
    fill_data.color = (0.50, 0.62, 0.85)
    fill_obj = bpy.data.objects.new("FillLight", fill_data)
    fill_dir = Vector((0.4, -0.6, -0.4)).normalized()
    fill_obj.rotation_euler = fill_dir.to_track_quat('-Z', 'Y').to_euler()
    bpy.context.collection.objects.link(fill_obj)

    rim_data = bpy.data.lights.new("RimLight", type='POINT')
    rim_data.energy = 150.0
    rim_data.color = (0.65, 0.80, 1.0)
    rim_obj = bpy.data.objects.new("RimLight", rim_data)
    rim_obj.location = (0.0, -4.5, 3.8)
    bpy.context.collection.objects.link(rim_obj)

    # Crear pivote central para el giro en el origen (0, 0, 0) para no desplazar ningún vértice hacia arriba
    turntable = bpy.data.objects.new("TurntablePivot", None)
    turntable.location = (0.0, 0.0, 0.0)
    bpy.context.collection.objects.link(turntable)

    for o in mesh_objs:
        o.parent = turntable

    # Driver de rotación lineal constante (360° en 270 frames = 9s a 30fps)
    fcurve = turntable.driver_add('rotation_euler', 2)
    driver = fcurve.driver
    driver.type = 'SCRIPTED'
    driver.expression = '(frame - 1) * 6.283185307179586 / 270'

    # Cámara encuadrada perfectamente de cuerpo entero (The Mother mide 3.5m)
    cam_data = bpy.data.cameras.new("TurnaroundCam")
    cam_data.lens = 45
    cam_obj = bpy.data.objects.new("TurnaroundCam", cam_data)
    target = Vector((0.0, 0.0, 1.75))
    dist = 5.8
    pitch = math.radians(8)
    cam_obj.location = (0.0, dist * math.cos(pitch), 1.75 + dist * math.sin(pitch))
    dir_cam = target - cam_obj.location
    cam_obj.rotation_euler = dir_cam.to_track_quat('-Z', 'Y').to_euler()
    bpy.context.collection.objects.link(cam_obj)

    scene = bpy.context.scene
    scene.camera = cam_obj
    scene.frame_start = 1
    scene.frame_end = 270
    scene.render.fps = 30

    # Configuración de video MP4 H.264
    scene.render.image_settings.media_type = 'VIDEO'
    scene.render.image_settings.file_format = 'FFMPEG'
    scene.render.ffmpeg.format = 'MPEG4'
    scene.render.ffmpeg.codec = 'H264'
    scene.render.ffmpeg.constant_rate_factor = 'MEDIUM'
    scene.render.ffmpeg.ffmpeg_preset = 'GOOD'
    scene.render.resolution_x = 1080
    scene.render.resolution_y = 1080
    scene.render.resolution_percentage = 100

    out_mp4 = PROMO_DIR / "turnaround_the_mother.mp4"
    scene.render.filepath = str(out_mp4)

    print("Renderizando animación de video...")
    bpy.ops.render.render(animation=True)

    # Copiar a destinos
    shutil.copy2(out_mp4, DRIVE_PROMO / "turnaround_the_mother.mp4")
    shutil.copy2(out_mp4, DRIVE_MOTHER / "turnaround_the_mother.mp4")
    shutil.copy2(out_mp4, ARTIFACTS_DIR / "turnaround_the_mother.mp4")
    shutil.copy2(out_mp4, SOURCE_DIR / "turnaround_the_mother.mp4")

    # Guardar .blend con la animación
    bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE_DIR / "turnaround_the_mother.blend"))
    shutil.copy2(SOURCE_DIR / "turnaround_the_mother.blend", DRIVE_PROMO / "turnaround_the_mother.blend")
    print(f"Video completado exitosamente: {out_mp4}")


if __name__ == "__main__":
    generate_turnaround()
