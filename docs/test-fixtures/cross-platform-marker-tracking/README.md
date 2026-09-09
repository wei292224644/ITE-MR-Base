# PICO QR + ArUco A3 and dual-A4 test fixtures

> **过时**：PICO 原生 ArUco 已被真机证伪。现行打印夹具在 `docs/test-fixtures/pico-camera-fiducial-tracking/`（QR + AprilTag）。本目录只作历史对照，不要再按这里的 ArUco 尺寸做新试验。

These fixtures isolate marker-tracking architecture feasibility from later
business modes such as one-shot triggers and repeated localization scans.

## Files

- `pico_qr_aruco_static_id0_a3_landscape.pdf`: preferred one-page A3 fixture
  for PICO static marker ID 0 and QR payload `0`.
- `pico_qr_aruco_dynamic_id250_a3_landscape.pdf`: preferred one-page A3
  fixture for PICO dynamic marker ID 250 and QR payload `250`.
- `pico_qr_aruco_static_id0_a4.pdf`: two A4 pages for PICO static marker ID 0
  and QR payload `0`; fallback when A3 printing is unavailable.
- `pico_qr_aruco_dynamic_id250_a4.pdf`: two A4 pages for PICO dynamic marker
  ID 250 and QR payload `250`; fallback when A3 printing is unavailable.
- `*_preview_300dpi.png`: 4960 × 3508 pixel side-by-side mounting previews;
  print the corresponding PDF, not the PNG.

## Marker facts

- ArUco dictionary: OpenCV `DICT_4X4_1000`.
- Static fixture: dictionary ID 0, inner 4 × 4 white-bit value `0xB532`.
- Dynamic fixture: dictionary ID 250, inner 4 × 4 white-bit value `0x7E80`.
- The marker artwork is cropped from PICO's supplied `A4_0_static.pdf` and
  `A4_250_dynamic.pdf`; it is not reconstructed from a visually similar symbol.
- PICO's supplied sets partition IDs 0–249 as static and 250–499 as dynamic.
  This naming/range split is established by the supplied assets. The public API
  does not document enough behavior to infer the runtime difference, so both
  fixtures must be exercised and logged on a device.
- Dictionary identification was independently checked against OpenCV's official
  predefined dictionary data.

References:

- PICO demo: <https://github.com/picoxr/ArUcoMarkerTracking>
- PICO room marker guide: <https://business.picoxr.com/global/doc/RoomMarkerLayoutRecommendationsandGuide>
- OpenCV ArUco dictionaries: <https://docs.opencv.org/master/de/d67/group__objdetect__aruco.html>
- Deterministic QR matrices: Nayuki QR Code generator, Version 1 / ECC M,
  <https://github.com/nayuki/QR-Code-generator>

## Physical layout

- Preferred A3 fixture: one landscape page, 420 × 297 mm.
- Dual-A4 fallback: two portrait pages, each 210 × 297 mm. Page 1 is the left
  QR sheet and page 2 is the right ArUco sheet. Mount them upright with their
  top/bottom edges aligned and inner page edges touching.
- QR outer square: 160 × 160 mm, including the required four-module quiet zone.
- ArUco outer black square: 160 × 160 mm.
- In both formats, QR is left of ArUco, their rotations match, vertical
  displacement is zero, and physical center-to-center distance is 210 mm.

For clarity, a 160 mm QR outer square includes its white quiet zone. Its 21 × 21
module symbol occupies about 115.86 mm; each module is about 5.52 mm and each
side of the quiet zone is about 22.07 mm.

The 210 mm value is a physical center-to-center displacement. Do not encode it
as a Unity local-space vector until a device log confirms PICO's marker pose
axes, handedness, origin, and orientation convention.

## Printing

Prefer the A3 PDF: print one A3 landscape page using **100% / Actual Size**.
Disable Fit, Shrink, Scale, and borderless enlargement. If A3 is unavailable,
print both fallback PDF pages on A4 portrait paper with the same settings, then
mount both sheets on one flat, rigid surface; join their page edges without
overlap or gap and tape from the back.

After printing, measure **both** of the following and record the measured
values (not the nominal ones) in the probe session log:

1. **ArUco outer black square**: 160 mm on each side, tolerance ±1 mm. This
   catches print scaling.
2. **QR outer square center to ArUco outer square center**: 210 mm, tolerance
   ±1 mm. This is the number that later becomes `ArUcoToQrOffset`.

Measuring (1) does **not** establish (2) on the dual-A4 fallback. There the
210 mm is produced by hand-butting two pages, so a 3 mm misalignment leaves the
ArUco square perfectly 160 mm while the offset is off by 3 mm — an error the
5 cm pose target will happily pass, and every later marker localization will
carry. Measure the center distance on **every** dual-A4 fixture; on A3, where a
single page scale ties the two dimensions together, spot-check once per print
batch.

If the measured center distance is outside tolerance, either use the measured
value for pose acceptance or keep the fixture out of pose tests.

Keep the fixture flat and avoid glossy lamination.

For the 160 mm PICO fixture, start device testing within the guide's recommended
0.2–0.8 m observation range, then deliberately sample outside it for logs.

## Regeneration

> **macOS only.** The generator is a Swift script that draws via CoreGraphics,
> so regenerating the PDFs requires a Mac with a Swift toolchain. The generated
> PDFs and previews are committed, so day-to-day work and CI never need to run
> it — only geometry changes do.

From the repository root:

```sh
env CLANG_MODULE_CACHE_PATH=/tmp/mrbase_marker_swift_cache \
  SWIFT_MODULECACHE_PATH=/tmp/mrbase_marker_swift_cache \
  swift Tools/MarkerFixtures/generate_pico_qr_aruco_fixtures.swift \
  '/Users/wwj/Downloads/static marker/A4_0_static.pdf' \
  '/Users/wwj/Downloads/dynamic marker/A4_250_dynamic.pdf' \
  docs/test-fixtures/cross-platform-marker-tracking
```

The generator embeds fixed QR module matrices so output does not depend on
Core Image behavior or hardware acceleration.

## Verification record

The QR half of each generated preview was downscaled to a normal decoder input
size and decoded with `zbarimg --quiet --raw`:

```text
pico_qr_aruco_static_id0_a3_landscape_preview_300dpi.png  -> 0
pico_qr_aruco_dynamic_id250_a3_landscape_preview_300dpi.png -> 250
pico_qr_aruco_static_id0_a4_preview_300dpi.png  -> 0
pico_qr_aruco_dynamic_id250_a4_preview_300dpi.png -> 250
```

All previews report 4960 × 3508 pixels at 300 DPI. Each A3 PDF contains one page
with MediaBox `1190.551 × 841.8898 pt`; each fallback PDF contains two pages
with A4 MediaBox `595.2756 × 841.8898 pt`.

Source PDF SHA-256:

```text
f5bb7ebfdc2844c024d75564eae3afa5fafcfff65a0f5d33c66b82563c16849d  A4_0_static.pdf
a122b7f8f0e0b94b300a1a678a78c5d2945d97bbeb7388f2c8bdf2cf3a8ce540  A4_250_dynamic.pdf
```

Generated PDF SHA-256:

```text
4aa969097dbebf232e9e4725f6b2ec7b8ed5975848b3ac9bea0a935ec5d356ca  pico_qr_aruco_static_id0_a3_landscape.pdf
76e340775a25a7cd8f6e54874a21ad8bbf3af9c3ce5e502593a7b4a007c114b7  pico_qr_aruco_dynamic_id250_a3_landscape.pdf
99f3fd4d5826077d27a55cb43e53907ecf4e8868a62e37ed38bfa8f5f8424705  pico_qr_aruco_static_id0_a4.pdf
81911f70b834c0a7aafd594e23ab67ed6610f0afba2f5dfc4f01d2ce884db95f  pico_qr_aruco_dynamic_id250_a4.pdf
```

Generated preview SHA-256:

```text
82cf843de5d9bfdaa18f5d088518c885ae87af80aa7aa7bf52055f0a9ed76fce  pico_qr_aruco_static_id0_a3_landscape_preview_300dpi.png
0cffe7897db394bac4324c79155668eb42688a01bb539210031a9389f562a6d2  pico_qr_aruco_dynamic_id250_a3_landscape_preview_300dpi.png
f27062088e929874e4706d337da2510f54135c8fbc9db13f6b3ed8f04af141e7  pico_qr_aruco_static_id0_a4_preview_300dpi.png
379344f4d526f2115b980d3f8743a09c5b8ca0224669c44e3cd3e448fe4e0214  pico_qr_aruco_dynamic_id250_a4_preview_300dpi.png
```

## Redact device logs before committing

Keep the original device JSONL outside the repository. Generate a new representative copy:

```bash
python3 Tools/MarkerProbe/redact_marker_probe_jsonl.py \
  /path/to/device-session.jsonl \
  /path/to/representative-session.jsonl
```

The tool refuses to overwrite either source or an existing output. It removes QR plaintext
and unique device/account/network fields, scrubs their values from free-form messages, and
converts every Unity world Pose to the coordinate frame of the first world Pose in that run.
The retained MarkerID, RawPayload length/SHA-256, relative motion, timing, state, and SDK
results remain suitable for architecture review without publishing the test location.

Run its built-in regression check with:

```bash
python3 Tools/MarkerProbe/redact_marker_probe_jsonl.py --self-test
```
