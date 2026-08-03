---
name: openspec-apply-change
description: Implement tasks from an OpenSpec change. Use when the user wants to start implementing, continue implementation, or work through tasks.
allowed-tools: Bash(openspec:*)
license: MIT
compatibility: Requires openspec CLI.
metadata:
  author: openspec
  version: "1.0"
  generatedBy: "1.6.0"
---

Implement tasks from an OpenSpec change with TDD discipline.

**TDD DISCIPLINE — the point of this skill, not a side gate:**
- **One test at a time.** Drive ONE behavior to green before writing the next test. Never write a batch of tests up front, then a batch of implementation — that is "horizontal slicing" and it produces tests coupled to imagined behavior that pass when things break.
  ```
  WRONG (horizontal):  RED: test1..test5   then  GREEN: impl1..impl5
  RIGHT (vertical):    test1→impl1,  test2→impl2,  test3→impl3, ...
  ```
- **Test behavior through public interfaces, not implementation.** A good test reads like a spec ("user can checkout with valid cart") and survives an internal refactor. Do NOT mock internal collaborators, assert on private methods, or verify data shapes — a green test that breaks on refactor is worse than no test.
- The `tddMode` gates below decide when a task is *done*; this block decides *how* you get there. A green gate does not excuse batched or implementation-coupled tests.

**Store selection:** If the user names a store (a store is a standalone OpenSpec repo registered on this machine) or the work lives in one, run `openspec store list --json` to discover registered store ids, then pass `--store <id>` on the commands that read or write specs and changes (`new change`, `status`, `instructions`, `list`, `show`, `validate`, `archive`, `doctor`, `context`). Other commands do not take the flag. Hints printed by commands already carry the flag; keep it on follow-ups. Without a store, commands act on the nearest local `openspec/` root.

**Input**: Optionally specify a change name. If omitted, check if it can be inferred from conversation context. If vague or ambiguous you MUST prompt for available changes.

**Steps**

1. **Select the change**

   If a name is provided, use it. Otherwise:
   - Infer from conversation context if the user mentioned a change
   - Auto-select if only one active change exists
   - If ambiguous, run `openspec list --json` to get available changes and use the **AskUserQuestion tool** to let the user select

   Always announce: "Using change: <name>" and how to override (e.g., `/opsx-apply <other>`).

2. **Check status to understand the schema**
   ```bash
   openspec status --change "<name>" --json
   ```
   Parse the JSON to understand:
   - `schemaName`: The workflow being used (e.g., "spec-driven")
   - `planningHome`, `changeRoot`, and `actionContext`: planning scope and edit constraints
   - Which artifact contains the tasks (typically "tasks" for spec-driven, check status for others)

3. **Get apply instructions**

   ```bash
   openspec instructions apply --change "<name>" --json
   ```

   This returns:
   - `contextFiles`: artifact ID -> array of concrete file paths (varies by schema - could be proposal/specs/design/tasks or spec/tests/implementation/docs)
   - `tddMode`: TDD discipline level — `"strict"` | `"default"` | `"off"`
   - `commitMode`: per-task commit behavior — `"task"` | `"off"`
   - Progress (total, complete, remaining)
   - Task list with status
   - Dynamic instruction based on current state

   **Handle states:**
   - If `state: "blocked"` (missing artifacts): show message, suggest using openspec-continue-change
   - If `state: "all_done"`: congratulate, suggest archive
   - Otherwise: proceed to implementation

4. **Read context files**

   Read every file path listed under `contextFiles` from the apply instructions output.
   The files depend on the schema being used:
   - **spec-driven**: proposal, specs, design, tasks
   - Other schemas: follow the contextFiles from CLI output

5. **Create task progress list**

   Before starting implementation, register each pending task in the session task system. Use whatever mechanism the current platform provides — for example, TaskCreate in Claude Code, todo tools in Codex, or equivalent in other environments. This makes progress visible in the UI throughout the session.

   For each pending task in the apply instructions output:
   - Register a task/todo entry with the task title
   - Track the returned ID (if any) so you can update status later

   Then announce the session plan to the user:
   - Schema being used
   - TDD Mode: `<tddMode>`
   - Commit Mode: `<commitMode>`
   - Progress: "N/M tasks complete"
   - Remaining tasks overview
   - Dynamic instruction from CLI

6. **Implement tasks with TDD discipline (loop until done or blocked)**

   **Interpret `tddMode`:**
   - `"default"`: closing tasks require their scenarios green; intermediate/non-scenario tasks need only code complete
   - `"strict"`: same as default, plus every delta scenario must be covered by a green test before the change is done
   - `"off"`: no test gates (document/spike projects)

   **Interpret `commitMode`:**
   - `"task"`: commit after each completed task — the task's code changes and its checkbox flip in one atomic commit
   - `"off"`: never commit; leave all changes in the working tree

   **Test runner detection (run once before the loop):**
   Check whether a test runner is available by looking for one of:
   - `vitest.config.ts` / `vitest.config.js`
   - `jest.config.ts` / `jest.config.js` / `jest.config.mjs`
   - `mocha.opts` / `.mocharc.*`
   - A `"test"` script in `package.json`
   If none found, announce: "No test runner detected — tddMode degraded to 'off'" and proceed without test gates for this session.

   **Git detection (run once before the loop, only when `commitMode` is `"task"`):**
   - If the project is not a git repository (`git rev-parse --git-dir` fails), announce: "Not a git repo — commitMode degraded to 'off'" and skip all commit steps this session.
   - Record pre-existing dirty files via `git status --porcelain`. Those changes belong to the user — never stage or commit them.

   **For each pending task:**

   1. **Mark task in_progress**: Update the task/todo entry to `in_progress` (via the platform task mechanism) before starting work.
   2. **Map the task to scenarios**: Read the task description and the specs context files to identify which WHEN/THEN/AND scenarios this task advances.
   3. **Write ONE failing test** for the single scenario/behavior you are currently driving — not all of the task's scenarios at once:
      - Translate that scenario's WHEN/THEN/AND into a test assertion that exercises the public interface
      - Run the test and confirm it fails (RED) — do not proceed if it passes or errors with a setup problem
   4. **Write the minimal implementation** to make that one test pass — only enough to go green, don't anticipate later tests
   5. **Run the test**: confirm it is GREEN (and previously-green tests stay green). If the task touches more scenarios, repeat 3→5 one behavior at a time — never batch-write all the task's tests first
   6. **(Optional) Refactor** while keeping all previously-green tests green
   7. **Mark task complete** using this rule:
      - **Closes a scenario** (this is the last task that implements scenario X): that scenario's tests MUST be GREEN before marking `- [ ] → - [x]`
      - **Partially advances a scenario** (more tasks remain that implement scenario X): mark done as soon as code is complete — tests may still be RED (legal WIP)
      - **Does not advance any scenario** (pure refactor / config / migration): no test gate — mark done when code is complete
      - After marking `- [x]` in the tasks file, update the task/todo entry to `completed`
   8. **Commit the task** (skip when `commitMode` is `"off"` or degraded):
      - Stage ONLY the files you created or modified for this task, plus the tasks file with its checkbox flip — list them explicitly: `git add <file> <file> ...`. NEVER use `git add -A`, `git add .`, or `git add -u`
      - Commit message: `task(<change-id>): <N.M> <task title>` (e.g., `task(add-user-auth): 1.3 Implement CSV export endpoint`)
      - If a file touched by this task was already dirty before the session and your changes cannot be cleanly separated: skip this task's commit, announce it, and continue
      - If a hook (e.g., pre-commit) rejects the commit: report the hook output and continue with the next task — NEVER retry with `--no-verify`
      - Never push, never create branches, never amend previous commits

   **"Closes a scenario" judgment:** After completing code changes, scan the remaining pending tasks. If no remaining task further implements scenario X, this task closes scenario X.

   **Pause if:**
   - Task is unclear → ask for clarification
   - Implementation reveals a design issue → suggest updating artifacts
   - Error or blocker encountered → report and wait for guidance
   - User interrupts

7. **On completion or pause, show status**

   Display:
   - Tasks completed this session
   - Overall progress: "N/M tasks complete"
   - Test suite results (run the full test suite and report actual output)
   - Commits made this session (paste actual `git log --oneline` output for this session's commits; note any tasks whose commit was skipped, or "none" when `commitMode` is `"off"`)
   - **Change-level coverage gate** (depends on `tddMode`):
     - `"default"`: every scenario that **has** a test must be GREEN. Scenarios with **no** test → list them as a warning (does NOT block reporting all-done).
     - `"strict"`: **every** delta scenario must have a GREEN test. If any delta scenario lacks a test, or has a RED test → **STOP. Do NOT report all-done and do NOT suggest archive.** List the uncovered/red scenarios and ask the user to add tests or record a waiver. The `no-test` escape is closed in strict mode.
     - `"off"`: skip this gate entirely.
   - If all done (and the coverage gate above passes): suggest archive
   - If paused: explain why and wait for guidance

**Output During Implementation**

```
## Implementing: <change-name> (schema: <schema-name>, tddMode: <tddMode>, commitMode: <commitMode>)

Working on task 3/7: <task description>
  Scenarios: <scenario names this task advances>
  Writing test for: <scenario name> → RED
  Implementing...
  Tests: GREEN
✓ Task complete
  Committed: task(<change-id>): 3.1 <task title>

Working on task 4/7: <task description>
  (No scenario closed by this task — marking done on code complete)
✓ Task complete
```

**Output On Completion**

```
## Implementation Complete

**Change:** <change-name>
**Schema:** <schema-name>
**TDD Mode:** <tddMode>
**Commit Mode:** <commitMode>
**Progress:** N/M tasks complete

### Completed This Session
- [x] Task 1
- [x] Task 2
...

### Test Results
<paste actual test runner output here>

### Commits This Session
<paste actual git log --oneline output for this session's commits, or "none (commitMode: off)">

All tasks complete! Ready to archive this change.
```

**Output On Pause (Issue Encountered)**

```
## Implementation Paused

**Change:** <change-name>
**Schema:** <schema-name>
**TDD Mode:** <tddMode>
**Commit Mode:** <commitMode>
**Progress:** 4/7 tasks complete

### Issue Encountered
<description of the issue>

**Options:**
1. <option 1>
2. <option 2>
3. Other approach

What would you like to do?
```

**Guardrails**
- Keep going through tasks until done or blocked
- Always read context files before starting (from the apply instructions output)
- Read `tddMode` from apply instructions JSON before the loop; respect it throughout
- Read `commitMode` from apply instructions JSON before the loop; when `"task"`, commit each completed task — code changes + checkbox flip in one atomic commit, message `task(<change-id>): <N.M> <task title>`
- Stage files explicitly per task — never `git add -A`, `git add .`, or `git add -u`; pre-existing dirty files are never staged or committed
- Never bypass hooks with `--no-verify`, never push, never amend previous commits; if not a git repo, degrade commitMode to 'off' and announce it
- Test runner detection: degrade to `tddMode: "off"` if no runner found, announce it
- For tasks that close a scenario: do NOT mark `- [x]` until that scenario's tests are GREEN
- For tasks that partially advance a scenario: RED tests are legal — mark done when code is complete
- For tasks with no scenario: no test required — mark done when code is complete
- One test at a time: drive each behavior to green before the next; never batch all of a task's tests then all of its implementation (horizontal slicing)
- Tests must verify behavior through public interfaces and survive an internal refactor — no mocking internal collaborators, no asserting on private state; a green but implementation-coupled test does not satisfy the gate
- Change-level gate (`default`): a covered scenario may not stay RED at close; uncovered scenarios warn but do not block
- Change-level gate (`strict`): every delta scenario must have a GREEN test — if any is missing or RED, STOP, do not report all-done, do not suggest archive; `no-test` escape requires a recorded waiver
- Completion output MUST include actual test runner output, not a hardcoded claim
- If task is ambiguous, pause and ask before implementing
- If implementation reveals issues, pause and suggest artifact updates
- Keep code changes minimal and scoped to each task
- Pause on errors, blockers, or unclear requirements — do not guess
- Use contextFiles from CLI output, do not assume specific file names

**Fluid Workflow Integration**

This skill supports the "actions on a change" model:

- **Can be invoked anytime**: Before all artifacts are done (if tasks exist), after partial implementation, interleaved with other actions
- **Allows artifact updates**: If implementation reveals design issues, suggest updating artifacts — not phase-locked, work fluidly
