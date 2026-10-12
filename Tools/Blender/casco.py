# blender -b -P casco.py -- <F1_2026.fbx> <salida.fbx> <render.png>
# Añade al F1_2026 un casco (carcasa ovalada) y su visera, sentados en la cabina, y exporta
# el coche completo. El coche mide 3,39 unidades = 5,63 m: 1 unidad = 1,66 m.
import bpy, sys
from mathutils import Vector

coche, salida, render = sys.argv[sys.argv.index("--") + 1:][:3]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=coche)
mallas_coche = [o for o in bpy.context.scene.objects if o.type == "MESH"]

M = 1 / 1.66                     # un metro en unidades del modelo
cy, cz = 0.06, 0.42              # centro del casco: tras el volante (y = 0,355), asomando de la cabina

bpy.ops.mesh.primitive_uv_sphere_add(segments=28, ring_count=14, radius=1, location=(0, cy, cz))
casco = bpy.context.active_object
casco.name = casco.data.name = "Casco"
casco.scale = (0.14 * M, 0.165 * M, 0.15 * M)          # 28 x 33 x 30 cm
bpy.ops.object.transform_apply(scale=True)
bpy.ops.object.shade_smooth()

# Visera: un óvalo aplastado que asoma por delante del casco (el morro mira a +Y).
bpy.ops.mesh.primitive_uv_sphere_add(segments=24, ring_count=12, radius=1, location=(0, cy + 0.075 * M, cz + 0.015 * M))
visera = bpy.context.active_object
visera.name = visera.data.name = "Visera"
visera.scale = (0.12 * M, 0.11 * M, 0.045 * M)
bpy.ops.object.transform_apply(scale=True)
bpy.ops.object.shade_smooth()

print("CASCO", len(casco.data.vertices), "+", len(visera.data.vertices), "vértices")

esc = bpy.context.scene
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); esc.collection.objects.link(cam); esc.camera = cam
objetivo = Vector((0, cy, cz))
for nombre, desde in (("tras", Vector((0.9, -1.4, 0.7))), ("frente", Vector((0.7, 1.6, 0.6)))):
    cam.location = objetivo + desde
    cam.rotation_euler = (objetivo - cam.location).to_track_quat("-Z", "Y").to_euler()
    cam.data.lens = 50; cam.data.clip_start = 0.01
    esc.render.engine = "BLENDER_WORKBENCH"
    esc.display.shading.light = "STUDIO"; esc.display.shading.color_type = "OBJECT"
    for o in mallas_coche: o.color = (0.9, 0.35, 0.1, 1)
    casco.color = (0.15, 0.3, 0.9, 1); visera.color = (0.03, 0.03, 0.04, 1)
    esc.render.resolution_x, esc.render.resolution_y = 800, 500
    esc.render.filepath = render.replace(".png", f"_{nombre}.png")
    bpy.ops.render.render(write_still=True)

bpy.ops.object.select_all(action="SELECT")
for o in bpy.context.scene.objects:
    if o.type == "CAMERA": o.select_set(False)
bpy.ops.export_scene.fbx(filepath=salida, use_selection=True, apply_unit_scale=True, mesh_smooth_type="FACE")
print("EXPORTADO", salida)
