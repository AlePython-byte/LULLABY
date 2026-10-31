import sys
import math
from pathlib import Path
import shutil

sys.path.append(r"c:\blender-mcp\generators")
sys.path.append(r"C:\LULLABY\assets\source\blender\items")

import bpy
from mathutils import Vector
import generate_items

ITEM_CONFIGS = [
    {
        "name": "icono_biberon",
        "builder": generate_items.build_biberon,
        "azimuth_deg": 35,
        "elevation_deg": 25,
        "lens": 60,
        "pad": 1.25,
    },
    {
        "name": "icono_fotografia",
        "builder": generate_items.build_fotografia_rota,
        "azimuth_deg": 25,
        "elevation_deg": 20,
        "lens": 60,
        "pad": 1.25,
    },
    {
        "name": "icono_chupete",
        "builder": generate_items.build_chupete,
        "azimuth_deg": 155,
        "elevation_deg": 30,
        "lens": 60,
        "pad": 1.25,
    },
    {
        "name": "icono_oso",
        "builder": generate_items.build_oso_peluche,
        "azimuth_deg": 30,
        "elevation_deg": 18,
        "lens": 60,
        "pad": 1.25,
    },
    {
        "name": "icono_manta",
        "builder": generate_items.build_manta_vieja,
        "azimuth_deg": 55,
        "elevation_deg": 35,
        "lens": 60,
        "pad": 1.25,
    },
]

REPO_ROOT = Path(r"C:\LULLABY")
REPO_UI_DIR = REPO_ROOT / "assets" / "ui"
DRIVE_ICONS_DIR = Path(r"C:\Users\davin\OneDrive\Documentos\MODELOS JUEGO\items\icons")
BRAIN_DIR = Path(r"C:\Users\davin\.gemini\antigravity\brain\61207137-232d-4414-8860-f3fa45906692")

for d in [
    REPO_UI_DIR,
    REPO_UI_DIR / "128",
    REPO_UI_DIR / "512",
    DRIVE_ICONS_DIR,
    DRIVE_ICONS_DIR / "128",
    DRIVE_ICONS_DIR / "512",
]:
    d.mkdir(parents=True, exist_ok=True)

def setup_camera_and_lights(item_obj, cfg):
    scene = bpy.context.scene
    scene.render.film_transparent = True
    scene.render.image_settings.media_type = 'IMAGE'
    scene.render.image_settings.file_format = 'PNG'
    scene.render.image_settings.color_mode = 'RGBA'
    
    for o in list(scene.objects):
        if o != item_obj:
            bpy.data.objects.remove(o, do_unlink=True)
            
    bbox = [item_obj.matrix_world @ Vector(corner) for corner in item_obj.bound_box]
    min_x = min(v.x for v in bbox)
    max_x = max(v.x for v in bbox)
    min_y = min(v.y for v in bbox)
    max_y = max(v.y for v in bbox)
    min_z = min(v.z for v in bbox)
    max_z = max(v.z for v in bbox)
    
    center = Vector(((min_x + max_x)/2, (min_y + max_y)/2, (min_z + max_z)/2))
    max_dim = max(max_x - min_x, max_y - min_y, max_z - min_z)
    
    cam_data = bpy.data.cameras.new("IconCamera")
    cam_data.lens = cfg.get("lens", 60.0)
    cam_obj = bpy.data.objects.new("IconCamera", cam_data)
    scene.collection.objects.link(cam_obj)
    scene.camera = cam_obj
    
    azimuth = math.radians(cfg["azimuth_deg"])
    elevation = math.radians(cfg["elevation_deg"])
    pad = cfg.get("pad", 1.25)
    
    fov = 2.0 * math.atan(18.0 / cam_data.lens)
    req_dist = (max_dim * pad) / (2.0 * math.tan(fov / 2.0))
    
    cam_x = center.x + req_dist * math.sin(azimuth) * math.cos(elevation)
    cam_y = center.y + req_dist * math.cos(azimuth) * math.cos(elevation)
    cam_z = center.z + req_dist * math.sin(elevation)
    
    cam_obj.location = (cam_x, cam_y, cam_z)
    dir_vec = center - cam_obj.location
    cam_obj.rotation_euler = dir_vec.to_track_quat('-Z', 'Y').to_euler()
    
    # 3-point lighting relative to camera:
    key_az = azimuth + math.radians(35)
    key_el = elevation + math.radians(25)
    key_data = bpy.data.lights.new("KeyLight", type='SUN')
    key_data.energy = 4.2
    key_data.color = (1.0, 0.98, 0.95)
    key_obj = bpy.data.objects.new("KeyLight", key_data)
    k_dir = Vector((-math.sin(key_az) * math.cos(key_el), -math.cos(key_az) * math.cos(key_el), -math.sin(key_el)))
    key_obj.rotation_euler = k_dir.to_track_quat('-Z', 'Y').to_euler()
    scene.collection.objects.link(key_obj)
    
    fill_az = azimuth - math.radians(45)
    fill_el = elevation + math.radians(10)
    fill_data = bpy.data.lights.new("FillLight", type='SUN')
    fill_data.energy = 2.5
    fill_data.color = (0.88, 0.92, 1.0)
    fill_obj = bpy.data.objects.new("FillLight", fill_data)
    f_dir = Vector((-math.sin(fill_az) * math.cos(fill_el), -math.cos(fill_az) * math.cos(fill_el), -math.sin(fill_el)))
    fill_obj.rotation_euler = f_dir.to_track_quat('-Z', 'Y').to_euler()
    scene.collection.objects.link(fill_obj)
    
    rim_az = azimuth + math.radians(150)
    rim_el = elevation + math.radians(35)
    rim_data = bpy.data.lights.new("RimLight", type='SUN')
    rim_data.energy = 3.6
    rim_data.color = (1.0, 1.0, 1.0)
    rim_obj = bpy.data.objects.new("RimLight", rim_data)
    r_dir = Vector((-math.sin(rim_az) * math.cos(rim_el), -math.cos(rim_az) * math.cos(rim_el), -math.sin(rim_el)))
    rim_obj.rotation_euler = r_dir.to_track_quat('-Z', 'Y').to_euler()
    scene.collection.objects.link(rim_obj)

def generate_all_icons():
    print("=== GENERATING PRODUCTION ITEM ICONS (128x128 & 512x512) ===")
    results = []
    
    for item in ITEM_CONFIGS:
        name = item["name"]
        print(f"\nProcessing {name}...")
        obj = item["builder"]()
        setup_camera_and_lights(obj, item)
        scene = bpy.context.scene
        
        # 1. Render 512x512
        scene.render.resolution_x = 512
        scene.render.resolution_y = 512
        p512_brain = BRAIN_DIR / f"{name}_512.png"
        scene.render.filepath = str(p512_brain)
        bpy.ops.render.render(write_still=True)
        
        # 2. Render 128x128
        scene.render.resolution_x = 128
        scene.render.resolution_y = 128
        p128_brain = BRAIN_DIR / f"{name}_128.png"
        scene.render.filepath = str(p128_brain)
        bpy.ops.render.render(write_still=True)
        
        # Copy to brain standard
        shutil.copy2(p512_brain, BRAIN_DIR / f"{name}.png")
        
        # Copy to repo assets/ui/
        # Main file in assets/ui/ is standard 128x128, plus high-res in 512/ and standard in 128/
        shutil.copy2(p128_brain, REPO_UI_DIR / f"{name}.png")
        shutil.copy2(p128_brain, REPO_UI_DIR / "128" / f"{name}.png")
        shutil.copy2(p512_brain, REPO_UI_DIR / "512" / f"{name}.png")
        shutil.copy2(p512_brain, REPO_UI_DIR / f"{name}_512.png")
        
        # Copy to OneDrive
        shutil.copy2(p128_brain, DRIVE_ICONS_DIR / f"{name}.png")
        shutil.copy2(p128_brain, DRIVE_ICONS_DIR / "128" / f"{name}.png")
        shutil.copy2(p512_brain, DRIVE_ICONS_DIR / "512" / f"{name}.png")
        shutil.copy2(p512_brain, DRIVE_ICONS_DIR / f"{name}_512.png")
        
        results.append({
            "name": name,
            "size_128": (REPO_UI_DIR / f"{name}.png").stat().st_size,
            "size_512": (REPO_UI_DIR / "512" / f"{name}.png").stat().st_size,
        })
        print(f"  [OK] {name}: 128px={results[-1]['size_128']}B, 512px={results[-1]['size_512']}B")
        
    print("\n=== RESUMEN DE ICONOS GENERADOS ===")
    for r in results:
        print(f"  - {r['name']}.png (128x128 y 512x512) guardado con exito.")

if __name__ == "__main__":
    generate_all_icons()
