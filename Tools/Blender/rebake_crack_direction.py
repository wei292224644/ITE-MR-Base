"""Re-bake the crack mask and its per-piece timings so the fracture travels from a corner.

Why this exists: the Directional spread mode in SacredRelicFracture can only fade a shard's
whole painted crack network in and out, because _Progress is one scalar per shard. Real
growth needs the per-pixel arrival channel, and that channel is produced here — so the
direction has to be baked, not applied at runtime.

Touches nothing but the mask PNG and the arrive/detach fields of the manifest. No mesh, no
FBX, no .blend, so the hand-repaired stele normals are never at risk.

    blender --background --factory-startup \
        --python Tools/Blender/rebake_crack_direction.py -- --corner top-left
"""

from __future__ import annotations

import argparse
import importlib.util
import json
import math
import os
import random
import sys

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
GENERATED = os.path.join(REPO, "Assets/SacredRelicDemo/Generated")
MANIFEST = os.path.join(GENERATED, "Models/SacredRelic_Fractured.json")
MASK = os.path.join(GENERATED, "Textures/T_SacredRelic_CrackMask.png")

# The shipped Shell_Piece_* meshes were cut with these. They must not change here: the FBX
# is not being regenerated, so the painted cracks have to match the geometry already in it.
SHIPPED_SEG_LEN = 0.012
SHIPPED_JAG = 0.014
SHIPPED_CELLS = 48

CORNERS = {
    "top-left": (-0.5, 0.5),
    "top-right": (0.5, 0.5),
    "bottom-left": (-0.5, -0.5),
    "bottom-right": (0.5, -0.5),
    "centre": (0.0, 0.0),
}


def load_shared():
    path = os.path.join(REPO, "Tools/Blender/gen_sacred_relic.py")
    spec = importlib.util.spec_from_file_location("gen_sacred_relic", path)
    mod = importlib.util.module_from_spec(spec)
    sys.modules["gen_sacred_relic"] = mod
    spec.loader.exec_module(mod)
    return mod


def parse_args():
    argv = sys.argv
    argv = argv[argv.index("--") + 1:] if "--" in argv else []
    p = argparse.ArgumentParser()
    p.add_argument("--corner", default="top-left", choices=sorted(CORNERS))
    p.add_argument("--mask", type=int, default=2048, help="long-axis mask resolution")
    return p.parse_args(argv)


def main():
    args = parse_args()
    G = load_shared()

    manifest = json.load(open(MANIFEST))
    outer_w = manifest["outerWidth"]
    outer_h = manifest["outerHeight"]
    seed = manifest["seed"]
    crack_width = outer_h * 0.0013

    rng = random.Random(seed)
    seeds = G.gen_seeds(outer_w, outer_h, SHIPPED_CELLS, rng)
    cells = G.voronoi_cells(seeds, outer_w, outer_h)
    table = G.build_edge_table(cells, outer_w, outer_h, SHIPPED_SEG_LEN, SHIPPED_JAG)

    # Reproduce the ORIGINAL centre-out ordering first. Shell_Piece_NNN was named after
    # sorting by that detach, so this is the only way to know which cell is which piece.
    G.build_propagation(table)
    G.cell_timings(cells, table)
    ordered = sorted(cells, key=lambda c: (c["detach"], math.hypot(*c["seed"])))

    pieces = manifest["pieces"]
    if len(ordered) != len(pieces):
        raise SystemExit(f"cell count {len(ordered)} != manifest pieces {len(pieces)}")

    # Verify the mapping against the seed2D the exporter recorded, or we would silently
    # hand every shard someone else's timing.
    worst = 0.0
    for cell, piece in zip(ordered, pieces):
        dx = cell["seed"][0] - piece["seed2D"][0]
        dy = cell["seed"][1] - piece["seed2D"][1]
        worst = max(worst, math.hypot(dx, dy))
    if worst > 1e-4:
        raise SystemExit(f"cell/piece mapping does not match manifest seed2D (worst {worst})")
    print(f"REBAKE mapping verified against seed2D, worst mismatch {worst:.2e}")

    # Now re-run propagation from the chosen corner and overwrite only the timings.
    cx, cy = CORNERS[args.corner]
    origin = (cx * outer_w, cy * outer_h)
    G.build_propagation(table, origin_xy=origin)
    G.cell_timings(cells, table)

    for cell, piece in zip(ordered, pieces):
        piece["arrive"] = round(cell["arrive"], 5)
        piece["detach"] = round(cell["detach"], 5)

    first = min(pieces, key=lambda p: p["detach"])
    last = max(pieces, key=lambda p: p["detach"])
    print(f"REBAKE corner={args.corner} origin={origin}")
    print(f"REBAKE first piece {first['name']} centroid={first['centroid']}")
    print(f"REBAKE last  piece {last['name']} centroid={last['centroid']}")

    segs = G.rasterise_mask(MASK, table, cells, outer_w, outer_h, args.mask, crack_width)

    manifest["crackOrigin"] = args.corner
    with open(MANIFEST, "w") as fh:
        json.dump(manifest, fh, indent=2)

    print(f"REBAKE segments={segs}")
    print(f"REBAKE mask     {MASK}")
    print(f"REBAKE manifest {MANIFEST}")


if __name__ == "__main__":
    main()
