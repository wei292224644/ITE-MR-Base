## 1. Prefab and tablet setup

- [x] 1.1 Create flat inscribed stone Prefab (Shell + Tablet body, slight shell offset to avoid z-fighting)
- [x] 1.2 Author sealed decay shell look; tablet + **flat TMP** inscriptions: faded gray → **vermillion** restore (no relief mesh; no audio)
- [x] 1.3 Add colliders / XR poke or near-interaction on Prefab root

## 2. Dissolve and sacred gold look

- [x] 2.1 Instance Burn (or Custom) Dissolve on Shell; tune warm honey/amber gold edge (sacred awaken, not white-hot scorch)
- [x] 2.2 Wire `Dissolver` (+ `DissolverVFX` if used); verify dissolve amount at runtime
- [x] 2.3 Optional: touch world position → dissolve center/axis; else touch-point gold flash fallback

## 3. Chunk-to-ash VFX

- [x] 3.1 Primary VFX: larger chunk/debris along shell normals (from MasterKit templates)
- [x] 3.2 Secondary fine-ash (cooler, lighter, slight lift) parallel or on chunk death
- [x] 3.3 Sync play/stop and dissolve amount via `DissolverVFX` (or equivalent)

## 4. Inscription color restore

- [x] 4.1 Expose `_RestoreAmount` (or equivalent) on tablet material; 0 = faded, 1 = original-era color
- [x] 4.2 Drive restore on mid-to-late awaken curve so color return peaks after shell is clearly collapsing
- [x] 4.3 Validate contrast: sealed = 年久褪色; awakened = 朱砂朱红字醒 (gold only on dissolve edge)

## 5. Awaken driver and state machine

- [x] 5.1 Implement `SacredRelicAwaken`: Sealed → Awakening → Awakened
- [x] 5.2 On first valid touch: lock input, dissolve shell, start VFX, advance inscription restore
- [x] 5.3 On end: hide Shell, settle gold afterglow, leave quiet restored tablet
- [x] 5.4 Debug/editor reset to Sealed (shell on, restore=0, VFX stop, dissolve reset)

## 6. Integration and validation

- [x] 6.1 Place Prefab in demo scene; verify Space/click one-shot; R/Reset to Sealed; ignore re-touch when Awakened
- [x] 6.2 Tune timing: gold seep → chunks → ash → vermillion restore → quiet; confirm sacred awaken (not burn/wash)
- [x] 6.3 Spot-check particle peak in Editor (device XR poke optional, not required for Demo pass)
