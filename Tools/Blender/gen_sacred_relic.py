"""Generate a pre-fractured stone tablet (core + Voronoi-shattered crust) for Unity.

Run headless:
    /Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup \
        --python Tools/Blender/gen_sacred_relic.py -- --out Assets/SacredRelicDemo/Generated

The crust is cut by a 2D Voronoi diagram in the tablet plane, so the crack polylines
are explicit data. The same polylines drive both the mesh and the crack mask texture,
which is what guarantees fragments line up with the painted cracks.

Axes (Blender, Z-up): X = width, Z = height, Y = depth. Front face points at -Y.
Exported with -Z forward / Y up so Unity gets X = width, Y = height, Z = depth.
"""

import argparse
import heapq
import json
import math
import os
import random
import sys

import bmesh
import bpy
import numpy as np

EPS = 1e-6


# --------------------------------------------------------------------------- args


def parse_args():
    argv = sys.argv
    argv = argv[argv.index("--") + 1:] if "--" in argv else []
    p = argparse.ArgumentParser()
    p.add_argument("--out", required=True, help="output directory (absolute or repo-relative)")
    p.add_argument("--blend", default="", help="where to save the .blend; keep it outside "
                                              "Assets/ or Unity imports the model twice")
    p.add_argument("--width", type=float, default=0.60)
    p.add_argument("--height", type=float, default=0.90)
    p.add_argument("--thickness", type=float, default=0.08)
    p.add_argument("--crust", type=float, default=0.012, help="crust thickness in metres")
    p.add_argument("--cells", type=int, default=24)
    p.add_argument("--seed", type=int, default=20260728)
    p.add_argument("--seg-len", type=float, default=0.009, help="crack subdivision length")
    p.add_argument("--jag", type=float, default=0.011, help="max crack jaggedness amplitude")
    p.add_argument("--mask", type=int, default=2048, help="crack mask resolution")
    p.add_argument("--crack-width", type=float, default=0.0035, help="crack half-width in metres")
    p.add_argument("--bevel", type=float, default=0.003)
    return p.parse_args(argv)


# ------------------------------------------------------------------- 2D geometry


def signed_area(poly):
    a = 0.0
    for i in range(len(poly)):
        x0, y0 = poly[i]
        x1, y1 = poly[(i + 1) % len(poly)]
        a += x0 * y1 - x1 * y0
    return 0.5 * a


def ensure_ccw(poly):
    return poly if signed_area(poly) >= 0 else poly[::-1]


def dedupe(poly, eps=1e-7):
    out = []
    for p in poly:
        if not out or (abs(p[0] - out[-1][0]) > eps or abs(p[1] - out[-1][1]) > eps):
            out.append(p)
    while len(out) > 1 and abs(out[0][0] - out[-1][0]) < eps and abs(out[0][1] - out[-1][1]) < eps:
        out.pop()
    return out


def clip_halfplane(poly, m, d):
    """Keep the part of poly where dot(p - m, d) <= 0."""
    out = []
    n = len(poly)
    for i in range(n):
        a, b = poly[i], poly[(i + 1) % n]
        da = (a[0] - m[0]) * d[0] + (a[1] - m[1]) * d[1]
        db = (b[0] - m[0]) * d[0] + (b[1] - m[1]) * d[1]
        if da <= 0:
            out.append(a)
        if (da <= 0) != (db <= 0):
            t = da / (da - db)
            out.append((a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t))
    return dedupe(out)


def clip_rect(poly, x0, x1, y0, y1):
    for m, d in (((x0, 0), (-1, 0)), ((x1, 0), (1, 0)), ((0, y0), (0, -1)), ((0, y1), (0, 1))):
        poly = clip_halfplane(poly, m, d)
        if len(poly) < 3:
            return []
    return poly


# ----------------------------------------------------------------------- voronoi


def gen_seeds(w, h, count, rng):
    """Poisson-disk seeds whose spacing grows with distance from the centre.

    Dart throwing rather than a jittered grid: a grid leaves visible rows and
    columns in the crack network, which instantly reads as procedural.
    """
    max_r = math.hypot(w / 2, h / 2)
    spacing = math.sqrt(w * h / max(1, count)) * 1.07
    r_min, r_max = spacing * 0.62, spacing * 1.30

    def radius_at(x, y):
        t = (math.hypot(x, y) / max_r) ** 0.8
        return r_min + (r_max - r_min) * t

    pts = [(0.0, 0.0)]
    attempts = 0
    while len(pts) < count and attempts < count * 900:
        attempts += 1
        x = rng.uniform(-w / 2, w / 2)
        y = rng.uniform(-h / 2, h / 2)
        rc = radius_at(x, y)
        ok = True
        for px, py in pts:
            if math.hypot(x - px, y - py) < min(rc, radius_at(px, py)):
                ok = False
                break
        if ok:
            pts.append((x, y))
    return pts


def voronoi_cells(seeds, w, h):
    rect = [(-w / 2, -h / 2), (w / 2, -h / 2), (w / 2, h / 2), (-w / 2, h / 2)]
    cells = []
    for i, si in enumerate(seeds):
        poly = rect
        for j, sj in enumerate(seeds):
            if i == j:
                continue
            m = ((si[0] + sj[0]) * 0.5, (si[1] + sj[1]) * 0.5)
            d = (sj[0] - si[0], sj[1] - si[1])
            poly = clip_halfplane(poly, m, d)
            if len(poly) < 3:
                break
        if len(poly) >= 3:
            cells.append({"seed": si, "poly": ensure_ccw(poly)})
    return cells


# ------------------------------------------------------------- shared crack edges


def edge_key(a, b, q=1e-5):
    ka = (int(round(a[0] / q)), int(round(a[1] / q)))
    kb = (int(round(b[0] / q)), int(round(b[1] / q)))
    return (ka, kb) if ka <= kb else (kb, ka)


def on_silhouette(a, b, w, h, tol=1e-5):
    hw, hh = w / 2, h / 2
    if abs(a[0] - hw) < tol and abs(b[0] - hw) < tol:
        return True
    if abs(a[0] + hw) < tol and abs(b[0] + hw) < tol:
        return True
    if abs(a[1] - hh) < tol and abs(b[1] - hh) < tol:
        return True
    if abs(a[1] + hh) < tol and abs(b[1] + hh) < tol:
        return True
    return False


def _hash01(i, seed):
    x = (i * 374761393 + seed * 668265263) & 0xFFFFFFFF
    x = ((x ^ (x >> 13)) * 1274126177) & 0xFFFFFFFF
    return ((x ^ (x >> 16)) & 0xFFFFFF) / float(0xFFFFFF)


def _value_noise(x, seed):
    i = math.floor(x)
    f = x - i
    f = f * f * (3.0 - 2.0 * f)
    a = _hash01(int(i), seed) * 2.0 - 1.0
    b = _hash01(int(i) + 1, seed) * 2.0 - 1.0
    return a + (b - a) * f


def _fbm(t, seed, octaves=3):
    total, amp, freq, norm = 0.0, 1.0, 1.0, 0.0
    for o in range(octaves):
        total += amp * _value_noise(t * freq, seed + o * 7919)
        norm += amp
        amp *= 0.42
        freq *= 2.7
    return total / norm


def displace_edge(a, b, seg_len, amp_max, seed):
    """Subdivide a->b and bend it with smooth fractal noise.

    Low-frequency fBm rather than per-point randomness: a crack wanders in long
    organic curves with fine detail on top, instead of buzzing like a saw blade.
    Endpoints stay put so the three or more cells meeting there remain welded.
    """
    dx, dy = b[0] - a[0], b[1] - a[1]
    length = math.hypot(dx, dy)
    k = max(1, int(round(length / seg_len)))
    if k < 3:
        return [a, b]
    nx, ny = -dy / length, dx / length
    amp = min(amp_max, length * 0.17)
    phase = _hash01(seed, 12345) * 64.0
    cycles = 1.4 + _hash01(seed, 777) * 1.3
    pts = [a]
    for i in range(1, k):
        t = i / k
        taper = math.sin(math.pi * t) ** 0.62
        off = _fbm(phase + t * cycles, seed) * amp * taper
        pts.append((a[0] + dx * t + nx * off, a[1] + dy * t + ny * off))
    pts.append(b)
    return pts


def build_edge_table(cells, w, h, seg_len, amp_max):
    table = {}
    for cell in cells:
        poly = cell["poly"]
        for i in range(len(poly)):
            a, b = poly[i], poly[(i + 1) % len(poly)]
            key = edge_key(a, b)
            if key in table:
                continue
            border = on_silhouette(a, b, w, h)
            if border:
                table[key] = {"pts": [a, b], "border": True}
            else:
                seed = (hash(key) & 0x7FFFFFFF) ^ 0x5F3759DF
                table[key] = {"pts": displace_edge(a, b, seg_len, amp_max, seed), "border": False}
    return table


def node_key(p, q=1e-5):
    return (int(round(p[0] / q)), int(round(p[1] / q)))


def build_propagation(table):
    """Label every crack point with when the fracture reaches it, 0 at the centre.

    Distance is measured *along the crack graph* (Dijkstra), not straight-line from
    the centre. Straight-line would light up a far segment before the crack that
    connects it, so isolated glowing stubs would pop out of nowhere.
    """
    adj = {}
    for entry in table.values():
        if entry["border"]:
            continue
        pts = entry["pts"]
        cum = [0.0]
        for i in range(len(pts) - 1):
            cum.append(cum[-1] + math.dist(pts[i], pts[i + 1]))
        entry["cum"] = cum
        u, v = node_key(pts[0]), node_key(pts[-1])
        entry["nodes"] = (u, v)
        adj.setdefault(u, []).append((v, cum[-1]))
        adj.setdefault(v, []).append((u, cum[-1]))

    if not adj:
        return
    origin = min(adj, key=lambda k: math.hypot(k[0], k[1]))
    dist = {origin: 0.0}
    heap = [(0.0, origin)]
    while heap:
        d, node = heapq.heappop(heap)
        if d > dist.get(node, math.inf) + EPS:
            continue
        for nb, w in adj[node]:
            nd = d + w
            if nd < dist.get(nb, math.inf) - EPS:
                dist[nb] = nd
                heapq.heappush(heap, (nd, nb))

    # Anything the graph never reaches arrives last rather than never.
    fallback = (max(dist.values()) if dist else 1.0) * 1.15
    for entry in table.values():
        if entry["border"]:
            continue
        u, v = entry["nodes"]
        du, dv = dist.get(u, fallback), dist.get(v, fallback)
        total = entry["cum"][-1]
        entry["prop"] = [min(du + s, dv + (total - s)) for s in entry["cum"]]

    peak = max((max(e["prop"]) for e in table.values() if not e["border"]), default=1.0)
    peak = peak if peak > EPS else 1.0
    for entry in table.values():
        if not entry["border"]:
            entry["prop"] = [p / peak for p in entry["prop"]]


def cell_timings(cells, table):
    """When each piece is first touched by a crack, and when it is fully cut free."""
    for cell in cells:
        poly = cell["poly"]
        vals = []
        for i in range(len(poly)):
            entry = table[edge_key(poly[i], poly[(i + 1) % len(poly)])]
            if not entry["border"]:
                vals.extend(entry["prop"])
        cell["arrive"] = min(vals) if vals else 0.0
        cell["detach"] = max(vals) if vals else 0.0


def refine_cells(cells, table):
    for cell in cells:
        poly = cell["poly"]
        refined = []
        for i in range(len(poly)):
            a, b = poly[i], poly[(i + 1) % len(poly)]
            pts = table[edge_key(a, b)]["pts"]
            if math.hypot(pts[0][0] - a[0], pts[0][1] - a[1]) > 1e-6:
                pts = pts[::-1]
            refined.extend(pts[:-1])
        cell["refined"] = ensure_ccw(dedupe(refined))
    return cells


# ------------------------------------------------------------------ mesh building

MAT_OUTER, MAT_INNER = 0, 1
INNER_UV_SCALE = 1.0 / 0.25  # tiling metres for the fracture-face texture


def add_prism(bm, poly, y0, y1, uv, outer_w, outer_h, cap_outer):
    """Extrude a 2D polygon between depths y0 and y1. Returns nothing.

    cap_outer=True marks the y0 cap as the weathered outer surface (material 0)
    and gives it tablet-space UVs; every other face is a fracture face.
    """
    poly = ensure_ccw(poly)
    lo = [bm.verts.new((p[0], y0, p[1])) for p in poly]
    hi = [bm.verts.new((p[0], y1, p[1])) for p in poly]
    n = len(poly)

    face_lo = bm.faces.new(lo)
    face_hi = bm.faces.new(list(reversed(hi)))
    walls = []
    for i in range(n):
        j = (i + 1) % n
        walls.append(bm.faces.new((lo[i], lo[j], hi[j], hi[i])))

    if cap_outer:
        face_lo.material_index = MAT_OUTER
        for loop in face_lo.loops:
            x, _, z = loop.vert.co
            loop[uv].uv = (0.5 + x / outer_w, 0.5 + z / outer_h)
    else:
        face_lo.material_index = MAT_INNER
        for loop in face_lo.loops:
            x, _, z = loop.vert.co
            loop[uv].uv = (x * INNER_UV_SCALE, z * INNER_UV_SCALE)

    face_hi.material_index = MAT_INNER
    for loop in face_hi.loops:
        x, _, z = loop.vert.co
        loop[uv].uv = (x * INNER_UV_SCALE, z * INNER_UV_SCALE)

    run = 0.0
    for i, face in enumerate(walls):
        j = (i + 1) % n
        seg = math.hypot(poly[j][0] - poly[i][0], poly[j][1] - poly[i][1])
        face.material_index = MAT_INNER
        for loop in face.loops:
            x, y, z = loop.vert.co
            at_start = abs(x - poly[i][0]) < 1e-9 and abs(z - poly[i][1]) < 1e-9
            s = run if at_start else run + seg
            loop[uv].uv = (s * INNER_UV_SCALE, y * INNER_UV_SCALE)
        run += seg


def rim_strips(w, h, crust):
    hw, hh = w / 2, h / 2
    ow, oh = hw + crust, hh + crust
    return [
        (-ow, -hw, -oh, oh),
        (hw, ow, -oh, oh),
        (-hw, hw, -oh, -hh),
        (-hw, hw, hh, oh),
    ]


def make_piece(name, poly, thickness, crust, outer_w, outer_h, w, h):
    mesh = bpy.data.meshes.new(name)
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")

    add_prism(bm, poly, -crust, 0.0, uv, outer_w, outer_h, cap_outer=True)
    for x0, x1, y0, y1 in rim_strips(w, h, crust):
        sub = clip_rect(poly, x0, x1, y0, y1)
        if len(sub) >= 3:
            add_prism(bm, sub, 0.0, thickness, uv, outer_w, outer_h, cap_outer=False)

    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    bmesh.ops.triangulate(bm, faces=bm.faces[:], quad_method="BEAUTY", ngon_method="BEAUTY")
    bm.to_mesh(mesh)
    bm.free()
    mesh.materials.append(bpy.data.materials["M_Shell_Outer"])
    mesh.materials.append(bpy.data.materials["M_Shell_Inner"])
    return bpy.data.objects.new(name, mesh)


def recentre(obj):
    """Move the mesh so its origin sits at the area-weighted centroid, keeping world pose."""
    mesh = obj.data
    total = 0.0
    acc = np.zeros(3)
    verts = np.array([v.co[:] for v in mesh.vertices])
    for poly in mesh.polygons:
        idx = list(poly.vertices)
        a, b, c = verts[idx[0]], verts[idx[1]], verts[idx[2]]
        area = 0.5 * np.linalg.norm(np.cross(b - a, c - a))
        acc += area * (a + b + c) / 3.0
        total += area
    centroid = acc / total if total > EPS else verts.mean(axis=0)
    for v in mesh.vertices:
        v.co = (v.co[0] - centroid[0], v.co[1] - centroid[1], v.co[2] - centroid[2])
    obj.location = (centroid[0], centroid[1], centroid[2])
    return centroid


def make_core(w, h, thickness, bevel):
    mesh = bpy.data.meshes.new("Relic_Core")
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    add_prism(bm, [(-w / 2, -h / 2), (w / 2, -h / 2), (w / 2, h / 2), (-w / 2, h / 2)],
              0.0, thickness, uv, w, h, cap_outer=True)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    if bevel > 0:
        bmesh.ops.bevel(bm, geom=bm.edges[:] + bm.verts[:], offset=bevel, segments=2,
                        profile=0.5, affect="EDGES", clamp_overlap=True)
    bmesh.ops.triangulate(bm, faces=bm.faces[:], quad_method="BEAUTY", ngon_method="BEAUTY")
    bm.to_mesh(mesh)
    bm.free()
    mesh.materials.append(bpy.data.materials["M_Core"])
    return bpy.data.objects.new("Relic_Core", mesh)


# ------------------------------------------------------------------ crack mask


def rasterise_mask(path, table, cells, outer_w, outer_h, res, half_width):
    """R = crack intensity, G = radial distance from centre, B = cell id, A = 1.

    Distances are measured in metres, so cracks keep a constant real-world width
    even though the texture is square and the tablet is not.
    """
    res_x = res_y = res
    px_x = outer_w / res_x
    px_y = outer_h / res_y

    xs = (np.arange(res_x) + 0.5) * px_x - outer_w / 2
    ys = (np.arange(res_y) + 0.5) * px_y - outer_h / 2

    max_r = math.hypot(outer_w / 2, outer_h / 2)
    crack = np.zeros((res_y, res_x), dtype=np.float32)

    lengths = []
    for entry in table.values():
        if entry["border"]:
            continue
        pts = entry["pts"]
        entry["length"] = sum(math.dist(pts[i], pts[i + 1]) for i in range(len(pts) - 1))
        lengths.append(entry["length"])
    ref_len = sorted(lengths)[len(lengths) // 2] if lengths else 1.0

    segments = []
    for entry in table.values():
        if entry["border"]:
            continue
        # Long edges are trunk cracks and read wider; short ones are hairline branches.
        trunk = 0.55 + 0.85 * min(1.0, entry["length"] / max(ref_len, EPS))
        pts = entry["pts"]
        for i in range(len(pts) - 1):
            a, b = pts[i], pts[i + 1]
            mid_r = math.hypot((a[0] + b[0]) * 0.5, (a[1] + b[1]) * 0.5) / max_r
            prop = 0.5 * (entry["prop"][i] + entry["prop"][i + 1])
            # Widest at the centre so the network reads as an impact origin.
            segments.append((a, b, half_width * trunk * (1.30 - 0.62 * mid_r), prop))

    arrival = np.ones((res_y, res_x), dtype=np.float32)

    for (ax, ay), (bx, by), w, prop in segments:
        pad = w * 3.0
        x0 = max(0, int((min(ax, bx) - pad + outer_w / 2) / px_x))
        x1 = min(res_x, int((max(ax, bx) + pad + outer_w / 2) / px_x) + 2)
        y0 = max(0, int((min(ay, by) - pad + outer_h / 2) / px_y))
        y1 = min(res_y, int((max(ay, by) + pad + outer_h / 2) / px_y) + 2)
        if x0 >= x1 or y0 >= y1:
            continue
        gx, gy = np.meshgrid(xs[x0:x1], ys[y0:y1])
        ex, ey = bx - ax, by - ay
        ll = ex * ex + ey * ey
        t = 0.0 if ll < EPS else np.clip(((gx - ax) * ex + (gy - ay) * ey) / ll, 0.0, 1.0)
        d = np.hypot(gx - (ax + t * ex), gy - (ay + t * ey))
        inten = np.clip(1.0 - (d - w * 0.30) / w, 0.0, 1.0)
        inten = (inten * inten * (3.0 - 2.0 * inten)).astype(np.float32)
        window = crack[y0:y1, x0:x1]
        closer = inten > window
        arrival[y0:y1, x0:x1] = np.where(closer, np.float32(prop), arrival[y0:y1, x0:x1])
        crack[y0:y1, x0:x1] = np.where(closer, inten, window)

    gx, gy = np.meshgrid(xs, ys)

    seeds = np.array([c["seed"] for c in cells], dtype=np.float32)
    ids = np.array([(hash((i, 0x9E3779B9)) & 0xFFFF) / 65535.0 for i in range(len(cells))],
                   dtype=np.float32)
    cell_id = np.zeros((res_y, res_x), dtype=np.float32)
    chunk = max(1, 1 << 22 // max(1, len(seeds)))
    flat_x, flat_y = gx.ravel(), gy.ravel()
    out = np.empty(flat_x.size, dtype=np.float32)
    for start in range(0, flat_x.size, chunk):
        stop = min(flat_x.size, start + chunk)
        dx = flat_x[start:stop, None] - seeds[None, :, 0]
        dy = flat_y[start:stop, None] - seeds[None, :, 1]
        out[start:stop] = ids[np.argmin(dx * dx + dy * dy, axis=1)]
    cell_id = out.reshape(res_y, res_x)

    save_png("SacredRelic_CrackMask", path, np.stack(
        [crack.astype(np.float32), arrival, cell_id, np.ones_like(crack, dtype=np.float32)], -1))

    folder = os.path.dirname(path)
    grey = crack.astype(np.float32)
    save_png("CrackDebug", os.path.join(folder, "DEBUG_CrackLines.png"),
             np.stack([grey, grey, grey, np.ones_like(grey)], -1))
    # Arrival time visualised on the crack only: black at the centre, white at the rim.
    arr = arrival * grey
    save_png("ArrivalDebug", os.path.join(folder, "DEBUG_CrackArrival.png"),
             np.stack([arr, arr, arr, np.ones_like(arr)], -1))
    return len(segments)


def save_png(name, path, rgba):
    res_y, res_x = rgba.shape[0], rgba.shape[1]
    img = bpy.data.images.new(name, width=res_x, height=res_y, alpha=True,
                              float_buffer=False, is_data=True)
    img.pixels.foreach_set(np.ascontiguousarray(rgba, dtype=np.float32).ravel())
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()


# ----------------------------------------------------------------------- scene


def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    for coll in (bpy.data.materials, bpy.data.meshes, bpy.data.objects, bpy.data.images):
        for item in list(coll):
            coll.remove(item)


def make_materials():
    specs = {
        "M_Core": (0.62, 0.58, 0.50),
        "M_Shell_Outer": (0.22, 0.20, 0.16),
        "M_Shell_Inner": (0.74, 0.71, 0.64),
    }
    for name, colour in specs.items():
        mat = bpy.data.materials.new(name)
        mat.use_nodes = True
        bsdf = mat.node_tree.nodes.get("Principled BSDF")
        if bsdf:
            bsdf.inputs["Base Color"].default_value = (*colour, 1.0)
            bsdf.inputs["Roughness"].default_value = 0.85
    return specs


def export_fbx(path):
    kwargs = dict(
        filepath=path,
        use_selection=False,
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_UNITS",
        axis_forward="-Z",
        axis_up="Y",
        object_types={"EMPTY", "MESH"},
        use_mesh_modifiers=True,
        mesh_smooth_type="FACE",
        use_tspace=True,
        # Left off deliberately: Blender's own baking mangles children that carry a
        # translation, which is every crust piece. Unity's importer does it correctly,
        # so ModelImporter.bakeAxisConversion is switched on instead.
        bake_space_transform=False,
        path_mode="COPY",
    )
    try:
        bpy.ops.export_scene.fbx(**kwargs)
    except TypeError:
        for drop in ("use_tspace", "apply_scale_options", "bake_space_transform"):
            kwargs.pop(drop, None)
        bpy.ops.export_scene.fbx(**kwargs)


def main():
    args = parse_args()
    out_dir = os.path.abspath(args.out)
    models = os.path.join(out_dir, "Models")
    textures = os.path.join(out_dir, "Textures")
    os.makedirs(models, exist_ok=True)
    os.makedirs(textures, exist_ok=True)

    w, h, t, crust = args.width, args.height, args.thickness, args.crust
    outer_w, outer_h = w + 2 * crust, h + 2 * crust

    rng = random.Random(args.seed)
    seeds = gen_seeds(outer_w, outer_h, args.cells, rng)
    cells = voronoi_cells(seeds, outer_w, outer_h)
    table = build_edge_table(cells, outer_w, outer_h, args.seg_len, args.jag)
    build_propagation(table)
    cell_timings(cells, table)
    cells = refine_cells(cells, table)
    # Numbered in the order the fracture frees them, so the driver can just walk the list.
    cells.sort(key=lambda c: (c["detach"], math.hypot(*c["seed"])))

    reset_scene()
    make_materials()
    scene = bpy.context.scene

    root = bpy.data.objects.new("Relic_Root", None)
    scene.collection.objects.link(root)
    shell_root = bpy.data.objects.new("Relic_Shell", None)
    scene.collection.objects.link(shell_root)
    shell_root.parent = root

    core = make_core(w, h, t, args.bevel)
    scene.collection.objects.link(core)
    core.parent = root

    max_r = math.hypot(outer_w / 2, outer_h / 2)
    manifest = {
        "width": w, "height": h, "thickness": t, "crust": crust,
        "outerWidth": outer_w, "outerHeight": outer_h,
        "cellCount": len(cells), "seed": args.seed, "pieces": [],
    }

    total_tris = 0
    for i, cell in enumerate(cells):
        name = f"Shell_Piece_{i:03d}"
        obj = make_piece(name, cell["refined"], t, crust, outer_w, outer_h, w, h)
        scene.collection.objects.link(obj)
        obj.parent = shell_root
        centroid = recentre(obj)
        tris = len(obj.data.polygons)
        total_tris += tris
        manifest["pieces"].append({
            "name": name,
            "centroid": [round(float(centroid[0]), 5), round(float(centroid[1]), 5),
                         round(float(centroid[2]), 5)],
            "seed2D": [round(cell["seed"][0], 5), round(cell["seed"][1], 5)],
            "radial": round(math.hypot(*cell["seed"]) / max_r, 5),
            "arrive": round(cell["arrive"], 5),
            "detach": round(cell["detach"], 5),
            "tris": tris,
        })

    mask_path = os.path.join(textures, "T_SacredRelic_CrackMask.png")
    seg_count = rasterise_mask(mask_path, table, cells, outer_w, outer_h,
                               args.mask, args.crack_width)

    fbx_path = os.path.join(models, "SacredRelic_Fractured.fbx")
    export_fbx(fbx_path)
    blend_dir = os.path.abspath(args.blend) if args.blend else os.path.join(
        out_dir.split(os.sep + "Assets" + os.sep)[0], "Artwork", "SacredRelic")
    os.makedirs(blend_dir, exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(blend_dir, "SacredRelic_Fractured.blend"))

    manifest["crackSegments"] = seg_count
    manifest["coreTris"] = len(core.data.polygons)
    manifest["shellTris"] = total_tris
    with open(os.path.join(models, "SacredRelic_Fractured.json"), "w") as fh:
        json.dump(manifest, fh, indent=2)

    print("RELIC pieces      ", len(cells))
    print("RELIC core tris   ", len(core.data.polygons))
    print("RELIC shell tris  ", total_tris)
    print("RELIC total tris  ", total_tris + len(core.data.polygons))
    print("RELIC crack segs  ", seg_count)
    print("RELIC min/max tris", min(p["tris"] for p in manifest["pieces"]),
          max(p["tris"] for p in manifest["pieces"]))
    print("RELIC fbx         ", fbx_path)
    print("RELIC mask        ", mask_path)


if __name__ == "__main__":
    main()
