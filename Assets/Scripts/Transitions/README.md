# Ground-up MR → VR reveal

This folder contains a mobile-XR-friendly URP implementation of the ground-up reconstruction
described in `docs/mr-to-vr-transition-effects.md`.

## Use

1. Use `MRBase/Transitions/Ground Up Reveal Lit` on geometry that belongs to the VR world.
2. Add `GroundUpRevealController` to an object located at the virtual floor height.
3. Set `World Height` high enough to pass the highest participating mesh, then call `Play()`.
4. Connect `On Fully Revealed` to the platform passthrough adapter. At that point all VR geometry
   has covered the transition range and the shader has returned to its normal opaque path.

All participating materials share global transition values. Their base textures and surface
settings remain per-material, while the moving front does not create material instances.

`GroundUpReveal.hlsl` also exposes `MRBaseGroundUpReveal_float` and
`MRBaseGroundUpReveal_half` for a Shader Graph Custom Function node. Feed its `keep` output to
Alpha and use an Alpha Clip Threshold of zero; add `edge` and `grid` multiplied by the global edge
colour to Emission. This lets an existing project Shader Graph keep its own lighting model.

The reveal clip is present in the forward, shadow-caster, and depth-only passes, so hidden objects
do not leave depth or shadow silhouettes. The implementation uses opaque alpha clipping rather
than full-screen transparency to keep overdraw suitable for PICO and Quest.
