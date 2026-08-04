# Marker Probe implementation boundary review

Reviewed: 2026-08-03

This note records task 2.1 of the `cross-platform-marker-tracking` change. The
review used CodeGraph call-path exploration first, followed by exact source,
assembly-definition, build-profile, and serialized scene/prefab checks.

## Existing production path

```text
MarkerTrackingBootstrapper.Start
  -> MarkerAnchorService.Initialize
  -> MarkerTrackingBootstrapper.CreateProvider
       -> QuestMarkerProvider       (MRBASE_QUEST)
       -> PicoMarkerProvider        (MRBASE_PICO && MRBASE_HAS_PICO_SDK)
  -> MarkerAnchorService.BindProviderForTesting
  -> IMarkerTrackingProvider.StartTracking
```

`MarkerAnchorService` owns stabilization, registry lookup, and business object
creation. The Probe must not call it or implement its responsibilities.

No `.unity`, `.prefab`, or `.asset` file currently references the script GUIDs
of `MarkerTrackingBootstrapper`, `QuestMarkerProvider`, or
`PicoMarkerProvider`. The production path exists in code but is not serialized
into the current checked-in scenes.

## PICO enterprise-service ownership and callback slot

The only current project call path is:

```text
PicoMarkerProvider.StartTracking
  -> PXR_Enterprise.InitEnterpriseService
  -> PXR_Enterprise.BindEnterpriseService
  -> PicoMarkerProvider.OnEnterpriseServiceBound
  -> PXR_Enterprise.SetMarkerInfoCallback

PicoMarkerProvider.StopTracking
  -> PXR_Enterprise.UnBindEnterpriseService
```

There are no project calls to `PXR_Enterprise.ScanQRCode` yet. The TOB Marker
API uses a single `setMarkerInfoCallback` slot and exposes no callback
unregister operation. A Probe and `PicoMarkerProvider` therefore cannot safely
run together: a later registration silently replaces the earlier callback,
and `PicoMarkerProvider.StopTracking` globally unbinds the enterprise service.

Probe constraints:

- use a dedicated diagnostic scene with no `MarkerTrackingBootstrapper`;
- refuse Probe startup if an enabled production bootstrapper/provider is found;
- let the Probe perform and log its own Init + Bind for this diagnostic change;
- never call global Unbind when the Probe stops;
- use session/run generations to discard callbacks after stop or timeout.

## Assembly and platform boundaries

`MRBase.Localization` contains vendor-neutral contracts and business-localization
code. It references only `MRBase.Common`. Vendor-neutral Probe data models,
parsing, JSONL records, and log writer belong in this assembly.

`MRBase.Localization.Native` references `MRBase.Localization`, MRUK/Oculus, and
PICO assemblies. It defines `MRBASE_HAS_MRUK` and `MRBASE_HAS_PICO_SDK` from
package versions. Quest/PICO Probe adapters and the diagnostic runner belong in
this assembly and must retain platform compilation guards:

- Quest: `MRBASE_QUEST` (and package availability where required);
- PICO: `MRBASE_PICO && MRBASE_HAS_PICO_SDK`;
- configured target without required package: explicit unavailable/error log.

Build intent comes from the checked-in Quest/PICO build profiles and
`BuildScript`, which supply `MRBASE_QUEST` or `MRBASE_PICO`. The native SDK
availability symbol is an independent version define and must not be treated as
build intent.

`MRBase.Localization.Tests` references the vendor-neutral localization
assembly, Unity Test Runner, and NUnit, but not the native assembly. Core Probe
models/parser/logger can be tested there without bringing vendor SDK types into
EditMode tests. Native behavior should be driven through vendor-neutral Probe
interfaces/fakes when later tasks add adapter tests.

## Minimal insertion point

The implementation should add, without changing existing provider contracts:

1. vendor-neutral Probe session/config/event/parser/logger types under
   `Assets/Scripts/Localization/Probe/`;
2. Quest/PICO native adapters and a debug-only runtime entry under
   `Assets/Scripts/Localization/Native/Probe/`;
3. a dedicated diagnostic scene or explicit runtime entry that is absent from
   production scenes and defaults to disabled;
4. EditMode tests in `Assets/Tests/EditMode/` through public Probe interfaces.

This boundary keeps `IMarkerTrackingProvider`, `MarkerAnchorService`, and the
existing Bootstrapper unchanged while preserving direct access to native facts
needed by the feasibility test.
