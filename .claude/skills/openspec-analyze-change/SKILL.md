---
name: openspec-analyze-change
description: Pre-apply read-only analysis: constitution alignment + artifact consistency. Use after propose, before apply.
allowed-tools: Bash(openspec:*)
license: MIT
compatibility: Requires openspec CLI.
metadata:
  author: openspec
  version: "1.0"
  generatedBy: "1.6.0"
---

Analyze a change against the project constitution and artifact consistency. READ-ONLY — do not modify any files.

**Store selection:** If the user names a store (a store is a standalone OpenSpec repo registered on this machine) or the work lives in one, run `openspec store list --json` to discover registered store ids, then pass `--store <id>` on the commands that read or write specs and changes (`new change`, `status`, `instructions`, `list`, `show`, `validate`, `archive`, `doctor`, `context`). Other commands do not take the flag. Hints printed by commands already carry the flag; keep it on follow-ups. Without a store, commands act on the nearest local `openspec/` root.

**Authority**: Constitution is non-negotiable during analyze. On MUST violations, adjust the plan — do NOT reinterpret or delete clauses. Constitution changes require /opsx:constitution separately.

**Input**: Change name (kebab-case). If omitted, prompt via openspec list --json.

**Steps**

1. **Load structured context**
   ```bash
   openspec instructions analyze --change "<name>" --json
   ```
   You receive: constitutionPresent, clauses[] (id/title/level/criteria), waivers[] (principle/reason), artifacts[] (id/path/exists).
   If constitutionPresent is false → report WARNING: no constitution, skip the Constitution Alignment pass.

2. **Read artifact contents** for each artifacts[].path where exists is true.

3. **Pass: Constitution Alignment** — iterate clauses[] (already parsed; do not re-parse):
   - structure criterion: check artifact structure mechanically against the criterion
   - judgment criterion: evaluate itemized yes/no per requirement (anchor to ### Requirement: IDs)
   - MUST violation + no matching waiver → CRITICAL with evidence: file + requirement ID + criterion
   - MUST violation + waiver where principle === clause.id → NOTE: "⚠ Waived <id> — reason: <reason>"
   - judgment without concrete evidence → downgrade to WARNING (never CRITICAL)

4. **Pass: Coverage / Ambiguity / Consistency** (SpecKit-style, artifacts only)
   - Requirement → task mapping gaps
   - Duplicate or conflicting requirements
   - proposal Capabilities vs specs files
   - design Decisions vs specs scope

5. **Report** — write for a human reader; there is NO downstream machine consumer of this report, so optimize for reading, not for parsing. Conclusion first, then triage. Output in this exact order:

   **(a) Verdict banner** — one line, the answer up front:
   - any CRITICAL → `⛔ Do not apply — N CRITICAL issue(s) to resolve`
   - else if any WARNING → `⚠️ OK to apply — N warning(s) worth a look first`
   - else → `✅ OK to apply`

   **(b) One-paragraph summary** — 2–3 plain-language sentences: what blocks apply, what's minor, what was waived. No jargon, no IDs.

   **(c) Coverage bar** — render requirement→task coverage as a 10-cell ASCII bar (█ = covered, ░ = uncovered) plus the fraction:
   `Coverage  ████████░░ 80%  (8/10 have tasks)`

   **(d) 🔴 Must fix (CRITICALs)** — numbered, one finding each:
   ```
   1. <one-line problem> → violates <constitution clause / consistency rule>
      Location  <file › Requirement: id>
      Fix       <concrete action>
   ```

   **(e) 🟡 Should fix (WARNINGs)** — same shape, but collapsed: print the count and a `‹expand N›` hint instead of the bodies. Expand inline only if there are ≤2, or when the user asks.

   **(f) Waived (NOTEs)** — only if present, one line each: `⚠ <id> — <reason>`.

   **Advisory-block**: if any CRITICAL, do NOT proceed to apply — say so in the verdict and stop. Continue only if the user explicitly accepts the risk (and still list every CRITICAL).

**Do NOT**: read implementation code, modify constitution, modify artifacts, or conflate with verify.
