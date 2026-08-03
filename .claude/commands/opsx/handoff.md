---
name: "OPSX: Handoff"
description: Compact the current session into a handoff doc under docs/handoff/ for a fresh agent (Experimental)
allowed-tools: Bash(openspec:*)
category: Workflow
tags: [workflow, handoff, experimental]
---

Compact the current session into a handoff document so a fresh agent can continue.

**Store selection:** If the user names a store (a store is a standalone OpenSpec repo registered on this machine) or the work lives in one, run `openspec store list --json` to discover registered store ids, then pass `--store <id>` on the commands that read or write specs and changes (`new change`, `status`, `instructions`, `list`, `show`, `validate`, `archive`, `doctor`, `context`). Other commands do not take the flag. Hints printed by commands already carry the flag; keep it on follow-ups. Without a store, commands act on the nearest local `openspec/` root.

Follow the openspec-handoff skill. Write the document to `docs/handoff/<YYYY-MM-DD>-<slug>.md` in the current workspace.

**Input** (optional): A short description after `/opsx:handoff` of what the next session will focus on — used to tailor the document.
