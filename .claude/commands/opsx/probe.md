---
name: "OPSX: Probe"
description: Probe a change before proposing — grilling over a 6-layer question tree (Experimental)
allowed-tools: Bash(openspec:*)
category: Workflow
tags: [workflow, planning, experimental]
---

Probe a change before `/opsx:propose`: depth-first grilling over a 6-layer question tree, producing `openspec/changes/<name>/probe-report.md`.

**Store selection:** If the user names a store (a store is a standalone OpenSpec repo registered on this machine) or the work lives in one, run `openspec store list --json` to discover registered store ids, then pass `--store <id>` on the commands that read or write specs and changes (`new change`, `status`, `instructions`, `list`, `show`, `validate`, `archive`, `doctor`, `context`). Other commands do not take the flag. Hints printed by commands already carry the flag; keep it on follow-ups. Without a store, commands act on the nearest local `openspec/` root.

Follow the openspec-probe skill. This is a free-text interview: ask exactly ONE question per message and wait for the reply — never use option-card / multiple-choice tools (e.g. AskUserQuestion) to collect answers.

Start by reading context:
```bash
openspec status --json
openspec instructions --json
```

**Input**: A change name (kebab-case) after `/opsx:probe`, OR a description to derive one from.
