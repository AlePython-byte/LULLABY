"""The Mother: Jefa de The Cradle para LULLABY.
Muñeca de trapo gigante con cara de porcelana y brazos muy largos que intentan envolver a Noah.
Estilo: terror psicológico infantil, juguete tierno e inquietante, colores planos sin texturas.

Generado para la entrega oficial de David:
  - assets/models/the_mother.glb
  - assets/source/blender/the_mother.blend
  - assets/source/blender/the_mother.py
  - assets/source/blender/the_mother_preview.png

Partes móviles separadas (según el plan de trabajo de Alejandro):
  - Base: Falda acampanada victoriana, pivote en el suelo (0, 0, 0)
  - Body: Torso victoriano, delantal crema y cuello alto, pivote en cintura (0, 0, 1.80)
  - Head: Cabeza de porcelana, mejillas, labios, grietas, pelo de lana y lazo cenital, pivote en cuello (0, 0, 2.55)
  - Eyes: Ojos oscuros de muñeca ligeramente asimétricos, pivote en cuello (0, 0, 2.55)
  - ArmL: Brazo largo izquierdo de trapo con mano acolchada, pivote en hombro (-0.48, 0, 2.45)
  - ArmR: Brazo largo derecho de trapo con mano acolchada, pivote en hombro (0.48, 0, 2.45)

Orientación:
  En Blender mira hacia +Y (en Godot con export_yup=True se importa mirando hacia -Z).
  Lado L es -X, lado R es +X.
"""

import math
import os
import shutil
import tempfile
from pathlib import Path

import bpy
import numpy as np
from mathutils import Matrix, Vector

# Rutas de destino
REPO_ROOT = Path(r"C:\LULLABY")
MODELS_DIR = REPO_ROOT / "assets" / "models"
SOURCE_DIR = REPO_ROOT / "assets" / "source" / "blender"
DRIVE_DIR = Path(r"C:\Users\davin\OneDrive\Documentos\MODELOS JUEGO\the_mother")

MODELS_DIR.mkdir(parents=True, exist_ok=True)
SOURCE_DIR.mkdir(parents=True, exist_ok=True)
DRIVE_DIR.mkdir(parents=True, exist_ok=True)

GLB_PATH = MODELS_DIR / "the_mother.glb"
BLEND_PATH = SOURCE_DIR / "the_mother.blend"
PY_PATH = SOURCE_DIR / "the_mother.py"
PREVIEW_PATH = SOURCE_DIR / "the_mother_preview.png"

RENDER_SIZE = 800

# Paleta victoriana suave de juguete (sRGB)
PALETTE = {
    "Dress": (0.76, 0.45, 0.52),      # Rosa viejo victoriano
    "ApronCream": (0.93, 0.88, 0.78), # Crema vintage
    "Porcelain": (0.95, 0.93, 0.89),  # Porcelana suave
    "RagHair": (0.24, 0.16, 0.14),    # Marrón lana oscura
    "Blush": (0.86, 0.40, 0.46),      # Colorete de muñeca
    "Dark": (0.10, 0.08, 0.09),       # Ojos oscuros y botones
    "Cracks": (0.28, 0.20, 0.20),     # Grieta en porcelana
}


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
                    inp.default_value = 0.25 if name == "Porcelain" else 0.70
                elif inp.name == "Metallic":
                    inp.default_value = 0.0
                elif inp.name in ("Specular IOR Level", "Specular"):
                    inp.default_value = 0.45 if name == "Porcelain" else 0.20
        mat.diffuse_color = (*linear, 1.0)
        materials[name] = mat
    return materials


def set_pivot(obj, pivot_co: tuple[float, float, float]):
    current_cursor = bpy.context.scene.cursor.location.copy()
    bpy.context.scene.cursor.location = pivot_co
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.origin_set(type='ORIGIN_CURSOR', center='MEDIAN')
    bpy.context.scene.cursor.location = current_cursor


def clean_scene():
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete(use_global=False)
    for block in bpy.data.meshes:
        if block.users == 0:
            bpy.data.meshes.remove(block)
    for block in bpy.data.materials:
        if block.users == 0:
            bpy.data.materials.remove(block)


def build_the_mother(materials):
    mother_collection = bpy.data.collections.new("TheMother")
    bpy.context.scene.collection.children.link(mother_collection)

    # =========================================================================
    # 1. BASE: Falda acampanada victoriana de muñeca de trapo (Z=0 a Z=1.80m)
    # Pivote en el suelo (0, 0, 0)
    # =========================================================================
    bpy.ops.mesh.primitive_cone_add(vertices=24, radius1=1.05, radius2=0.45, depth=1.80, location=(0, 0, 0.90))
    skirt = bpy.context.object
    skirt.name = "Base"
    skirt.scale = (1.05, 1.0, 1.0)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    skirt.data.materials.append(materials["Dress"])

    # Borde inferior del vestido (volante de encaje crema)
    bpy.ops.mesh.primitive_torus_add(major_radius=1.04, minor_radius=0.06, location=(0, 0, 0.06))
    skirt_rim = bpy.context.object
    skirt_rim.scale = (1.05, 1.0, 0.8)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    skirt_rim.data.materials.append(materials["ApronCream"])

    # Unir volante al objeto Base
    bpy.context.view_layer.objects.active = skirt
    skirt_rim.select_set(True)
    skirt.select_set(True)
    bpy.ops.object.join()
    base_obj = bpy.context.object
    set_pivot(base_obj, (0.0, 0.0, 0.0))

    # =========================================================================
    # 2. BODY: Torso victoriano, delantal crema y cuello alto (Z=1.80m a Z=2.55m)
    # Pivote en la cintura (0, 0, 1.80)
    # =========================================================================
    bpy.ops.mesh.primitive_cylinder_add(vertices=20, radius=0.42, depth=0.75, location=(0, 0, 2.175))
    torso = bpy.context.object
    torso.name = "Body"
    torso.scale = (1.0, 0.85, 1.0)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    torso.data.materials.append(materials["Dress"])

    # Cuello alto victoriano de encaje crema
    bpy.ops.mesh.primitive_torus_add(major_radius=0.35, minor_radius=0.08, location=(0, 0, 2.54))
    collar = bpy.context.object
    collar.scale = (1.0, 0.88, 0.70)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    collar.data.materials.append(materials["ApronCream"])

    # Delantal crema en el pecho (+Y es el frente)
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0, 0.38, 1.95))
    apron = bpy.context.object
    apron.scale = (0.52, 0.04, 0.85)
    apron.rotation_euler = (math.radians(6), 0, 0)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    apron.data.materials.append(materials["ApronCream"])

    # Botones oscuros del delantal hacia el frente (+Y)
    for bz in [2.25, 2.05, 1.85]:
        bpy.ops.mesh.primitive_cylinder_add(radius=0.035, depth=0.03, location=(0, 0.40, bz))
        btn = bpy.context.object
        btn.rotation_euler = (math.radians(90), 0, 0)
        bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
        btn.data.materials.append(materials["Dark"])
        bpy.context.view_layer.objects.active = apron
        btn.select_set(True)
        apron.select_set(True)
        bpy.ops.object.join()

    # Unir partes al Body
    bpy.context.view_layer.objects.active = torso
    for part in [collar, apron]:
        part.select_set(True)
    torso.select_set(True)
    bpy.ops.object.join()
    body_obj = bpy.context.object
    set_pivot(body_obj, (0.0, 0.0, 1.80))

    # =========================================================================
    # 3. HEAD: Cabeza de porcelana, pelo de lana y lazo cenital (Z=2.55m a Z=3.35m)
    # Pivote en el cuello (0, 0, 2.55)
    # =========================================================================
    bpy.ops.mesh.primitive_uv_sphere_add(segments=24, ring_count=16, radius=0.38, location=(0, 0, 2.92))
    head_mesh = bpy.context.object
    head_mesh.name = "Head"
    head_mesh.scale = (0.92, 0.95, 1.05)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    head_mesh.data.materials.append(materials["Porcelain"])

    # Mejillas con colorete de muñeca hacia +Y
    bpy.ops.mesh.primitive_uv_sphere_add(radius=0.075, location=(-0.20, 0.30, 2.84))
    blush_l = bpy.context.object
    blush_l.scale = (1.0, 0.35, 0.8)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    blush_l.data.materials.append(materials["Blush"])

    bpy.ops.mesh.primitive_uv_sphere_add(radius=0.075, location=(0.20, 0.29, 2.83))
    blush_r = bpy.context.object
    blush_r.scale = (1.0, 0.35, 0.8)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    blush_r.data.materials.append(materials["Blush"])

    # Labios pintados de muñeca antigua
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0, 0.34, 2.74))
    lips = bpy.context.object
    lips.scale = (0.09, 0.03, 0.04)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    lips.data.materials.append(materials["Blush"])

    # Grieta visible en la porcelana (mejilla derecha y frente)
    bpy.ops.mesh.primitive_cylinder_add(radius=0.008, depth=0.18, location=(0.23, 0.26, 3.00))
    crack1 = bpy.context.object
    crack1.rotation_euler = (math.radians(15), math.radians(25), math.radians(45))
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
    crack1.data.materials.append(materials["Cracks"])

    bpy.ops.mesh.primitive_cylinder_add(radius=0.007, depth=0.12, location=(0.19, 0.24, 2.90))
    crack2 = bpy.context.object
    crack2.rotation_euler = (math.radians(10), math.radians(-30), math.radians(-15))
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
    crack2.data.materials.append(materials["Cracks"])

    # Cabello de lana de muñeca de trapo (RagHair)
    bpy.ops.mesh.primitive_uv_sphere_add(segments=20, ring_count=14, radius=0.42, location=(0, -0.08, 3.08))
    hair_top = bpy.context.object
    hair_top.scale = (1.05, 0.95, 0.85)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    hair_top.data.materials.append(materials["RagHair"])

    # Coletas / bucles laterales de lana
    bpy.ops.mesh.primitive_cylinder_add(radius=0.12, depth=0.65, location=(-0.38, -0.04, 2.78))
    pigtail_l = bpy.context.object
    pigtail_l.rotation_euler = (math.radians(-10), math.radians(-15), 0)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
    pigtail_l.data.materials.append(materials["RagHair"])

    bpy.ops.mesh.primitive_cylinder_add(radius=0.12, depth=0.65, location=(0.38, -0.04, 2.78))
    pigtail_r = bpy.context.object
    pigtail_r.rotation_euler = (math.radians(-10), math.radians(15), 0)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
    pigtail_r.data.materials.append(materials["RagHair"])

    # Gran lazo de tela rosa en la cima de la cabeza (marca cenital visible desde 60°)
    bpy.ops.mesh.primitive_torus_add(major_radius=0.18, minor_radius=0.045, location=(-0.14, 0.0, 3.38))
    bow_l = bpy.context.object
    bow_l.rotation_euler = (math.radians(-25), 0, math.radians(-35))
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
    bow_l.data.materials.append(materials["Dress"])

    bpy.ops.mesh.primitive_torus_add(major_radius=0.18, minor_radius=0.045, location=(0.14, 0.0, 3.38))
    bow_r = bpy.context.object
    bow_r.rotation_euler = (math.radians(-25), 0, math.radians(35))
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
    bow_r.data.materials.append(materials["Dress"])

    bpy.ops.mesh.primitive_uv_sphere_add(radius=0.08, location=(0, 0.0, 3.38))
    bow_knot = bpy.context.object
    bow_knot.data.materials.append(materials["ApronCream"])

    # Unir piezas de cabeza y cabello al objeto Head
    bpy.context.view_layer.objects.active = head_mesh
    for part in [blush_l, blush_r, lips, crack1, crack2, hair_top, pigtail_l, pigtail_r, bow_l, bow_r, bow_knot]:
        part.select_set(True)
    head_mesh.select_set(True)
    bpy.ops.object.join()
    head_obj = bpy.context.object
    set_pivot(head_obj, (0.0, 0.0, 2.55))

    # =========================================================================
    # 4. EYES: Ojos oscuros de muñeca separados (Z=2.92m, mirando hacia +Y)
    # Pivote en el cuello (0, 0, 2.55)
    # =========================================================================
    # Ojo Izquierdo (-X)
    bpy.ops.mesh.primitive_uv_sphere_add(segments=16, ring_count=12, radius=0.095, location=(-0.16, 0.32, 2.95))
    eye_l = bpy.context.object
    eye_l.name = "Eyes"
    eye_l.data.materials.append(materials["Dark"])

    # Ojo Derecho (+X, 8% mayor y más caído, estilo Noah)
    bpy.ops.mesh.primitive_uv_sphere_add(segments=16, ring_count=12, radius=0.103, location=(0.16, 0.31, 2.92))
    eye_r = bpy.context.object
    eye_r.data.materials.append(materials["Dark"])

    # Cejas / pestañas pintadas oscuras
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(-0.16, 0.33, 3.07))
    brow_l = bpy.context.object
    brow_l.scale = (0.12, 0.02, 0.015)
    brow_l.rotation_euler = (0, 0, math.radians(12))
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    brow_l.data.materials.append(materials["Dark"])

    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0.16, 0.32, 3.05))
    brow_r = bpy.context.object
    brow_r.scale = (0.12, 0.02, 0.015)
    brow_r.rotation_euler = (0, 0, math.radians(-10))
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    brow_r.data.materials.append(materials["Dark"])

    # Brillos fijos de los ojos
    bpy.ops.mesh.primitive_uv_sphere_add(radius=0.025, location=(-0.14, 0.40, 2.98))
    shine_l = bpy.context.object
    shine_l.data.materials.append(materials["ApronCream"])

    bpy.ops.mesh.primitive_uv_sphere_add(radius=0.027, location=(0.18, 0.39, 2.95))
    shine_r = bpy.context.object
    shine_r.data.materials.append(materials["ApronCream"])

    bpy.context.view_layer.objects.active = eye_l
    for part in [eye_r, brow_l, brow_r, shine_l, shine_r]:
        part.select_set(True)
    eye_l.select_set(True)
    bpy.ops.object.join()
    eyes_obj = bpy.context.object
    set_pivot(eyes_obj, (0.0, 0.0, 2.55))

    # =========================================================================
    # 5. ARM_L: Brazo largo izquierdo de trapo con mano acolchada (-X)
    # Articulación en hombro izquierdo (-0.48, 0.0, 2.45)
    # =========================================================================
    bpy.ops.mesh.primitive_uv_sphere_add(radius=0.18, location=(-0.52, 0.0, 2.42))
    sleeve_l = bpy.context.object
    sleeve_l.name = "ArmL"
    sleeve_l.data.materials.append(materials["Dress"])

    # Miembro alargado extendiéndose hacia +Y (frente)
    bpy.ops.mesh.primitive_cylinder_add(radius=0.085, depth=1.15, location=(-0.72, 0.45, 2.15))
    limb_l = bpy.context.object
    limb_l.rotation_euler = (math.radians(-55), math.radians(-25), math.radians(20))
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
    limb_l.data.materials.append(materials["ApronCream"])

    # Puño de encaje
    bpy.ops.mesh.primitive_torus_add(major_radius=0.095, minor_radius=0.03, location=(-0.88, 0.85, 1.88))
    cuff_l = bpy.context.object
    cuff_l.rotation_euler = (math.radians(-55), math.radians(-25), math.radians(20))
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
    cuff_l.data.materials.append(materials["Dress"])

    # Mano acolchada de muñeca de trapo
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(-0.98, 1.05, 1.78))
    palm_l = bpy.context.object
    palm_l.scale = (0.28, 0.22, 0.12)
    palm_l.rotation_euler = (math.radians(-45), math.radians(-30), math.radians(15))
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    palm_l.data.materials.append(materials["ApronCream"])

    # Dedos acolchados curvados hacia adentro (para envolver a Noah)
    for i, f_ang in enumerate([-0.10, -0.03, 0.04, 0.11]):
        bpy.ops.mesh.primitive_cylinder_add(radius=0.032, depth=0.28, location=(-1.02 + f_ang, 1.22, 1.72))
        finger = bpy.context.object
        finger.rotation_euler = (math.radians(-65), math.radians(-25), math.radians(-i*5))
        bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
        finger.data.materials.append(materials["ApronCream"])
        bpy.context.view_layer.objects.active = palm_l
        finger.select_set(True)
        palm_l.select_set(True)
        bpy.ops.object.join()

    # Unir mano, puño y extremidad al ArmL
    bpy.context.view_layer.objects.active = sleeve_l
    for part in [limb_l, cuff_l, palm_l]:
        part.select_set(True)
    sleeve_l.select_set(True)
    bpy.ops.object.join()
    arm_l_obj = bpy.context.object
    set_pivot(arm_l_obj, (-0.48, 0.0, 2.45))

    # =========================================================================
    # 6. ARM_R: Brazo largo derecho de trapo con mano acolchada (+X)
    # Articulación en hombro derecho (0.48, 0.0, 2.45)
    # =========================================================================
    bpy.ops.mesh.primitive_uv_sphere_add(radius=0.18, location=(0.52, 0.0, 2.42))
    sleeve_r = bpy.context.object
    sleeve_r.name = "ArmR"
    sleeve_r.data.materials.append(materials["Dress"])

    bpy.ops.mesh.primitive_cylinder_add(radius=0.085, depth=1.15, location=(0.72, 0.45, 2.15))
    limb_r = bpy.context.object
    limb_r.rotation_euler = (math.radians(-55), math.radians(25), math.radians(-20))
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
    limb_r.data.materials.append(materials["ApronCream"])

    bpy.ops.mesh.primitive_torus_add(major_radius=0.095, minor_radius=0.03, location=(0.88, 0.85, 1.88))
    cuff_r = bpy.context.object
    cuff_r.rotation_euler = (math.radians(-55), math.radians(25), math.radians(-20))
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
    cuff_r.data.materials.append(materials["Dress"])

    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0.98, 1.05, 1.78))
    palm_r = bpy.context.object
    palm_r.scale = (0.28, 0.22, 0.12)
    palm_r.rotation_euler = (math.radians(-45), math.radians(30), math.radians(-15))
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    palm_r.data.materials.append(materials["ApronCream"])

    for i, f_ang in enumerate([-0.11, -0.04, 0.03, 0.10]):
        bpy.ops.mesh.primitive_cylinder_add(radius=0.032, depth=0.28, location=(1.02 + f_ang, 1.22, 1.72))
        finger = bpy.context.object
        finger.rotation_euler = (math.radians(-65), math.radians(25), math.radians(i*5))
        bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
        finger.data.materials.append(materials["ApronCream"])
        bpy.context.view_layer.objects.active = palm_r
        finger.select_set(True)
        palm_r.select_set(True)
        bpy.ops.object.join()

    bpy.context.view_layer.objects.active = sleeve_r
    for part in [limb_r, cuff_r, palm_r]:
        part.select_set(True)
    sleeve_r.select_set(True)
    bpy.ops.object.join()
    arm_r_obj = bpy.context.object
    set_pivot(arm_r_obj, (0.48, 0.0, 2.45))

    # =========================================================================
    # Suavizado de todas las partes oficiales
    # =========================================================================
    parts = [base_obj, body_obj, head_obj, eyes_obj, arm_l_obj, arm_r_obj]
    for p in parts:
        bpy.context.view_layer.objects.active = p
        p.select_set(True)
        bpy.ops.object.shade_smooth()

    return parts


def setup_preview_scene(parts):
    scene = bpy.context.scene
    preview_col = bpy.data.collections.new("Preview")
    scene.collection.children.link(preview_col)

    # Suelo oscuro de prueba de 20x20 m
    mat_floor = bpy.data.materials.new("PreviewFloor")
    mat_floor.use_nodes = True
    bsdf = mat_floor.node_tree.nodes.get("Principled BSDF")
    if bsdf:
        bsdf.inputs["Base Color"].default_value = (0.12, 0.10, 0.13, 1.0)
        bsdf.inputs["Roughness"].default_value = 0.85
    mat_floor.diffuse_color = (0.12, 0.10, 0.13, 1.0)

    bpy.ops.mesh.primitive_plane_add(size=25.0, location=(0, 0, -0.002))
    floor_obj = bpy.context.object
    floor_obj.name = "Floor"
    floor_obj.data.materials.append(mat_floor)
    if floor_obj.name in scene.collection.objects:
        scene.collection.objects.unlink(floor_obj)
    if floor_obj.name not in preview_col.objects:
        preview_col.objects.link(floor_obj)

    # Luces
    sun_key = bpy.data.lights.new("SunKey", type='SUN')
    sun_key.energy = 3.5
    sun_key_obj = bpy.data.objects.new("SunKey", sun_key)
    sun_key_obj.rotation_euler = (math.radians(52), 0, math.radians(-28))
    preview_col.objects.link(sun_key_obj)

    sun_fill = bpy.data.lights.new("SunFill", type='SUN')
    sun_fill.energy = 1.6
    sun_fill_obj = bpy.data.objects.new("SunFill", sun_fill)
    sun_fill_obj.rotation_euler = (math.radians(60), 0, math.radians(145))
    preview_col.objects.link(sun_fill_obj)

    sun_top = bpy.data.lights.new("SunTop", type='SUN')
    sun_top.energy = 1.2
    sun_top_obj = bpy.data.objects.new("SunTop", sun_top)
    sun_top_obj.rotation_euler = (0, 0, 0)
    preview_col.objects.link(sun_top_obj)

    # Configuración de render Workbench / EEVEE
    scene.render.engine = 'BLENDER_EEVEE'
    scene.render.resolution_x = RENDER_SIZE
    scene.render.resolution_y = RENDER_SIZE
    scene.render.resolution_percentage = 100
    scene.render.film_transparent = False
    scene.render.image_settings.file_format = 'PNG'
    scene.render.image_settings.color_mode = 'RGB'
    scene.view_settings.view_transform = 'Standard'

    # 3 Cámaras: Frente, Cámara del juego (60°), Perfil
    target = Vector((0.0, 0.0, 1.5))

    def make_cam(name, loc, target_pt, ortho=False, ortho_scale=4.5, fov_deg=55.0):
        cdata = bpy.data.cameras.new(name)
        if ortho:
            cdata.type = 'ORTHO'
            cdata.ortho_scale = ortho_scale
        else:
            cdata.type = 'PERSP'
            cdata.angle = math.radians(fov_deg)
        cobj = bpy.data.objects.new(name, cdata)
        cobj.location = loc
        direction = target_pt - Vector(loc)
        rot_quat = direction.to_track_quat('-Z', 'Y')
        cobj.rotation_euler = rot_quat.to_euler()
        preview_col.objects.link(cobj)
        return cobj

    # Frente mira desde +Y hacia el personaje
    cam_front = make_cam("CamFront", (0.0, 7.5, 1.5), target, ortho=True, ortho_scale=4.2)
    # Cámara de juego inclinada 60° (desde +Y arriba)
    pitch_rad = math.radians(60)
    dist = 7.5
    cam_game_loc = (0.0, dist * math.cos(pitch_rad), dist * math.sin(pitch_rad) + 0.8)
    cam_game = make_cam("CamGame", cam_game_loc, Vector((0.0, 0.0, 1.2)), ortho=False, fov_deg=50.0)
    # Perfil mira desde +X
    cam_side = make_cam("CamSide", (7.5, 0.0, 1.5), target, ortho=True, ortho_scale=4.2)

    return [("1_frente", cam_front), ("2_camara_juego_60deg", cam_game), ("3_perfil", cam_side)]


def render_preview_sheet(cameras, output_path):
    scene = bpy.context.scene
    temp_dir = tempfile.mkdtemp(prefix="mother_preview_")
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

    # Concatenar horizontalmente las 3 vistas
    sheet = np.concatenate(panels, axis=1)
    out = bpy.data.images.new("mother_preview", sheet.shape[1], sheet.shape[0], alpha=False)
    out.pixels.foreach_set(sheet.ravel())
    out.filepath_raw = str(output_path)
    out.file_format = 'PNG'
    out.save()
    bpy.data.images.remove(out)


def main():
    print("[the_mother] Iniciando modelado oficial de The Mother...")
    clean_scene()
    materials = make_materials()
    parts = build_the_mother(materials)

    # Validar partes
    part_names = [p.name for p in parts]
    print(f"[the_mother] Partes construidas ({len(parts)}): {', '.join(part_names)}")
    expected_parts = {"Base", "Body", "Head", "Eyes", "ArmL", "ArmR"}
    assert set(part_names) == expected_parts, f"Faltan partes o no coinciden: {set(part_names)} vs {expected_parts}"

    # Exportar GLB oficial en assets/models/the_mother.glb
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
    print(f"[the_mother] Exportado GLB oficial -> {GLB_PATH}")

    # Configurar y renderizar vista previa de 3 paneles
    cameras = setup_preview_scene(parts)
    render_preview_sheet(cameras, PREVIEW_PATH)
    print(f"[the_mother] Vista previa (3 vistas) -> {PREVIEW_PATH}")

    # Guardar archivo .blend oficial en assets/source/blender/the_mother.blend
    bpy.context.preferences.filepaths.save_version = 0  # sin .blend1
    bpy.ops.wm.save_as_mainfile(filepath=str(BLEND_PATH), compress=True)
    print(f"[the_mother] Archivo .blend oficial -> {BLEND_PATH}")

    # Copiar script fuente a assets/source/blender/the_mother.py
    this_script = Path(__file__).resolve()
    shutil.copy2(this_script, PY_PATH)
    print(f"[the_mother] Script python fuente guardado en -> {PY_PATH}")

    # Copiar copia completa a la carpeta de entrega de David
    shutil.copy2(GLB_PATH, DRIVE_DIR / "the_mother.glb")
    shutil.copy2(BLEND_PATH, DRIVE_DIR / "the_mother.blend")
    shutil.copy2(PY_PATH, DRIVE_DIR / "the_mother.py")
    shutil.copy2(PREVIEW_PATH, DRIVE_DIR / "the_mother_preview.png")
    print(f"[the_mother] Copia de entrega guardada en carpeta de David -> {DRIVE_DIR}")

    print("[the_mother] ¡Completado con éxito!")


if __name__ == "__main__":
    main()
