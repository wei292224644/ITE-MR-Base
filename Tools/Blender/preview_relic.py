"""Render previews of the generated relic so the fracture can be eyeballed.

The crack mask is wired into the crust material as gold emission, which is the
real check: the painted crack lines must land exactly on the piece boundaries
once the pieces are pulled apart.

    /Applications/Blender.app/Contents/MacOS/Blender --background \
        Assets/SacredRelicDemo/Generated/Models/SacredRelic_Fractured.blend \
        --python Tools/Blender/preview_relic.py -- \
        --mask Assets/SacredRelicDemo/Generated/Textures/T_SacredRelic_CrackMask.png \
        --out /tmp/relic_preview
"""

import argparse
import math
import os
import sys

import bpy
from mathutils import Vector

TARGET = Vector((0, 0, 0.45))


def parse_args():
    argv = sys.argv
    argv = argv[argv.index("--") + 1:] if "--" in argv else []
    p = argparse.ArgumentParser()
    p.add_argument("--out", required=True)
    p.add_argument("--mask", default="")
    p.add_argument("--res", type=int, default=900)
    return p.parse_args(argv)


def shell_pieces():
    return sorted((o for o in bpy.data.objects if o.name.startswith("Shell_Piece_")),
                  key=lambda o: o.name)


def stone_material(mat, colour, rough=0.82):
    mat.use_nodes = True
    bsdf = next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    bsdf.inputs["Base Color"].default_value = (*colour, 1.0)
    bsdf.inputs["Roughness"].default_value = rough
    return mat, bsdf


def wire_crack_emission(mask_path, gold=(1.0, 0.62, 0.18), strength=16.0):
    """Gold emission along cracks, gated by the mask's arrival channel.

    Red is the crack shape, green is when the fracture reaches that point, so
    raising the returned threshold grows the crack outward from the centre.
    Returns the threshold socket so the caller can animate it.
    """
    mat = bpy.data.materials.get("M_Shell_Outer")
    if mat is None or not mask_path or not os.path.exists(mask_path):
        return None
    _, bsdf = stone_material(mat, (0.085, 0.075, 0.062), rough=0.92)
    tree = mat.node_tree
    img = bpy.data.images.load(mask_path, check_existing=True)
    img.colorspace_settings.name = "Non-Color"
    bsdf.inputs["Emission Color"].default_value = (*gold, 1.0)

    tex = tree.nodes.new("ShaderNodeTexImage")
    tex.image = img
    tex.interpolation = "Cubic"
    sep = tree.nodes.new("ShaderNodeSeparateColor")
    tree.links.new(tex.outputs["Color"], sep.inputs["Color"])

    reveal = tree.nodes.new("ShaderNodeMath")
    reveal.operation = "LESS_THAN"
    reveal.inputs[1].default_value = 1.0
    tree.links.new(sep.outputs["Green"], reveal.inputs[0])

    gate = tree.nodes.new("ShaderNodeMath")
    gate.operation = "MULTIPLY"
    tree.links.new(sep.outputs["Red"], gate.inputs[0])
    tree.links.new(reveal.outputs["Value"], gate.inputs[1])

    scale = tree.nodes.new("ShaderNodeMath")
    scale.operation = "MULTIPLY"
    scale.inputs[1].default_value = strength
    tree.links.new(gate.outputs["Value"], scale.inputs[0])
    tree.links.new(scale.outputs["Value"], bsdf.inputs["Emission Strength"])
    return reveal.inputs[1]


def setup(res):
    scene = bpy.context.scene
    engine_prop = type(scene.render).bl_rna.properties["engine"]
    available = [i.identifier for i in engine_prop.enum_items]
    scene.render.engine = "BLENDER_EEVEE" if "BLENDER_EEVEE" in available else available[0]
    scene.render.resolution_x = res
    scene.render.resolution_y = res
    scene.view_settings.view_transform = "AgX" if hasattr(scene.view_settings, "view_transform") else "Standard"

    world = bpy.data.worlds.new("W")
    world.use_nodes = True
    for node in world.node_tree.nodes:
        if node.type == "BACKGROUND":
            node.inputs[0].default_value = (0.035, 0.038, 0.048, 1)
            node.inputs[1].default_value = 1.0
    scene.world = world

    for name, loc, energy, size in (("Key", (1.3, -2.0, 2.0), 120, 2.0),
                                    ("Fill", (-1.9, -1.4, 0.7), 45, 2.5),
                                    ("Rim", (0.3, 2.2, 1.7), 70, 1.5)):
        light = bpy.data.lights.new(name, "AREA")
        light.energy = energy
        light.size = size
        obj = bpy.data.objects.new(name, light)
        obj.location = loc
        obj.rotation_euler = (TARGET - Vector(loc)).to_track_quat("-Z", "Y").to_euler()
        scene.collection.objects.link(obj)

    cam_data = bpy.data.cameras.new("Cam")
    cam_data.lens = 55
    cam = bpy.data.objects.new("Cam", cam_data)
    scene.collection.objects.link(cam)
    scene.camera = cam
    return cam


def aim(cam, azimuth_deg, elevation_deg, distance):
    a, e = math.radians(azimuth_deg), math.radians(elevation_deg)
    offset = Vector((math.sin(a) * math.cos(e), -math.cos(a) * math.cos(e), math.sin(e))) * distance
    cam.location = TARGET + offset
    cam.rotation_euler = (TARGET - cam.location).to_track_quat("-Z", "Y").to_euler()


def set_spread(rest, spread, forward, lift, spin=0.0):
    """Scale pieces outward from the tablet centre and push them toward the viewer.

    Scaling by the centroid rather than a normalised direction is what makes the
    interior cracks open up too, not just the silhouette.
    """
    for obj, home in rest.items():
        obj.location = Vector((home.x * (1.0 + spread), home.y - forward,
                               0.45 + (home.z - 0.45) * (1.0 + spread) + lift))
        r = math.hypot(home.x, home.z - 0.45)
        obj.rotation_euler = (spin * (0.4 + r), spin * 0.7, spin * (0.3 - r))


def render(path):
    bpy.context.scene.render.filepath = path
    bpy.ops.render.render(write_still=True)


def main():
    args = parse_args()
    os.makedirs(args.out, exist_ok=True)

    root = bpy.data.objects.get("Relic_Root")
    if root:
        root.location = (0, 0, 0.45)

    stone_material(bpy.data.materials["M_Core"], (0.50, 0.46, 0.40), rough=0.72)
    stone_material(bpy.data.materials["M_Shell_Inner"], (0.42, 0.39, 0.34), rough=0.95)
    threshold = wire_crack_emission(args.mask)

    cam = setup(args.res)
    rest = {o: Vector(o.matrix_world.translation) for o in shell_pieces()}

    def set_threshold(value):
        if threshold is not None:
            threshold.default_value = value

    # Crack racing outward from the centre, tablet still sealed.
    for i, value in enumerate((0.0, 0.22, 0.48, 0.75, 1.0)):
        set_threshold(value)
        set_spread(rest, 0.0, 0.0, 0.0, 0.0)
        aim(cam, 0, 3, 2.05)
        render(os.path.join(args.out, f"grow_{i}_{int(value * 100):03d}"))

    set_threshold(1.0)
    stages = [
        ("stage_1_sealed", 0.0, 0.0, 0.0, 0.0, 0, 4, 2.05),
        ("stage_2_cracked", 0.010, 0.004, 0.0, 0.0, 24, 14, 2.05),
        ("stage_3_gold_seep", 0.030, 0.012, 0.0, 0.02, 24, 14, 2.05),
        ("stage_4_burst", 0.22, 0.10, 0.10, 0.45, 24, 14, 2.55),
    ]
    for name, spread, forward, lift, spin, az, el, dist in stages:
        set_spread(rest, spread, forward, lift, spin)
        aim(cam, az, el, dist)
        render(os.path.join(args.out, name))

    print("PREVIEW done", args.out)


if __name__ == "__main__":
    main()
