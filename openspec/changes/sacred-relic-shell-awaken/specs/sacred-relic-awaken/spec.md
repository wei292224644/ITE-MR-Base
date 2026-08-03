## ADDED Requirements

### Requirement: Flat inscribed stone tablet form
The system SHALL present the relic as a flat stone tablet whose primary face carries carved (or inscribed) writing.

#### Scenario: Recognizable tablet
- **WHEN** the relic is viewed in the sealed or awakened state
- **THEN** it reads as a stone slab/tablet with inscriptions on its face
- **AND** it MUST NOT require a box/casket opening structure for the first version

### Requirement: Dual-layer shell and tablet structure
The system SHALL use an outer sealed decay shell and an underlying tablet body that remains present while sealed.

#### Scenario: Initial sealed state
- **WHEN** the relic is spawned or scene-loaded before awakening
- **THEN** the decay shell is visible as the weathered/sealed covering
- **AND** the tablet body exists in the hierarchy (MAY be occluded by the shell)
- **AND** inscriptions appear aged: faded, desaturated, or otherwise color-lost relative to their restored look

### Requirement: Touch triggers sacred light awakening
The system SHALL start the awakening sequence on a single valid near-interaction or poke/touch, treating the touch as ritual light entering the seal.

#### Scenario: First valid touch
- **WHEN** a valid XR poke or near-touch contacts the relic while it is Sealed
- **THEN** the system transitions to Awakening and begins shell dissolve/VFX and inscription restore
- **AND** further touches MUST NOT restart or stack another sequence until a debug reset occurs

#### Scenario: Touch ignored after awakened
- **WHEN** the relic is already Awakened
- **AND** the user touches it again
- **THEN** the system MUST NOT replay the collapse and restore sequence

### Requirement: Sacred gold edge and inward seepage look
During awakening the shell dissolve edge SHALL use warm sacred gold (honey/amber gold), reading as light seeping from within rather than harsh white-hot burn.

#### Scenario: Gold edge during collapse
- **WHEN** the shell dissolve amount is advancing
- **THEN** the dissolve frontier exhibits warm sacred-gold edge lighting
- **AND** the tone MUST NOT read as aggressive sunlight scorch

### Requirement: Chunk-to-ash collapse VFX
As the sealed shell collapses, the system SHALL emit larger decay chunks first, then fine ash.

#### Scenario: Chunk then ash
- **WHEN** awakening is in progress
- **THEN** larger debris-like particles appear along the collapsing surface (or along normals)
- **AND** fine ash/dust follows or spawns as chunks expire, lighter and cooler than the gold edge
- **AND** the shell is hidden or fully dissolved by sequence end

### Requirement: Inscription color restoration
The system SHALL restore inscription appearance from an aged faded state to a vivid vermillion (朱砂朱红) primary coloration as part of awakening (the emotional climax is regained color, not merely a cleaned blank stone). Sacred gold MUST remain associated with light/dissolve edge, not as the primary restored inscription fill for this Demo.

#### Scenario: Faded to restored vermillion
- **WHEN** awakening reaches the mid-to-late phase
- **THEN** inscription colors transition from faded/desaturated (or color-lost) toward a vermillion-dominant restored look
- **AND** by Awakened state the inscriptions MUST read as clearly vermillion-colored relative to the sealed faded look

### Requirement: Demo flat inscription presentation
For the Demo, inscriptions SHALL be presented as flat text (TMP or face texture), not relief-carved mesh.

#### Scenario: Flat text demo
- **WHEN** the Demo Prefab is viewed
- **THEN** inscriptions are readable as flat text on the tablet face
- **AND** their color participates in the faded→vermillion restore

### Requirement: Demo has no audio requirement
The Demo SHALL NOT require sound effects or audio hooks to be considered complete.

#### Scenario: Silent demo pass
- **WHEN** the awakening sequence completes in the Demo
- **THEN** visual success criteria alone determine pass
- **AND** absence of audio MUST NOT fail the Demo

### Requirement: Editor demo success criteria
The Demo SHALL be passable in the Editor without real-hand XR poke.

#### Scenario: Keyboard or click awaken
- **WHEN** the operator presses the demo trigger (e.g. Space) or clicks the relic while Sealed
- **THEN** one full sequence plays: shell collapse with chunk-to-ash, vermillion restore, quiet settle
- **AND** a further trigger MUST NOT replay until reset
- **AND** a demo reset MUST return the relic to Sealed for re-preview

#### Scenario: Tablet body continuity
- **WHEN** awakening completes
- **THEN** the revealed tablet MUST be the same tablet object that existed while sealed
- **AND** restoration MUST NOT be implemented solely by spawning a new tablet at the final frame

### Requirement: Quiet settle after restore
After shell collapse and color restore, sacred gold SHALL settle to a brief afterglow then fade; the resting state is a quiet tablet with restored inscription color.

#### Scenario: Awakened quiet state
- **WHEN** the awakening sequence completes
- **THEN** the decay shell is no longer a solid covering
- **AND** restored inscription color remains visible
- **AND** strong sacred-gold highlight fades within a short settle window

### Requirement: Awakening state machine
The system SHALL expose Sealed → Awakening → Awakened (optional debug reset to Sealed).

#### Scenario: One-shot progression
- **WHEN** a sealed relic receives a valid trigger
- **THEN** it moves Sealed → Awakening → Awakened once per runtime instance (unless debug reset)

#### Scenario: Debug replay
- **WHEN** a debug/editor reset is invoked
- **THEN** the relic MAY return to Sealed with shell restored, restore amount reset, and VFX stopped
