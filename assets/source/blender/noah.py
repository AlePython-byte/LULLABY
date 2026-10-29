"""Noah, el protagonista: una figurita de juguete (plástico/arcilla) tierna pero ligeramente inquietante.

Se ejecuta en segundo plano, con poca memoria (Workbench, 800x800, nunca Cycles):

    "C:/Program Files/Blender Foundation/Blender 5.2/blender.exe" --background --python assets/source/blender/noah.py

Salidas:
    assets/models/noah.glb                    el modelo para Godot
    assets/source/blender/noah.blend          la escena, para abrirla y retocarla a mano
    assets/source/blender/noah_preview.png    tres vistas: de frente | cámara del juego | de perfil

Construcción: solo primitivas (esferas, conos y toros de bmesh) y colores planos, sin texturas. Cada parte es un objeto
con nombre y el ORIGEN de cada objeto está en su articulación, para poder animarlo por partes en Godot:

    Noah (Empty, en los pies)
    ├── Body        pivote en la cintura
    │   ├── Head    pivote en el cuello
    │   │   ├── EyeL, EyeR, Hat, Pacifier, FacingMark
    │   ├── ArmL, ArmR   pivote en los hombros
    ├── LegL, LegR  pivote en las caderas

Convención de ejes (se comprueba en Godot con tests/PlayerVisualTest, no se da por supuesta):
    En Blender Noah MIRA HACIA +Y y el eje vertical es Z. El exportador glTF (+Y arriba) convierte (x, y, z) -> (x, z, -y),
    así que en Godot mira hacia -Z, que es hacia donde apunta PlayerController con yaw 0. "L" es el lado -X (izquierda de
    Noah) y "R" el +X.
Medidas: unos 0.9 x 0.9 m de huella y ~1.05 m de alto, con el origen en los pies (z = 0), para que dentro del Player
(cuyo centro está a 0.5 m del suelo) los pies queden en y = 0.
"""

import math
import os
import tempfile

import bmesh
import bpy
import numpy as np
from mathutils import Euler, Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
GLB_PATH = os.path.join(ROOT, "assets", "models", "noah.glb")
BLEND_PATH = os.path.join(HERE, "noah.blend")
PREVIEW_PATH = os.path.join(HERE, "noah_preview.png")

RENDER_SIZE = 800

# Paleta pastel (sRGB). Los colores del juego son planos: sin texturas todavía.
PALETTE = {
    "Skin": "#F8D9CB",
    "Blush": "#F2A3A3",
    "Onesie": "#BFE3D0",
    "Diaper": "#FFF3E6",
    "Bootie": "#FFE9A8",
    "Hat": "#C9B8EE",
    "HatBrim": "#FFF1F5",
    "Mark": "#FF8F7E",
    "Eye": "#1B1530",
    "Shine": "#FFFFFF",
    "PacShield": "#A9D2F2",
    "PacRing": "#F7A8C8",
}

# Medidas en metros (Z arriba, Noah mira hacia +Y).
HIP_Z = 0.21
WAIST_Z = 0.20
NECK_Z = 0.52
SHOULDER_Z = 0.47
HEAD_CENTER = Vector((0.0, 0.0, 0.70))


# --------------------------------------------------------------------------------------------------------------------
# Materiales
# --------------------------------------------------------------------------------------------------------------------

def srgb_to_linear(value):
    return value / 12.92 if value <= 0.04045 else ((value + 0.055) / 1.055) ** 2.4


def make_materials():
    materials = {}
    for name, hex_color in PALETTE.items():
        srgb = [int(hex_color[i:i + 2], 16) / 255.0 for i in (1, 3, 5)]
        linear = tuple(srgb_to_linear(c) for c in srgb)
        material = bpy.data.materials.new(name)
        material.use_nodes = True
        bsdf = material.node_tree.nodes["Principled BSDF"]
        bsdf.inputs["Base Color"].default_value = (*linear, 1.0)
        bsdf.inputs["Roughness"].default_value = 0.45  # plástico mate
        bsdf.inputs["Metallic"].default_value = 0.0
        material.diffuse_color = (*linear, 1.0)  # lo que dibuja Workbench
        materials[name] = material
    return materials


# --------------------------------------------------------------------------------------------------------------------
# Primitivas (cada una añade geometría a un bmesh y devuelve sus vértices)
# --------------------------------------------------------------------------------------------------------------------

def _rotation(rot):
    if rot is None:
        return Matrix.Identity(4)
    if isinstance(rot, Euler):
        return rot.to_matrix().to_4x4()
    return rot.to_matrix().to_4x4()


def ellipsoid(center, radii, rot=None, pre=None, segments=32, rings=16, keep_above_ring=None):
    """Esfera escalada. keep_above_ring = k conserva solo desde el anillo k hacia el polo norte (una cúpula)."""

    def build(bm):
        pre_m = pre if pre is not None else Matrix.Identity(4)
        matrix = pre_m @ Matrix.Translation(center) @ _rotation(rot) @ Matrix.Diagonal((*radii, 1.0))
        verts = bmesh.ops.create_uvsphere(bm, u_segments=segments, v_segments=rings, radius=1.0, matrix=matrix)["verts"]
        if keep_above_ring is not None:
            z_cut = center[2] + radii[2] * math.cos(keep_above_ring * math.pi / rings) - 1e-4
            inverse = pre_m.inverted()
            below = [v for v in verts if (inverse @ v.co).z < z_cut]
            bmesh.ops.delete(bm, geom=below, context="VERTS")
            verts = [v for v in verts if v.is_valid]
        return verts

    return build


def dome_arrow(dome_center, dome_radii, pre, offset=0.012, tail_y=-0.12, neck_y=0.08, tip_y=0.285):
    """Flecha plana pegada a la cúpula del gorro, de atrás hacia delante (+Y): un cuerpo estrecho y una punta ancha.
    Sigue la curva de la cúpula (a `offset` metros de su superficie) y apunta hacia donde mira Noah, así que se ve desde
    arriba aunque la figura sea diminuta. Es una lámina; el modificador Solidify del objeto le da grosor."""
    cx, cy, cz = dome_center
    rx, ry, rz = dome_radii

    def surface_z(x, y):
        t = 1.0 - (x / rx) ** 2 - ((y - cy) / ry) ** 2
        return cz + rz * math.sqrt(max(t, 0.0)) + offset

    def build(bm):
        verts = []

        def strip(y0, y1, half0, half1, rows, cols=6):
            first = len(verts)
            for r in range(rows + 1):
                u = r / rows
                y = y0 + (y1 - y0) * u
                half = half0 + (half1 - half0) * u
                for c in range(cols + 1):
                    x = -half + 2.0 * half * c / cols
                    verts.append(bm.verts.new(pre @ Vector((x, y, surface_z(x, y)))))
            for r in range(rows):
                for c in range(cols):
                    a = first + r * (cols + 1) + c
                    b = a + cols + 1
                    bm.faces.new((verts[a], verts[a + 1], verts[b + 1], verts[b]))  # normal hacia +Z

        strip(tail_y, neck_y, 0.036, 0.036, 8)   # el cuerpo de la flecha
        strip(neck_y, tip_y, 0.10, 0.004, 8)     # la punta, ancha y afilada hacia delante
        return verts

    return build


def torus(center, major, minor, rot=None, pre=None, scale=(1.0, 1.0, 1.0), major_segments=40, minor_segments=14):
    """Toro con el eje en Z local."""

    def build(bm):
        pre_m = pre if pre is not None else Matrix.Identity(4)
        matrix = pre_m @ Matrix.Translation(center) @ _rotation(rot) @ Matrix.Diagonal((*scale, 1.0))
        verts = []
        for i in range(major_segments):
            a = 2.0 * math.pi * i / major_segments
            for j in range(minor_segments):
                b = 2.0 * math.pi * j / minor_segments
                radius = major + minor * math.cos(b)
                verts.append(bm.verts.new(matrix @ Vector((radius * math.cos(a), radius * math.sin(a), minor * math.sin(b)))))
        for i in range(major_segments):
            for j in range(minor_segments):
                bm.faces.new((verts[i * minor_segments + j],
                              verts[((i + 1) % major_segments) * minor_segments + j],
                              verts[((i + 1) % major_segments) * minor_segments + (j + 1) % minor_segments],
                              verts[i * minor_segments + (j + 1) % minor_segments]))
        return verts

    return build


# --------------------------------------------------------------------------------------------------------------------
# Partes
# --------------------------------------------------------------------------------------------------------------------

class Rig:
    def __init__(self, materials, collection):
        self.materials = materials
        self.collection = collection
        self.objects = []

    def empty(self, name, location):
        obj = bpy.data.objects.new(name, None)
        obj.empty_display_type = "PLAIN_AXES"
        obj.empty_display_size = 0.1
        obj.location = location
        self.collection.objects.link(obj)
        self.objects.append(obj)
        return obj

    def part(self, name, pivot, items, parent=None, recalc_normals=True, solidify=0.0):
        """Un objeto con el origen en `pivot`. items = [(material, constructor), ...].
        recalc_normals=False respeta el orden de caras de las láminas abiertas; solidify > 0 añade un modificador
        Solidify de ese grosor (hacia dentro, contra el sentido de las normales)."""
        pivot = Vector(pivot)
        bm = bmesh.new()
        used = []
        for material, build in items:
            if material not in used:
                used.append(material)
            verts = build(bm)
            vert_set = set(verts)
            for face in {f for v in verts for f in v.link_faces}:
                if all(v in vert_set for v in face.verts):
                    face.material_index = used.index(material)

        if recalc_normals:
            bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        bmesh.ops.translate(bm, verts=bm.verts, vec=-pivot)
        for face in bm.faces:
            face.smooth = True

        mesh = bpy.data.meshes.new(name)
        bm.to_mesh(mesh)
        bm.free()
        for material in used:
            mesh.materials.append(self.materials[material])

        obj = bpy.data.objects.new(name, mesh)
        obj.location = pivot
        self.collection.objects.link(obj)
        self.objects.append(obj)
        if solidify > 0.0:
            modifier = obj.modifiers.new("Grosor", "SOLIDIFY")
            modifier.thickness = solidify
            modifier.offset = -1.0
        if parent is not None:
            # Todas las transformaciones son traslaciones puras, así que el mundo de un objeto es su `location`.
            obj.parent = parent
            obj.matrix_parent_inverse = Matrix.Translation(parent.location).inverted()
        return obj


def side(sign):
    return "R" if sign > 0 else "L"


def build_noah(materials, collection):
    rig = Rig(materials, collection)
    root = rig.empty("Noah", (0.0, 0.0, 0.0))

    # Cuerpo: mono verde menta con un pañal.
    body = rig.part("Body", (0.0, 0.0, WAIST_Z), [
        ("Onesie", ellipsoid((0.0, 0.0, 0.34), (0.25, 0.21, 0.22))),
        ("Diaper", ellipsoid((0.0, -0.01, 0.21), (0.225, 0.20, 0.115))),
    ], parent=root)

    # Cabeza grande: cara, orejas y mofletes.
    head_items = [
        ("Skin", ellipsoid(HEAD_CENTER, (0.31, 0.29, 0.27))),
        ("Skin", ellipsoid(HEAD_CENTER + Vector((0.305, -0.02, -0.01)), (0.04, 0.028, 0.05))),
        ("Skin", ellipsoid(HEAD_CENTER + Vector((-0.305, -0.02, -0.01)), (0.04, 0.028, 0.05))),
        ("Blush", ellipsoid(HEAD_CENTER + Vector((0.19, 0.19, -0.10)), (0.065, 0.03, 0.04))),
        ("Blush", ellipsoid(HEAD_CENTER + Vector((-0.19, 0.19, -0.10)), (0.065, 0.03, 0.04))),
    ]
    head = rig.part("Head", (0.0, 0.0, NECK_Z), head_items, parent=body)

    # Ojos enormes y oscuros, muy separados; el izquierdo un poco más grande (lo justo para inquietar).
    for sign in (-1, 1):
        scale = 1.08 if sign < 0 else 1.0
        eye_center = Vector((0.132 * sign, 0.245, 0.69 - (0.01 if sign < 0 else 0.0)))
        radii = (0.092 * scale, 0.062 * scale, 0.108 * scale)
        shine_center = eye_center + Vector((-0.03 * scale, 0.054, 0.04 * scale))
        rig.part("Eye" + side(sign), eye_center, [
            ("Eye", ellipsoid(eye_center, radii)),
            ("Shine", ellipsoid(shine_center, (0.02 * scale, 0.011, 0.022 * scale))),
        ], parent=head)

    # Gorrito de punto, ligeramente ladeado (rota alrededor del centro de la cabeza).
    tilt = (Matrix.Translation(HEAD_CENTER) @ Euler((math.radians(-3), math.radians(4), 0.0)).to_matrix().to_4x4()
            @ Matrix.Translation(-HEAD_CENTER))
    rim_z = HEAD_CENTER.z + 0.30 * math.cos(6 * math.pi / 16)
    dome_center = HEAD_CENTER + Vector((0.0, -0.005, 0.02))
    dome_radii = (0.325, 0.305, 0.30)
    rig.part("Hat", HEAD_CENTER, [
        ("Hat", ellipsoid(dome_center, dome_radii, pre=tilt, keep_above_ring=6)),
        ("HatBrim", torus((0.0, -0.005, rim_z + 0.02), 0.288, 0.031, pre=tilt, scale=(1.0, 0.94, 1.0))),
    ], parent=head)

    # La marca de dirección: una flecha coral sobre el gorrito que apunta hacia delante (+Y) y se ve desde arriba.
    rig.part("FacingMark", (0.0, 0.05, 1.03), [
        ("Mark", dome_arrow(dome_center, dome_radii, tilt)),
    ], parent=head, recalc_normals=False, solidify=0.014)

    # Chupete: escudo azul, anillo rosa de agarre y botón.
    mouth = Vector((0.0, 0.272, 0.555))
    rig.part("Pacifier", mouth, [
        ("PacShield", ellipsoid(mouth, (0.105, 0.016, 0.072))),
        ("PacRing", ellipsoid(mouth + Vector((0.0, 0.028, 0.0)), (0.03, 0.03, 0.03))),
        ("PacRing", torus(mouth + Vector((0.0, 0.06, 0.0)), 0.034, 0.0095, rot=Euler((math.radians(90), 0.0, 0.0)))),
    ], parent=head)

    # Brazos regordetes, algo separados y hacia delante, con manita.
    for sign in (-1, 1):
        shoulder = Vector((0.245 * sign, 0.0, SHOULDER_Z))
        direction = Vector((math.sin(math.radians(20)) * sign, math.sin(math.radians(15)), -math.cos(math.radians(20)))).normalized()
        rotation = Vector((0.0, 0.0, 1.0)).rotation_difference(direction)
        rig.part("Arm" + side(sign), shoulder, [
            ("Onesie", ellipsoid(shoulder + direction * 0.115, (0.068, 0.068, 0.125), rot=rotation)),
            ("Skin", ellipsoid(shoulder + direction * 0.245, (0.06, 0.06, 0.06))),
        ], parent=body)

    # Piernas cortas con botitas: el origen del objeto está en la cadera.
    for sign in (-1, 1):
        hip = Vector((0.115 * sign, 0.0, HIP_Z))
        rig.part("Leg" + side(sign), hip, [
            ("Onesie", ellipsoid((0.115 * sign, 0.0, 0.125), (0.082, 0.088, 0.115))),
            ("Bootie", ellipsoid((0.115 * sign, 0.03, 0.07), (0.098, 0.128, 0.07))),
        ], parent=root)

    return rig


# --------------------------------------------------------------------------------------------------------------------
# Exportación
# --------------------------------------------------------------------------------------------------------------------

def world_bounds(objects):
    low = Vector((1e9, 1e9, 1e9))
    high = Vector((-1e9, -1e9, -1e9))
    for obj in objects:
        if obj.type != "MESH":
            continue
        for corner in obj.bound_box:
            point = obj.matrix_world @ Vector(corner)
            low = Vector((min(low[i], point[i]) for i in range(3)))
            high = Vector((max(high[i], point[i]) for i in range(3)))
    return low, high


def export_glb(rig):
    bpy.context.view_layer.update()
    bpy.ops.object.select_all(action="DESELECT")
    for obj in rig.objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = rig.objects[0]
    os.makedirs(os.path.dirname(GLB_PATH), exist_ok=True)
    bpy.ops.export_scene.gltf(
        filepath=GLB_PATH,
        export_format="GLB",
        use_selection=True,
        export_apply=True,
        export_yup=True,
        export_cameras=False,
        export_lights=False,
        export_animations=False,
        export_materials="EXPORT",
    )


# --------------------------------------------------------------------------------------------------------------------
# Vista previa
# --------------------------------------------------------------------------------------------------------------------

def add_camera(collection, name, location, target, ortho_scale=None, fov=None):
    data = bpy.data.cameras.new(name)
    if ortho_scale is not None:
        data.type = "ORTHO"
        data.ortho_scale = ortho_scale
    else:
        data.angle = math.radians(fov)
    data.clip_end = 100.0
    camera = bpy.data.objects.new(name, data)
    camera.location = location
    camera.rotation_euler = (Vector(target) - Vector(location)).to_track_quat("-Z", "Y").to_euler()
    collection.objects.link(camera)
    return camera


def game_camera_location(distance, azimuth_degrees, pitch_degrees=60.0):
    """Posición de una cámara como la del juego (cabeceo de 60° hacia abajo) a `distance` metros del objetivo.
    Con azimut 0 está al sur (-Y) de Noah, que mira al norte (+Y): lo ve de espaldas, como en el juego al avanzar hacia
    arriba. Con 135° lo ve de frente y de lado (Noah avanzando en diagonal hacia la cámara)."""
    pitch = math.radians(pitch_degrees)
    azimuth = math.radians(azimuth_degrees)
    horizontal = distance * math.cos(pitch)
    return Vector((horizontal * math.sin(azimuth), -horizontal * math.cos(azimuth), distance * math.sin(pitch)))


def add_sun(collection, name, energy, euler_degrees, angle_degrees=10.0):
    data = bpy.data.lights.new(name, "SUN")
    data.energy = energy
    data.angle = math.radians(angle_degrees)
    obj = bpy.data.objects.new(name, data)
    obj.rotation_euler = Euler([math.radians(a) for a in euler_degrees])
    collection.objects.link(obj)
    return obj


def setup_preview_scene(rig):
    scene = bpy.context.scene
    preview = bpy.data.collections.new("Preview")
    scene.collection.children.link(preview)

    # Suelo oscuro como el del juego y un cuadrado de 1 x 1 m que marca la huella de Noah.
    floor_materials = {}
    for name, color in (("Floor", (0.18, 0.15, 0.15)), ("Footprint", (0.30, 0.26, 0.27))):
        material = bpy.data.materials.new("Preview" + name)
        material.use_nodes = True
        bsdf = material.node_tree.nodes["Principled BSDF"]
        bsdf.inputs["Base Color"].default_value = (*(srgb_to_linear(c) for c in color), 1.0)
        bsdf.inputs["Roughness"].default_value = 0.9
        material.diffuse_color = (*color, 1.0)
        floor_materials[name] = material

    for name, size, z, key in (("PreviewFloor", 80.0, -0.002, "Floor"), ("PreviewFootprint", 1.0, -0.001, "Footprint")):
        mesh = bpy.data.meshes.new(name)
        half = size / 2.0
        mesh.from_pydata([(-half, -half, z), (half, -half, z), (half, half, z), (-half, half, z)], [], [(0, 1, 2, 3)])
        mesh.materials.append(floor_materials[key])
        obj = bpy.data.objects.new(name, mesh)
        preview.objects.link(obj)

    # Fondo oscuro como el juego (la luz ambiente es baja) y tres luces de sol: principal, relleno y cenital.
    world = bpy.data.worlds.new("PreviewWorld")
    world.color = (0.10, 0.10, 0.14)
    scene.world = world
    add_sun(preview, "SunKey", 3.2, (-52.0, 0.0, 28.0))      # desde delante, arriba y a la derecha de Noah
    add_sun(preview, "SunFill", 1.3, (-60.0, 0.0, 215.0), 25.0)  # relleno suave desde atrás-izquierda
    add_sun(preview, "SunTop", 0.9, (0.0, 0.0, 0.0), 30.0)       # cenital, para que el gorro y la marca se lean arriba

    # EEVEE (nunca Cycles): poca memoria, 800x800 y pocas muestras.
    render = scene.render
    render.engine = "BLENDER_EEVEE"
    render.resolution_x = render.resolution_y = RENDER_SIZE
    render.resolution_percentage = 100
    render.film_transparent = False
    render.image_settings.file_format = "PNG"
    render.image_settings.color_mode = "RGB"
    scene.view_settings.view_transform = "Standard"
    scene.eevee.taa_render_samples = 32

    target = (0.0, 0.0, 0.5)
    cameras = [
        ("1_frente", add_camera(preview, "CamFront", (0.0, 5.0, 0.52), (0.0, 0.0, 0.52), ortho_scale=1.55)),
        ("2_camara_del_juego", add_camera(preview, "CamGame", game_camera_location(3.4, 135.0), (0.0, 0.0, 0.45), fov=60.0)),
        ("3_perfil", add_camera(preview, "CamSide", (5.0, 0.0, 0.52), (0.0, 0.0, 0.52), ortho_scale=1.55)),
    ]
    return cameras


def render_view(camera, path):
    scene = bpy.context.scene
    scene.camera = camera
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)


def render_preview(cameras, output_path):
    temp_dir = tempfile.mkdtemp(prefix="noah_preview_")
    panels = []
    for name, camera in cameras:
        path = os.path.join(temp_dir, name + ".png")
        render_view(camera, path)
        image = bpy.data.images.load(path)
        width, height = image.size
        pixels = np.empty(width * height * 4, dtype=np.float32)
        image.pixels.foreach_get(pixels)
        panels.append(pixels.reshape(height, width, 4))
        bpy.data.images.remove(image)
        os.remove(path)
    os.rmdir(temp_dir)

    sheet = np.concatenate(panels, axis=1)
    out = bpy.data.images.new("noah_preview", sheet.shape[1], sheet.shape[0], alpha=False)
    out.pixels.foreach_set(sheet.ravel())
    out.filepath_raw = output_path
    out.file_format = "PNG"
    out.save()
    bpy.data.images.remove(out)


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    collection = bpy.data.collections.new("Noah")
    bpy.context.scene.collection.children.link(collection)

    rig = build_noah(make_materials(), collection)
    bpy.context.view_layer.update()
    low, high = world_bounds(rig.objects)
    size = high - low
    radius = max(max(abs(low.x), abs(high.x)), max(abs(low.y), abs(high.y)))
    print(f"[noah] huella {size.x:.3f} x {size.y:.3f} m, alto {size.z:.3f} m, pies en z = {low.z:.4f}, radio máx. {radius:.3f} m")
    print("[noah] partes: " + ", ".join(o.name for o in rig.objects))

    export_glb(rig)
    print(f"[noah] exportado {GLB_PATH}")

    cameras = setup_preview_scene(rig)
    render_preview(cameras, PREVIEW_PATH)
    print(f"[noah] vista previa {PREVIEW_PATH}")

    bpy.context.preferences.filepaths.save_version = 0  # sin noah.blend1
    bpy.ops.wm.save_as_mainfile(filepath=BLEND_PATH, compress=True)
    print(f"[noah] escena guardada {BLEND_PATH}")


main()
