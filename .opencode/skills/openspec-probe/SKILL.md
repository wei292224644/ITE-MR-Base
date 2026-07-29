---
name: openspec-probe
description: Probe a change before proposing: depth-first grilling over a 6-layer question tree, producing probe-report.md. Use when the user wants to align deeply on scope, design, and assumptions before generating artifacts.
allowed-tools: Bash(openspec:*)
license: MIT
compatibility: Requires openspec CLI.
metadata:
  author: openspec
  version: "1.0"
  generatedBy: "1.6.0"
---

Probe a change before `/opsx-propose` — converge on decisions and surface hidden assumptions.

**Store selection:** If the user names a store (a store is a standalone OpenSpec repo registered on this machine) or the work lives in one, run `openspec store list --json` to discover registered store ids, then pass `--store <id>` on the commands that read or write specs and changes (`new change`, `status`, `instructions`, `list`, `show`, `validate`, `archive`, `doctor`, `context`). Other commands do not take the flag. Hints printed by commands already carry the flag; keep it on follow-ups. Without a store, commands act on the nearest local `openspec/` root.

**INTERACTION RULES — non-negotiable, apply throughout the entire session:**
- **Ask exactly ONE question per message, as plain text, then stop and wait for the reply.** Asking multiple questions at once is bewildering — the user loses the thread and answers shallowly, which defeats the whole point of probing.
- **NEVER use AskUserQuestion, multiple-choice UI, or any form/options widget.** A form batches decisions and hides reasoning. This is a pure text conversation: put your recommended answer inside the question itself ("I'd suggest X because <evidence> — does that hold?"), not as selectable options.
- **Every question includes your recommended answer, with evidence**: a spec, `status --json`, a source file:line, or an explicit "(general assumption, unverified)". Never assert a strong recommendation from thin air.
- **Codebase-first**: if a question can be answered by reading, read instead of asking.
- **Depth-first**: drill one branch until clear, then move to the next. When an answer is vague, follow up ("you said 'roughly' — I read that as X, correct?").
- **Relentless but respectful**: don't let real ambiguity slide; but when the user says "default"/"don't know"/"your call", record it as an open assumption and move on. The user can say "enough, start" at any time.

---

probe is an OPTIONAL pre-propose alignment phase. Its job: use architecture (a question tree + grilling interaction) to reach a thinking depth the model would not reach on its own, then persist the result so it survives context compaction.

**Input**: A change name (kebab-case) OR a description of what to build. Derive a kebab-case name if only a description is given.

**Steps**

1. **Read the codebase FIRST (do not ask what you can read)**
   Before any question, gather context:
   ```bash
   openspec status --json
   openspec instructions --json
   ```
   Also read existing specs under `openspec/specs/` and any relevant source.
   If a project constitution exists (`openspec/constitution.md`), read it — do NOT ask about anything it already governs.

2. **Scaffold the change shell**
   ```bash
   openspec new change "<name>"
   ```
   The report will be written into this change directory (`openspec/changes/<name>/probe-report.md`).
   If the change already exists, proceed (you will update its report).

3. **Grill — follow the INTERACTION RULES above**
   Interview the user one question at a time, using the question tree (below) as your navigation map. The tree is NOT a checklist to march through — it exists so you know which branch to drill next; the conversation decides traversal order and depth. Do not batch questions to "cover" layers faster.

4. **Question tree (navigation map, not a checklist)**
   - **L1 Scope**: what problem, why now? what is explicitly out of scope? who/what benefits?
   - **L2 Impact**: which existing specs are touched (modify/add)? which code modules? what depends on what we change (downstream)? what does this change depend on (upstream)?
   - **L3 Design** (by complexity): 2–3 implementation options + trade-offs? recommended option + reason (cite evidence)? key interfaces/data-structure changes? consistent with existing patterns — if not, why?
   - **L4 Failure** (by complexity): how can this change fail? state on failure, recoverable? security/perf/concurrency risks? boundary conditions (min/max/empty)?
   - **L5 Success** (by complexity): measurable definition of "done"? what test proves it works? what signal tells us we got it wrong?
   - **L6 Open assumptions**: which assumptions did AI make about existing code (unverified)? about project conventions? what did AI guess because it didn't know? Mark each `[NEEDS CLARIFICATION]`.

5. **Write `openspec/changes/<name>/probe-report.md`** when probe ends (see structure below). Then suggest the next step.

**Stopping condition**
No forced end. probe ends when (a) no genuine ambiguity remains, or (b) the user says "enough"/"start". "If there really are no problems, no more will surface."
Before finishing, run a coverage check (this is where coverage is enforced — not by batching questions): L1 & L2 must have been addressed in conversation; L3–L5 scale with complexity (simple changes pass fast); L6 must appear in the report even if everything else resolved quickly.

**probe-report.md structure**

```markdown
# Probe Report: <change-name>

> Generated: <timestamp>
> Summary: <N questions, M decisions, K open assumptions>

## Confirmed decisions

### Scope & intent
- **Question**: ...
- **AI recommendation**: ... (evidence: <file:line | "general assumption">)
- **User confirmation**: ...

### Impact
...(same format)

### Design
...(same format)

### Success criteria
...(same format)

## Open assumptions [NEEDS CLARIFICATION]

These are assumptions AI made without confirmation; they will be carried explicitly into artifacts:

- [ ] `[ASSUMED]` <assumption> — affects: <which artifact / section>
- [ ] `[ASSUMED]` ...

## Suggested next step

- [ ] Run `/opsx-propose <change-name>` to generate artifacts (it will read this report)
```

**Output (on finish)**
```
## Probe complete: <change-name>

Asked N questions · confirmed M decisions · K open assumptions
Report: openspec/changes/<name>/probe-report.md

Next: run /opsx-propose <name> — it will read this report and carry the open
assumptions into proposal.md.
```

**Guardrails**
- Read before you ask; only ask what the codebase cannot answer
- One question per message, plain text, each with an evidence-backed recommendation — never option cards, never a batch
- Coverage is checked at the END (L1 & L2 addressed; L6 in the report), not by rushing the interview
- Never bury an assumption — every guess becomes an `[ASSUMED]` line
- Respect "enough/start"; record remaining items as open assumptions and finish
