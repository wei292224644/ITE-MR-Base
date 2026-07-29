---
name: /opsx-constitution
id: opsx-constitution
category: Workflow
description: Draft or revise project constitution (plan-level invariants)
---

Run the constitution workflow to create or update openspec/constitution.md.

**Store selection:** If the user names a store (a store is a standalone OpenSpec repo registered on this machine) or the work lives in one, run `openspec store list --json` to discover registered store ids, then pass `--store <id>` on the commands that read or write specs and changes (`new change`, `status`, `instructions`, `list`, `show`, `validate`, `archive`, `doctor`, `context`). Other commands do not take the flag. Hints printed by commands already carry the flag; keep it on follow-ups. Without a store, commands act on the nearest local `openspec/` root.

Follow the openspec-constitution skill. Start with:
```bash
openspec instructions constitution --json
```
