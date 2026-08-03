# Probe Report: sacred-relic-shell-awaken

> Generated: 2026-07-27
> Summary: 4 probe questions this session (+ prior explore decisions), 12+ confirmed decisions, 3 open assumptions

## Confirmed decisions

### Scope & intent

- **Question**: What is this change for, and what is out of scope?
- **AI recommendation**: Demo-first visual validation of “sacred awaken + inscription color restore”, not full product pipeline (evidence: `proposal.md` DECIDED Demo 表现验证; explore conversation).
- **User confirmation**: Converge on demo; plane inscribed tablet; no casket; no wipe-paint; no audio for this demo.

- **Question**: What is the emotional climax?
- **AI recommendation**: Inscriptions return to “当年色彩”, not merely cleaned stone (evidence: user — 「年久失修…焕发当年的色彩」).
- **User confirmation**: Yes — faded aged inscriptions → restored period color.

### Impact

- **Question**: What systems are touched?
- **AI recommendation**: New Prefab/scripts under project Assets; reuse INab Dissolve-FX / MasterKit optionally; XR poke optional with Editor Space/click for demo (evidence: `Assets/INab Studio/Dissolve-FX`, existing `SacredRelicAwaken.cs` skeleton).
- **User confirmation**: Implicit via demo scope — Editor trigger sufficient for pass criteria; real-hand Poke not required for Demo pass.

### Design

- **Question**: Restored inscription color — vermillion fill vs gold outline?
- **AI recommendation**: 朱砂朱红为主; sacred gold only on dissolve edge / light seep (evidence: avoids gold-on-gold muddying with 圣物苏醒 edge; historical朱砂填彩 association — general assumption for Chinese tablet fill).
- **User confirmation**: 可以.

- **Question**: Inscription geometry for Demo — TMP/flat vs relief mesh?
- **AI recommendation**: Flat TMP / simple face text for Demo; relief later (evidence: `proposal.md` Demo 不依赖最终美术; geometry does not change narrative beat).
- **User confirmation**: 可以.

- **Question**: Audio for first Demo?
- **AI recommendation**: No audio this Demo (evidence: visual-first validation in `proposal.md`).
- **User confirmation**: 不做音效.

- **Question** (prior explore): Visual metaphor?
- **AI recommendation**: Touch = ritual light; sealed shell collapses chunk→ash; core tablet always present; sacred honey-gold seep (evidence: explore — 「1000年未见光」「圣物苏醒」).
- **User confirmation**: Affirmed across explore; dual-layer + Burn-style gold + chunk→ash.

### Success criteria

- **Question**: What proves the Demo passes?
- **AI recommendation**:
  1. Editor Space/click runs one full sequence (shell collapse → chunk→ash → vermillion restore → quiet)
  2. Re-touch does not replay; R/Reset returns to Sealed
  3. Observer reads “sacred awaken + vermillion words”, not “burned away” or “washed white”
- **User confirmation**: 可以 (real-hand Poke not required for pass).

### Failure / risks (addressed lightly; mitigations carried)

- Gold edge reads as scorch → keep warm honey gold, cooler ash, vermillion climax contrast.
- Particles only fine dust → keep two-stage chunk then ash.
- Accidental re-trigger → one-shot state machine + Reset.

## Open assumptions [NEEDS CLARIFICATION]

These remain unverified or deferred; carry into artifacts:

- [ ] `[ASSUMED]` Demo shell may use INab Burn Dissolve material **or** a lightweight dissolve fallback driven by `SacredRelicAwaken` — whichever wires faster in Editor without blocking the beat — affects: `design.md` Decisions / tasks 2.x–3.x
- [ ] `[ASSUMED]` Tablet placement in Demo scene is upright slab at comfortable view distance (not floor plaque) — affects: Prefab default transform
- [ ] `[ASSUMED]` Inscription Demo copy is placeholder Chinese (e.g. short memorial line), not final lore text — affects: TMP content only

## Suggested next step

- [x] Planning artifacts already exist for this change
- [ ] Update `proposal.md` / `design.md` / `specs` / `tasks` with probe DECIDED items (朱红、平面字、无音效、三条验收)
- [ ] Run `/opsx:apply sacred-relic-shell-awaken` to finish Demo Prefab + scene (scripts skeleton already under `Assets/Scripts/SacredRelic/`)
