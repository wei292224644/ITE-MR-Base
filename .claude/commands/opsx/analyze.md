---
name: "OPSX: Analyze"
description: Pre-apply read-only constitution alignment and artifact consistency check
allowed-tools: Bash(openspec:*)
category: Workflow
tags: [workflow, analyze, experimental]
---

Analyze a change against the project constitution before implementation.

**Store selection:** If the user names a store (a store is a standalone OpenSpec repo registered on this machine) or the work lives in one, run `openspec store list --json` to discover registered store ids, then pass `--store <id>` on the commands that read or write specs and changes (`new change`, `status`, `instructions`, `list`, `show`, `validate`, `archive`, `doctor`, `context`). Other commands do not take the flag. Hints printed by commands already carry the flag; keep it on follow-ups. Without a store, commands act on the nearest local `openspec/` root.

Follow the openspec-analyze-change skill. Start with:
```bash
openspec instructions analyze --change "<name>" --json
```
