# blender -b -P Tools/Coche/monoplaza.py -- <f2002.fbx> <carpeta de salida> <render.png>
#
# El coche CC0 de la versión pública (f2002 de som_natalino): lo gira para que el morro mire a
# +Y, como el F1_2026 (así el juego lo trata igual: GiroModelo 180), le sienta un piloto con
# casco y visera en la cabina y exporta Monoplaza.fbx (sin piloto) y Monoplaza_piloto.fbx.
#
# Medido el 12-10-2026: el original mide 5,20 unidades con el morro hacia -Y; la toma de aire
# (lo más alto, Z 0,90) está entre Y 0,13 y 0,53 y la cabina justo delante. Un F2002 real mide
# 4,5 m, así que 1 m = 1,156 unidades: el casco se dimensiona con eso para que guarde la
# proporción con la cabina.
import bpy, sys, math, os
from mathutils import Vector, Matrix

fbx, carpeta, render = sys.argv[sys.argv.index("--") + 1:][:3]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=fbx)
coche = [o for o in bpy.context.scene.objects if o.type == "MESH"]

# Morro a +Y: medio giro alrededor de Z, aplicado a la geometría.
giro = Matrix.Rotation(math.pi, 4, "Z")
for o in coche:
    o.matrix_world = giro @ o.matrix_world
bpy.ops.object.select_all(action="DESELECT")
for o in coche: o.select_set(True)
bpy.context.view_layer.objects.active = coche[0]
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)


def exportar(nombre, objetos):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objetos: o.select_set(True)
    ruta = os.path.join(carpeta, nombre)
    bpy.ops.export_scene.fbx(filepath=ruta, use_selection=True, apply_unit_scale=True, mesh_smooth_type="FACE")
    print("EXPORTADO", ruta)


exportar("Monoplaza.fbx", coche)

M = 5.204 / 4.5                  # unidades por metro
cy = 0.0                         # delante del arco de seguridad (Y -0,18) y a 0,6 del volante (Y 0,6)
# Altura: el borde de los LATERALES de la cabina (|x| 0,12-0,4) junto a la cabeza; el casco asoma
# por encima. Midiendo en el centro se cogía el suelo de la cabina y el casco quedaba hundido.
borde = max((o.matrix_world @ v.co).z for o in coche for v in o.data.vertices
            if 0.12 < abs((o.matrix_world @ v.co).x) < 0.4 and -0.06 < (o.matrix_world @ v.co).y < 0.3)
cz = borde + 0.04 * M
print("BORDE DE CABINA", round(borde, 3), "-> centro del casco", round(cz, 3))

bpy.ops.mesh.primitive_uv_sphere_add(segments=28, ring_count=14, radius=1, location=(0, cy, cz))
casco = bpy.context.active_object
casco.name = casco.data.name = "Casco"
casco.scale = (0.14 * M, 0.165 * M, 0.15 * M)          # 28 x 33 x 30 cm
bpy.ops.object.transform_apply(scale=True)
bpy.ops.object.shade_smooth()

bpy.ops.mesh.primitive_uv_sphere_add(segments=24, ring_count=12, radius=1, location=(0, cy + 0.075 * M, cz + 0.015 * M))
visera = bpy.context.active_object
visera.name = visera.data.name = "Visera"
visera.scale = (0.12 * M, 0.11 * M, 0.045 * M)
bpy.ops.object.transform_apply(scale=True)
bpy.ops.object.shade_smooth()

# Foto de control, como en casco.py.
esc = bpy.context.scene
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); esc.collection.objects.link(cam); esc.camera = cam
objetivo = Vector((0, cy, cz))
for nombre, desde in (("tras", Vector((1.1, -1.7, 0.9))), ("lado", Vector((3.2, 0.3, 0.6)))):
    cam.location = objetivo + desde
    cam.rotation_euler = (objetivo - cam.location).to_track_quat("-Z", "Y").to_euler()
    cam.data.lens = 40; cam.data.clip_start = 0.01
    esc.render.engine = "BLENDER_WORKBENCH"
    esc.display.shading.light = "STUDIO"; esc.display.shading.color_type = "OBJECT"
    for o in coche: o.color = (0.9, 0.35, 0.1, 1)
    casco.color = (0.15, 0.3, 0.9, 1); visera.color = (0.03, 0.03, 0.04, 1)
    esc.render.resolution_x, esc.render.resolution_y = 800, 500
    esc.render.filepath = render.replace(".png", f"_{nombre}.png")
    bpy.ops.render.render(write_still=True)

exportar("Monoplaza_piloto.fbx", coche + [casco, visera])
