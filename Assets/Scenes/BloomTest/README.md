# BloomTest

Isolated scene for evaluating URP Bloom. Does **not** touch the shipping
`Standalone Performant Preset` / `Performant URP Renderer Config`.

## How to view it

The project's active pipeline has `supportsHDR = false` and a null
`postProcessData`, so Bloom renders as a no-op in the default setup. To see
the effect:

1. Open `BloomTest.unity`
2. Project Settings → Quality → set the current level's Render Pipeline Asset
   to `BloomTest URP Asset.asset`
3. Restore the original asset when finished

## What the assets differ in

| Asset | Change vs. shipping config |
|---|---|
| `BloomTest URP Asset` | `supportsHDR = true`, `HDRColorBufferPrecision = 64bit` (R16G16B16A16 — keeps alpha, which passthrough compositing needs; the 32-bit R11G11B10 default has no alpha channel) |
| `BloomTest Renderer` | `postProcessData` pointed at URP's built-in `PostProcessData` |

## Scene layout

Six spheres, URP/Lit with `_EMISSION`, emission color = base × k for
k ∈ {0.5, 1, 2, 4, 8, 16}. Bloom threshold is 1.0, so k ≤ 1 stays dark and
k ≥ 2 blooms — the ladder makes the threshold cutoff directly visible.

Bloom override settings are mobile-oriented: `highQualityFiltering = false`
(Fast Mode), `maxIterations = 4`, `scatter = 0.7`.

Emissive materials need `globalIlluminationFlags = RealtimeEmissive`. With
`None` or `EmissiveIsBlack`, `MaterialEditor.FixupEmissiveFlag` strips the
`_EMISSION` keyword on save and emission silently stops rendering.

## Not covered by this scene

- Device performance. Editor rendering says nothing about Adreno tile bandwidth.
  Measure with OVR Metrics / RenderDoc on hardware.
- XR single-pass instanced rendering. Only a device build exercises that path.
- Passthrough alpha. Bloom bleeding past object silhouettes onto the real-world
  feed can only be judged on-device.
