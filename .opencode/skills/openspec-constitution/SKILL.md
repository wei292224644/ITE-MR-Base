---
name: openspec-constitution
description: Draft or revise the project constitution (plan-level invariants). Use when establishing or updating openspec/constitution.md.
allowed-tools: Bash(openspec:*)
license: MIT
compatibility: Requires openspec CLI.
metadata:
  author: openspec
  version: "1.0"
  generatedBy: "1.6.0"
---

Draft or revise the project constitution at openspec/constitution.md.

**Store selection:** If the user names a store (a store is a standalone OpenSpec repo registered on this machine) or the work lives in one, run `openspec store list --json` to discover registered store ids, then pass `--store <id>` on the commands that read or write specs and changes (`new change`, `status`, `instructions`, `list`, `show`, `validate`, `archive`, `doctor`, `context`). Other commands do not take the flag. Hints printed by commands already carry the flag; keep it on follow-ups. Without a store, commands act on the nearest local `openspec/` root.

**Steps**

1. **Load template, rules, and migration hints**
   ```bash
   openspec status --json
   openspec instructions constitution --json
   ```
   Parse: template, writingRules, instruction, existingContent, configContext, configRules, migrationHints.

2. **Review migration hints (consume, do NOT re-classify)**
   For each entry in migrationHints, present its precomputed suggestion to the user:
   - [constitution] → confirm wording, then add as a clause in step 4
   - [linter] → recommend an ESLint rule / CI check, do NOT add to constitution
   - [keep] → leave in config.yaml context
   Ask the user to confirm or override each — the suggestion is a starting point, the user decides.

3. **Interactive grilling (one principle at a time)**
   For each principle ask:
   > "If someone violated this, what would analyze look at in proposal/design/specs to find it?"
   - Untestable goal → push for a CRITERION[structure] or CRITERION[judgment] line
   - Code-only rule → redirect to linter/CI
   - Vague wording → rewrite with MUST/SHOULD or downgrade to config rules

4. **Write openspec/constitution.md**
   - Roman numeral ids + (MUST)/(SHOULD)
   - Each clause: ≥1 CRITERION line with PASS/FAIL examples
   - One topic per clause
   - Light version stamp: `> Version: X.Y · Last amended: YYYY-MM-DD`

5. **Confirm**
   Summarize clauses. Remind: MUST violations are flagged CRITICAL by /opsx-analyze (advisory block) until fixed or waived in .openspec.yaml.
