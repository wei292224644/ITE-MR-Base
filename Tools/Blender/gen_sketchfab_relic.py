"""Build shell/core + Voronoi crust from the open Sketchfab stele, for SacredRelicFracture.

Narrative: the decay shell (mud, grime, lichen, weathered crust) fractures and turns to
dust; the inscription core stays.

Run inside an open Blender scene that already contains Sketchfab_model, or headless:
    blender file.blend --python Tools/Blender/gen_sketchfab_relic.py -- \\
        --out Assets/SacredRelicDemo/Generated --height 1.2
"""

from __future__ import annotations

import argparse
import importlib.util
import json
import math
import os
import random
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector


def _load_shared():
    here = os.path.dirname(os.path.abspath(__file__))
    path = os.path.join(here, "gen_sacred_relic.py")
    spec = importlib.util.spec_from_file_location("gen_sacred_relic", path)
    mod = importlib.util.module_from_spec(spec)
    # Avoid running gen_sacred_relic.main when the module defines it under __main__.
    sys.modules["gen_sacred_relic"] = mod
    spec.loader.exec_module(mod)
    return mod


G = None  # filled in run()


def parse_args():
    argv = sys.argv
    argv = argv[argv.index("--") + 1:] if "--" in argv else []
    p = argparse.ArgumentParser()
    p.add_argument("--out", default="",
                   help="output dir; default Assets/SacredRelicDemo/Generated under the blend")
    p.add_argument("--height", type=float, default=0.0,
                   help="target stele height in metres; 0 keeps the original scale")
    p.add_argument("--crust", type=float, default=0.04, help="decay-shell thickness")
    p.add_argument("--cells", type=int, default=48)
    p.add_argument("--seed", type=int, default=20260728)
    p.add_argument("--seg-len", type=float, default=0.012)
    p.add_argument("--jag", type=float, default=0.014)
    p.add_argument("--mask", type=int, default=2048)
    p.add_argument("--crack-width", type=float, default=0.004)
    return p.parse_args(argv)


def under_sketchfab(obj):
    p = obj
    while p:
        if p.name == "Sketchfab_model":
            return True
        p = p.parent
    return False


def delete_object(obj):
    data = obj.data
    bpy.data.objects.remove(obj, do_unlink=True)
    if data is not None and getattr(data, "users", 1) == 0:
        if isinstance(data, bpy.types.Mesh):
            bpy.data.meshes.remove(data)


def clean_empty_sketchfab_meshes():
    removed = []
    for obj in list(bpy.data.objects):
        if obj.type != "MESH" or not under_sketchfab(obj):
            continue
        if len(obj.data.vertices) == 0:
            removed.append(obj.name)
            delete_object(obj)
    return removed


def world_bounds(obj):
    # Use live vertices — obj.bound_box can stay stale after in-place mesh edits.
    mat = obj.matrix_world
    verts = obj.data.vertices
    if not verts:
        return Vector((0, 0, 0)), Vector((0, 0, 0))
    first = mat @ verts[0].co
    mn = first.copy()
    mx = first.copy()
    for v in verts:
        p = mat @ v.co
        mn.x = min(mn.x, p.x); mn.y = min(mn.y, p.y); mn.z = min(mn.z, p.z)
        mx.x = max(mx.x, p.x); mx.y = max(mx.y, p.y); mx.z = max(mx.z, p.z)
    return mn, mx


def mesh_world_volume(obj):
    mn, mx = world_bounds(obj)
    size = mx - mn
    return max(0.0, size.x) * max(0.0, size.y) * max(0.0, size.z)


def collect_body_meshes():
    """Pick the main stele mesh, plus nearby Sketchfab parts that belong to it.

    Joining every Sketchfab child blindly can invent a huge AABB when ornaments sit
    far from the body; that then makes reorient treat depth as height.
    """
    meshes = []
    for obj in bpy.data.objects:
        if obj.type == "MESH" and under_sketchfab(obj) and len(obj.data.vertices) > 0:
            meshes.append(obj)
    if not meshes:
        return meshes

    main = max(meshes, key=mesh_world_volume)
    mn, mx = world_bounds(main)
    pad = (mx - mn).length * 0.08
    kept = []
    for obj in meshes:
        omn, omx = world_bounds(obj)
        # Keep parts whose bounds overlap the main body (expanded slightly).
        overlap = (
            omn.x <= mx.x + pad and omx.x >= mn.x - pad
            and omn.y <= mx.y + pad and omx.y >= mn.y - pad
            and omn.z <= mx.z + pad and omx.z >= mn.z - pad
        )
        if obj == main or overlap:
            kept.append(obj)
    # Always keep the largest; if overlap filter was too strict, fall back to main only.
    return kept if kept else [main]


def ensure_active_view_layer():
    return bpy.context.view_layer


def join_meshes(objects, name):
    if not objects:
        raise RuntimeError("no Sketchfab body meshes to join")
    ensure_active_view_layer()
    for o in bpy.data.objects:
        o.select_set(False)
    # Duplicate so the original Sketchfab hierarchy stays inspectable.
    copies = []
    for src in objects:
        dup = src.copy()
        dup.data = src.data.copy()
        bpy.context.scene.collection.objects.link(dup)
        # Keep the evaluated world pose; unparenting without this drops Sketchfab's
        # root rotation/scale and the stele lands on the wrong axis.
        dup.matrix_world = src.matrix_world.copy()
        dup.parent = None
        dup.select_set(True)
        copies.append(dup)
    bpy.context.view_layer.objects.active = copies[0]
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    bpy.ops.object.join()
    body = bpy.context.view_layer.objects.active
    body.name = name
    body.parent = None
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    return body


def reorient_stele_z_up(obj):
    """Put height on +Z and thickness on +Y so export matches the procedural relic axes.

    Procedural pipeline: Blender X=width, Z=height, Y=depth, front toward -Y.
    Sketchfab scans often arrive with Y-up; remap vertex axes directly (more reliable
    than composing matrix_world + transform_apply on joined meshes).
    """
    # Bake whatever pose the join left on the object into the mesh first.
    mesh = obj.data
    mesh.transform(obj.matrix_world)
    obj.matrix_world = Matrix.Identity(4)
    mesh.update()

    mn, mx = world_bounds(obj)
    size = mx - mn
    order = sorted([(0, size.x), (1, size.y), (2, size.z)], key=lambda t: t[1])
    depth_i = order[0][0]
    width_i = order[1][0]
    height_i = order[2][0]

    # Right-handed check on the permutation (width, depth, height) → (X, Y, Z).
    # Sign of the permutation of (width_i, depth_i, height_i) must be +1.
    perm = (width_i, depth_i, height_i)
    sign = 1
    # Count inversions.
    for a in range(3):
        for b in range(a + 1, 3):
            if perm[a] > perm[b]:
                sign *= -1

    print(f"STELE reorient size_in=({size.x:.3f},{size.y:.3f},{size.z:.3f}) "
          f"width_from={width_i} height_from={height_i} depth_from={depth_i} sign={sign}")

    for v in mesh.vertices:
        src = (v.co.x, v.co.y, v.co.z)
        y = src[depth_i] * sign
        v.co = Vector((src[width_i], y, src[height_i]))

    mesh.update()
    # Mirroring one axis flips winding. Reverse faces to restore outward normals.
    # Do NOT call recalc_face_normals afterward — it already pointed them out, and
    # reversing after that turns the whole stele inside-out.
    bm = bmesh.new()
    bm.from_mesh(mesh)
    if sign < 0:
        bmesh.ops.reverse_faces(bm, faces=bm.faces[:])
    for f in bm.faces:
        f.normal_update()
    bm.normal_update()
    bm.to_mesh(mesh)
    bm.free()
    mesh.update()
    # Blender 4+/5: Mesh.calc_normals() was removed; normals come from faces.
    try:
        mesh.calc_normals()
    except AttributeError:
        pass

    mn2, mx2 = world_bounds(obj)
    size2 = mx2 - mn2
    print(f"STELE reorient size_out=({size2.x:.3f},{size2.y:.3f},{size2.z:.3f})")
    return {"width_from": width_i, "height_from": height_i, "depth_from": depth_i,
            "size_in": [round(size.x, 4), round(size.y, 4), round(size.z, 4)],
            "size_out": [round(size2.x, 4), round(size2.y, 4), round(size2.z, 4)],
            "handedness_sign": sign}


def scale_to_height(obj, target_height):
    """Optionally uniform-scale so Z height matches target_height.

    target_height <= 0 keeps the original proportions/size (only recentre + ground).
    """
    reorient = reorient_stele_z_up(obj)
    mn, mx = world_bounds(obj)
    size = mx - mn
    extent = size.z
    if extent < 1e-6:
        raise RuntimeError("body has zero height after reorient")

    s = 1.0
    if target_height and target_height > 0:
        s = target_height / extent
        obj.scale = (s, s, s)
        bpy.ops.object.select_all(action="DESELECT")
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
        mn, mx = world_bounds(obj)

    centre = (mn + mx) * 0.5
    obj.location.x -= centre.x
    obj.location.y -= centre.y
    obj.location.z -= mn.z
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.transform_apply(location=True, rotation=False, scale=False)
    mn, mx = world_bounds(obj)
    return mn, mx, "z", s, reorient


def flip_mesh_normals(obj):
    """Reverse every face so normals point the other way."""
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.reverse_faces(bm, faces=bm.faces[:])
    bm.normal_update()
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()


def ensure_shell_front_outward(obj):
    """Thin crust prisms must have their -Y face pointing outward (toward -Y)."""
    front = None
    best = float("inf")
    for p in obj.data.polygons:
        if p.center.y < best:
            best = p.center.y
            front = p
    if front is not None and front.normal.y > 0:
        flip_mesh_normals(obj)


def ensure_detail_face_on_neg_y(obj):
    """Sketchfab scans often leave the carved face on +Y after reorient.

    Our pipeline expects the inscription / crust on -Y (camera looks from -Y).
    If the dense side is +Y, spin 180° around Z so detail lands on -Y.
    """
    ys = [v.co.y for v in obj.data.vertices]
    y_min, y_max = min(ys), max(ys)
    span = max(1e-6, y_max - y_min)
    lo = sum(1 for y in ys if y <= y_min + span * 0.15)
    hi = sum(1 for y in ys if y >= y_max - span * 0.15)
    print(f"STELE face density  lo(-Y)={lo} hi(+Y)={hi}")
    if hi <= lo:
        return False

    mesh = obj.data
    for v in mesh.vertices:
        v.co.x *= -1.0
        v.co.y *= -1.0
    # Proper 180° around Z: transform normals the same way.
    # Mesh.normals may be empty until calculated; fix via bmesh face winding + update.
    bm = bmesh.new()
    bm.from_mesh(mesh)
    for f in bm.faces:
        f.normal_update()
    bm.normal_update()
    bm.to_mesh(mesh)
    bm.free()
    mesh.update()
    print("STELE face flip     180deg around Z (detail -> -Y)")
    return True


def ensure_core_outward(obj):
    """Volume-based outward normals, then flip if the front (-Y) side still points in."""
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()

    # Sample faces near the minimum Y (inscription face). Their normals must aim -Y.
    ys = [v.co.y for v in obj.data.vertices]
    y_min = min(ys)
    thresh = y_min + (max(ys) - y_min) * 0.08
    dots = []
    for p in obj.data.polygons:
        if p.center.y <= thresh:
            dots.append(p.normal.y)
    if dots and (sum(dots) / len(dots)) > 0:
        flip_mesh_normals(obj)
    print(f"STELE core front ny={sum(dots)/len(dots) if dots else 0:.3f} faces={len(dots)}")


def triangulate(obj):
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()


def make_crust_piece(poly_xz, y_front, crust, name, mats, x0, x1, z0, z1):
    """Thin decay-crust slab on the front face for one Voronoi cell."""
    # Front faces -Y in the procedural convention; crust grows outward toward -Y.
    y0 = y_front - crust
    y1 = y_front + crust * 0.15
    obj = make_prism(poly_xz, y0, y1, name)
    triangulate(obj)
    ensure_shell_front_outward(obj)
    obj.data.materials.clear()
    obj.data.materials.append(mats["M_Shell_Outer"])
    obj.data.materials.append(mats["M_Shell_Inner"])
    planar_uv_from_front(obj, x0, x1, z0, z1)
    return obj


def build_shell_pieces(cells, y_front, crust, outer_mats, x0, x1, z0, z1):
    pieces = []
    for i, cell in enumerate(cells):
        name = f"Shell_Piece_{i:03d}"
        piece = make_crust_piece(cell["refined"], y_front, crust, name, outer_mats, x0, x1, z0, z1)
        pieces.append((piece, cell))
    return pieces


def remove_previous_relic():
    for name in ("Relic_Root", "Relic_Shell", "Relic_Core", "Stele_Body", "Relic_Shell_Source"):
        obj = bpy.data.objects.get(name)
        if obj is None:
            continue
        for child in list(obj.children_recursive):
            delete_object(child)
        delete_object(obj)
    for obj in list(bpy.data.objects):
        if obj.name.startswith("Shell_Piece_") or obj.name.startswith("_Prism_"):
            delete_object(obj)


def find_stele_albedo():
    img = bpy.data.images.get("Image_0")
    if img is not None:
        return img
    for image in bpy.data.images:
        if image.size[0] > 0 and image.name not in ("Render Result", "Viewer Node"):
            return image
    return None


def make_core_material(albedo):
    mat = bpy.data.materials.get("M_Core_Stele") or bpy.data.materials.new("M_Core_Stele")
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    bsdf.inputs["Roughness"].default_value = 0.72
    # Slightly lifted value so the restored inscription reads clearer than the dirty shell.
    if albedo is not None:
        tex = nt.nodes.new("ShaderNodeTexImage")
        tex.image = albedo
        nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    else:
        bsdf.inputs["Base Color"].default_value = (0.62, 0.58, 0.50, 1.0)
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    return mat


def make_shell_materials():
    """Outer = mud/grime crust; inner = fresher stone revealed on crack faces."""
    specs = {
        "M_Shell_Outer": (0.18, 0.15, 0.11),   # mud / soot
        "M_Shell_Inner": (0.55, 0.50, 0.42),   # cleaner stone under the crust
    }
    mats = {}
    for name, colour in specs.items():
        mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
        mat.use_nodes = True
        bsdf = mat.node_tree.nodes.get("Principled BSDF")
        if bsdf:
            bsdf.inputs["Base Color"].default_value = (*colour, 1.0)
            bsdf.inputs["Roughness"].default_value = 0.92
        mats[name] = mat
    return mats


def duplicate_mesh_object(src, name):
    obj = src.copy()
    obj.data = src.data.copy()
    obj.name = name
    bpy.context.scene.collection.objects.link(obj)
    obj.parent = None
    return obj


def assign_single_material(obj, mat):
    obj.data.materials.clear()
    obj.data.materials.append(mat)


def planar_uv_from_front(obj, x0, x1, z0, z1):
    """Map front-face XZ into 0-1 UV so the crack mask lines up with Voronoi cells."""
    mesh = obj.data
    if not mesh.uv_layers:
        mesh.uv_layers.new(name="UVMap")
    uv = mesh.uv_layers.active.data
    sx = max(1e-6, x1 - x0)
    sz = max(1e-6, z1 - z0)
    for poly in mesh.polygons:
        for li in poly.loop_indices:
            vi = mesh.loops[li].vertex_index
            co = mesh.vertices[vi].co
            uv[li].uv = ((co.x - x0) / sx, (co.z - z0) / sz)


def make_prism(poly_xz, y0, y1, name):
    """Extrude a 2D cell (X,Z) through depth Y to cut a shell piece."""
    bm = bmesh.new()
    verts = [bm.verts.new((p[0], y0, p[1])) for p in poly_xz]
    bm.faces.new(verts)
    bm.faces.ensure_lookup_table()
    result = bmesh.ops.extrude_face_region(bm, geom=[bm.faces[0]])
    extruded = [v for v in result["geom"] if isinstance(v, bmesh.types.BMVert)]
    for v in extruded:
        v.co.y = y1
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    return obj


def default_out_dir():
    blend = bpy.data.filepath
    if blend:
        # .../Assets/SacredRelicDemo/Generated/Models/foo.blend -> Generated
        parts = blend.replace("\\", "/").split("/")
        if "SacredRelicDemo" in parts:
            idx = parts.index("SacredRelicDemo")
            return "/".join(parts[: idx + 1] + ["Generated"])
    return os.path.join(os.path.dirname(os.path.abspath(__file__)),
                        "..", "..", "Assets", "SacredRelicDemo", "Generated")


def run(args=None):
    global G
    G = _load_shared()
    args = args or parse_args()
    out_dir = os.path.abspath(args.out) if args.out else os.path.abspath(default_out_dir())
    models = os.path.join(out_dir, "Models")
    textures = os.path.join(out_dir, "Textures")
    os.makedirs(models, exist_ok=True)
    os.makedirs(textures, exist_ok=True)

    if bpy.data.objects.get("Sketchfab_model") is None:
        raise RuntimeError("Sketchfab_model not found in the open scene")

    removed = clean_empty_sketchfab_meshes()
    bodies = collect_body_meshes()
    if not bodies:
        raise RuntimeError("no non-empty meshes under Sketchfab_model")

    remove_previous_relic()

    body = join_meshes(bodies, "Stele_Body")
    mn, mx, height_axis, scale, reorient = scale_to_height(body, args.height)
    ensure_detail_face_on_neg_y(body)
    # Bounds change after the possible 180° spin — refresh before placing the crust.
    mn, mx = world_bounds(body)
    size = mx - mn

    # Front plane: XZ crack domain, depth along Y. Crust peels from the -Y face.
    pad = args.crust
    x0, x1 = mn.x - pad, mx.x + pad
    z0, z1 = mn.z - pad, mx.z + pad
    outer_w, outer_h = (x1 - x0), (z1 - z0)
    y_front = mn.y

    albedo = find_stele_albedo()
    core_mat = make_core_material(albedo)
    shell_mats = make_shell_materials()

    # Always write a Unity-ready albedo next to the crack mask.
    if albedo is not None:
        albedo_out = os.path.join(textures, "Image_0.png")
        try:
            albedo.filepath_raw = albedo_out
            albedo.file_format = "PNG"
            albedo.save_render(albedo_out)
            print("STELE albedo       ", albedo_out)
        except Exception as exc:
            print("STELE albedo export failed:", exc)

    root = bpy.data.objects.new("Relic_Root", None)
    bpy.context.scene.collection.objects.link(root)
    shell_root = bpy.data.objects.new("Relic_Shell", None)
    bpy.context.scene.collection.objects.link(shell_root)
    shell_root.parent = root

    core = duplicate_mesh_object(body, "Relic_Core")
    assign_single_material(core, core_mat)
    ensure_core_outward(core)
    core.parent = root

    rng = random.Random(args.seed)
    # Voronoi in XZ, stored as (x, z) pairs using the shared 2D helpers.
    seeds = G.gen_seeds(outer_w, outer_h, args.cells, rng)
    cells = G.voronoi_cells(seeds, outer_w, outer_h)
    table = G.build_edge_table(cells, outer_w, outer_h, args.seg_len, args.jag)
    G.build_propagation(table)
    G.cell_timings(cells, table)
    cells = G.refine_cells(cells, table)
    cells.sort(key=lambda c: (c["detach"], math.hypot(*c["seed"])))

    # Shift cell coordinates from centred Voronoi space into world XZ.
    cx, cz = (x0 + x1) * 0.5, (z0 + z1) * 0.5

    def to_world_poly(poly):
        return [(p[0] + cx, p[1] + cz) for p in poly]

    for cell in cells:
        cell["refined"] = to_world_poly(cell["refined"])
        cell["seed_world"] = (cell["seed"][0] + cx, cell["seed"][1] + cz)

    pieces = build_shell_pieces(cells, y_front, args.crust, shell_mats, x0, x1, z0, z1)

    max_r = math.hypot(outer_w / 2, outer_h / 2)
    manifest = {
        "narrative": "decay_shell",
        "narrativeSummary": (
            "Decay shell (mud, grime, lichen, weathered crust) peels away; "
            "inscription core remains and restores."
        ),
        "source": "Sketchfab_model",
        "heightAxis": height_axis,
        "reorient": reorient,
        "scaleApplied": round(scale, 6),
        "width": round(size.x, 5),
        "height": round(size.z, 5),
        "thickness": round(size.y, 5),
        "crust": args.crust,
        "outerWidth": round(outer_w, 5),
        "outerHeight": round(outer_h, 5),
        "cellCount": len(pieces),
        "seed": args.seed,
        "cleanedEmptyMeshes": removed,
        "bodyParts": [o.name for o in bodies],
        "pieces": [],
    }

    total_tris = 0
    for i, (piece, cell) in enumerate(pieces):
        piece.name = f"Shell_Piece_{i:03d}"
        piece.parent = shell_root
        # Keep world pose; parenting without inverse would jump.
        piece.matrix_parent_inverse = shell_root.matrix_world.inverted()
        tris = len(piece.data.polygons)
        total_tris += tris
        centroid = sum((piece.matrix_world @ v.co for v in piece.data.vertices), Vector()) / max(
            1, len(piece.data.vertices))
        manifest["pieces"].append({
            "name": piece.name,
            "centroid": [round(float(centroid.x), 5), round(float(centroid.y), 5),
                         round(float(centroid.z), 5)],
            "seed2D": [round(cell["seed"][0], 5), round(cell["seed"][1], 5)],
            "radial": round(math.hypot(*cell["seed"]) / max_r, 5),
            "arrive": round(cell["arrive"], 5),
            "detach": round(cell["detach"], 5),
            "tris": tris,
        })

    # Hide the working body; Sketchfab original stays as reference.
    body.hide_set(True)
    body.hide_render = True

    mask_path = os.path.join(textures, "T_SacredRelic_CrackMask.png")
    # Rasteriser expects cells/table in centred coordinates.
    centred_cells = []
    for cell in cells:
        centred = dict(cell)
        centred["poly"] = [(p[0] - cx, p[1] - cz) for p in cell["poly"]]
        centred["refined"] = [(p[0] - cx, p[1] - cz) for p in cell["refined"]]
        centred_cells.append(centred)
    # Rebuild edge table in centred space for the mask (propagation already on cell timings).
    table_c = G.build_edge_table(
        [{"seed": c["seed"], "poly": c["poly"]} for c in centred_cells],
        outer_w, outer_h, args.seg_len, args.jag)
    G.build_propagation(table_c)
    for src, dst in zip(centred_cells, cells):
        # Keep arrive/detach from the live cells used for pieces.
        src["arrive"] = dst["arrive"]
        src["detach"] = dst["detach"]
    seg_count = G.rasterise_mask(mask_path, table_c, centred_cells, outer_w, outer_h,
                                 args.mask, args.crack_width)

    # Export only the relic hierarchy.
    for o in bpy.data.objects:
        o.select_set(False)
    root.select_set(True)
    for o in root.children_recursive:
        o.select_set(True)
    fbx_path = os.path.join(models, "SacredRelic_Fractured.fbx")
    kwargs = dict(
        filepath=fbx_path,
        use_selection=True,
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_UNITS",
        axis_forward="-Z",
        axis_up="Y",
        object_types={"EMPTY", "MESH"},
        use_mesh_modifiers=True,
        mesh_smooth_type="FACE",
        use_tspace=True,
        bake_space_transform=False,
        path_mode="COPY",
        embed_textures=True,
    )
    try:
        bpy.ops.export_scene.fbx(**kwargs)
    except TypeError:
        for drop in ("use_tspace", "apply_scale_options", "bake_space_transform", "embed_textures"):
            kwargs.pop(drop, None)
        bpy.ops.export_scene.fbx(**kwargs)

    manifest["crackSegments"] = seg_count
    manifest["coreTris"] = len(core.data.polygons)
    manifest["shellTris"] = total_tris
    with open(os.path.join(models, "SacredRelic_Fractured.json"), "w") as fh:
        json.dump(manifest, fh, indent=2)

    # Keep the working blend next to the previous artwork output when possible.
    blend_out = os.path.join(os.path.dirname(out_dir) if out_dir.endswith("Generated") else out_dir,
                             "..", "..", "Artwork", "SacredRelic")
    # Prefer repo Artwork path.
    repo_art = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..",
                                            "Artwork", "SacredRelic"))
    os.makedirs(repo_art, exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(repo_art, "SacredRelic_Stele.blend"))

    print("STELE removed empty ", removed)
    print("STELE body parts   ", [o.name for o in bodies])
    print("STELE scale        ", scale, "axis", height_axis)
    print("STELE pieces       ", len(pieces))
    print("STELE core tris    ", len(core.data.polygons))
    print("STELE shell tris   ", total_tris)
    print("STELE fbx          ", fbx_path)
    print("STELE mask         ", mask_path)
    return manifest


if __name__ == "__main__":
    run()
