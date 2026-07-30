"""Generate T_RelicDustGrain.png — the billboard sprite for the relic's shell turning to sand.

Why this is not a soft round blob: the particles are 0.07-0.22 m on a 16 m stele, so a handful of
them cover a shard's eroding edge. One smooth gaussian per billboard reads as fog or as a bloom
sprite. A clump of a few hard-edged irregular grains reads as sand even when each billboard is only
a few pixels across, because the silhouette stays broken instead of resolving to a circle.

Alpha carries the shape; RGB stays near-white with only mild value variation, because the colour
comes from M_Relic_Dust's _BaseColor and the particle system's colorOverLifetime gradient. Alpha is
forced to 0 at the border so the quad edge never shows.

Pure stdlib — writes the PNG by hand, no PIL/numpy.

    python3 Tools/Textures/gen_dust_grain.py
"""

from __future__ import annotations

import math
import os
import random
import struct
import zlib

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(REPO, "Assets/SacredRelicDemo/Generated/Textures/T_RelicDustGrain.png")

SIZE = 128
SEED = 20260730
# Tuned against the sprite it replaces: the old soft blob had mean alpha 0.157, and mips average a
# distant billboard down to exactly that number, so going much sparser would make the dust fade out
# at range even though it looks stronger up close.
GRAINS = 46


def write_png(path: str, size: int, pixels: list[tuple[int, int, int, int]]) -> None:
    """Minimal RGBA8 PNG writer. One IDAT, filter type 0 on every scanline."""
    raw = bytearray()
    for y in range(size):
        raw.append(0)
        for x in range(size):
            raw.extend(pixels[y * size + x])

    def chunk(tag: bytes, data: bytes) -> bytes:
        return (struct.pack(">I", len(data)) + tag + data
                + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF))

    ihdr = struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0)
    with open(path, "wb") as fh:
        fh.write(b"\x89PNG\r\n\x1a\n")
        fh.write(chunk(b"IHDR", ihdr))
        fh.write(chunk(b"IDAT", zlib.compress(bytes(raw), 9)))
        fh.write(chunk(b"IEND", b""))


def smoothstep(edge0: float, edge1: float, x: float) -> float:
    """HLSL semantics, including edge1 < edge0 for a falling ramp — both callers rely on that."""
    if edge1 == edge0:
        return 0.0 if x < edge0 else 1.0
    t = min(max((x - edge0) / (edge1 - edge0), 0.0), 1.0)
    return t * t * (3.0 - 2.0 * t)


def main() -> None:
    rng = random.Random(SEED)

    # Each grain is a small convex polygon — the intersection of 5-7 random half-planes — because
    # weathered mineral grit is angular. Sinusoidal outline wobble was tried first and produced
    # star/snowflake silhouettes, which is the one shape sand never has.
    grains = []
    for _ in range(GRAINS):
        # Centre-weighted so the billboard reads as a clump with a dense core and stragglers,
        # rather than an evenly filled square.
        cx = min(max(0.5 + rng.gauss(0.0, 0.17), 0.07), 0.93)
        cy = min(max(0.5 + rng.gauss(0.0, 0.17), 0.07), 0.93)
        radius = rng.uniform(0.030, 0.088)
        sides = rng.randint(5, 7)
        # Random offsets per face give elongated and chunky grains from the same generator.
        base = rng.uniform(0, math.tau)
        planes = []
        for k in range(sides):
            theta = base + math.tau * (k + rng.uniform(-0.18, 0.18)) / sides
            planes.append((math.cos(theta), math.sin(theta),
                           radius * rng.uniform(0.62, 1.0)))
        grains.append({
            "cx": cx, "cy": cy, "r": radius, "planes": planes,
            "value": rng.uniform(0.66, 1.0),
            # In fractions of the radius. Kept tight: a wide falloff on a grain this small is
            # just a blur, and the whole point is a hard silhouette.
            "soft": radius * rng.uniform(0.16, 0.34),
            "opacity": rng.uniform(0.62, 1.0),
        })

    # Static speckle, sampled per pixel, to roughen every grain's interior and edge alike.
    speck = [rng.random() for _ in range(SIZE * SIZE)]

    pixels: list[tuple[int, int, int, int]] = []
    for y in range(SIZE):
        for x in range(SIZE):
            u = (x + 0.5) / SIZE
            v = (y + 0.5) / SIZE

            alpha = 0.0
            value = 0.0
            for g in grains:
                dx = u - g["cx"]
                dy = v - g["cy"]
                if dx * dx + dy * dy > g["r"] * g["r"] * 2.2:
                    continue
                # Convex signed distance: outside if any half-plane rejects the point.
                sd = max(nx * dx + ny * dy - d for nx, ny, d in g["planes"])
                # 0 on the outline, 1 once fully inside by the soft margin.
                fall = smoothstep(0.0, -g["soft"], sd)
                a = fall * g["opacity"]
                if a > alpha:
                    value = g["value"]
                    alpha = a
                elif a > 0.0:
                    # Overlapping grains accumulate a little, but never past opaque.
                    alpha = min(1.0, alpha + a * 0.35)

            if alpha <= 0.0:
                pixels.append((0, 0, 0, 0))
                continue

            # Roughen: the speckle bites into alpha, hardest where alpha is already low, so the
            # silhouette gets ragged rather than the interior getting blotchy.
            s = speck[y * SIZE + x]
            alpha *= 1.0 - 0.30 * s * (1.0 - alpha)
            value *= 0.86 + 0.14 * s

            # Guarantee nothing survives at the quad border.
            alpha *= smoothstep(0.5, 0.42, max(abs(u - 0.5), abs(v - 0.5)))

            c = int(round(min(1.0, value) * 255))
            pixels.append((c, c, c, int(round(min(1.0, max(0.0, alpha)) * 255))))

    write_png(OUT, SIZE, pixels)

    covered = sum(1 for p in pixels if p[3] > 8)
    mean_a = sum(p[3] for p in pixels) / len(pixels) / 255.0
    border = max(max(pixels[y * SIZE + 0][3], pixels[y * SIZE + SIZE - 1][3])
                 for y in range(SIZE))
    border = max(border, max(pixels[x][3] for x in range(SIZE)),
                 max(pixels[(SIZE - 1) * SIZE + x][3] for x in range(SIZE)))
    print(f"DUST {SIZE}x{SIZE} grains={GRAINS} coverage={covered / len(pixels):.3f} "
          f"meanAlpha={mean_a:.3f} maxBorderAlpha={border}")
    print(f"DUST wrote {OUT}")


if __name__ == "__main__":
    main()
