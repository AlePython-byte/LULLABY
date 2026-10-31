"""Pone la base del oso_gigante en Y=0 (Godot) / Z=0 (Blender): aplica transformaciones (Ctrl+A),
mueve la geometria para que el punto mas bajo quede en 0 y re-exporta el GLB.
Uso: blender -b assets/source/blender/oso_gigante.blend --python fix_oso_base.py
"""
import bpy
from pathlib import Path

GLB = Path(r"C:\LULLABY\assets\models\props\oso_gigante.glb")
BLEND = Path(r"C:\LULLABY\assets\source\blender\oso_gigante.blend")
DRIVE = Path(r"C:\Users\davin\OneDrive\Documentos\MODELOS JUEGO\oso_gigante")
bpy.context.preferences.filepaths.save_version = 0

meshes = [o for o in bpy.data.objects if o.type == 'MESH']
bpy.ops.object.select_all(action='DESELECT')
for o in meshes:
    o.select_set(True)
bpy.context.view_layer.objects.active = meshes[0]
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

min_z = min((o.matrix_world @ v.co).z for o in meshes for v in o.data.vertices)
print("min z antes:", min_z)
for o in meshes:
    o.location.z -= min_z
bpy.ops.object.transform_apply(location=True, rotation=False, scale=False)
min_z2 = min((o.matrix_world @ v.co).z for o in meshes for v in o.data.vertices)
print("min z despues:", min_z2)
assert abs(min_z2) < 1e-5

bpy.ops.object.select_all(action='DESELECT')
for o in meshes:
    o.select_set(True)
bpy.context.view_layer.objects.active = meshes[0]
bpy.ops.export_scene.gltf(filepath=str(GLB), export_format='GLB', use_selection=True,
                          export_yup=True, export_apply=False)
bpy.ops.wm.save_as_mainfile(filepath=str(BLEND), compress=True)
import shutil
if DRIVE.exists():
    shutil.copy2(GLB, DRIVE / "oso_gigante.glb")
    shutil.copy2(BLEND, DRIVE / "oso_gigante.blend")
print("OK")
