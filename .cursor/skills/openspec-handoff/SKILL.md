---
name: openspec-handoff
description: Compact the current session into a handoff document under docs/handoff/ so a fresh agent can continue the work. Use when the user wants to wrap up, hand off, or compact a long session before starting a new conversation.
allowed-tools: Bash(openspec:*)
license: MIT
compatibility: Requires openspec CLI.
metadata:
  author: openspec
  version: "1.0"
  generatedBy: "1.6.0"
---

Write a handoff document that summarises the current session so a fresh agent can continue the work.

**Store selection:** If the user names a store (a store is a standalone OpenSpec repo registered on this machine) or the work lives in one, run `openspec store list --json` to discover registered store ids, then pass `--store <id>` on the commands that read or write specs and changes (`new change`, `status`, `instructions`, `list`, `show`, `validate`, `archive`, `doctor`, `context`). Other commands do not take the flag. Hints printed by commands already carry the flag; keep it on follow-ups. Without a store, commands act on the nearest local `openspec/` root.

**Input** (optional): A short description of what the NEXT session will focus on. If given, tailor the document toward that focus.

**Where it goes**
Save to `docs/handoff/<YYYY-MM-DD>-<slug>.md` inside the current workspace (NOT the OS temp directory). Derive `<slug>` (kebab-case) from the session's focus or the next-session argument. Create the `docs/handoff/` directory if it does not exist.

**Rules**
- **Summarise, don't transcribe.** Capture the state, decisions, and the through-line a new agent needs — not the full back-and-forth.
- **Reference, don't duplicate.** Anything already captured in another artifact (OpenSpec `proposal.md` / `design.md` / `tasks.md`, specs under `openspec/specs/`, ADRs, commits, diffs, issues, URLs) MUST be linked by path or URL, never copied in.
- **Suggested skills.** Include a section recommending which skills the next agent should invoke (e.g. `/opsx:propose`, `/opsx:apply`, `/opsx:probe`), so the fresh session starts in the right mode.
- **Redact secrets.** Never write API keys, passwords, tokens, or PII into the document.
- **Tailor to the argument.** If the user passed a next-session focus, orient the "Next steps" and "Suggested skills" around it.

**Document structure**

```markdown
# Handoff: <topic>

> Generated: <timestamp>
> Next session focus: <argument, or "general continuation">

## Goal
<What we are ultimately trying to achieve — one or two sentences.>

## Current state
<Where things stand right now: what is done, what is in flight.>

## Key decisions
<Decisions already made and the reasoning, so the next agent does not re-litigate them.
Reference artifacts by path instead of restating their contents.>

## Open questions / blockers
<What is unresolved and needs a decision or investigation next.>

## Relevant artifacts
- <path or URL> — <why it matters>

## Suggested next steps
- [ ] <concrete next action>

## Suggested skills
- `/opsx:<skill>` — <when/why the next agent should invoke it>
```

**Output (on finish)**
```
## Handoff written

docs/handoff/<YYYY-MM-DD>-<slug>.md

Open a fresh session and point it at this file to continue.
```

**Guardrails**
- One file under `docs/handoff/`; never overwrite an existing dated handoff — add a counter or refine the slug if a same-day file exists
- Reference existing artifacts by path; do not copy their contents
- Always include a Suggested skills section
- Redact anything sensitive
