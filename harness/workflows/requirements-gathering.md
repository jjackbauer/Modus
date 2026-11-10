---
kind: workflow
mode: plan
description: Produce a requirements checklist and xUnit test plan for a C# project, driven by a pluggable analysis source, runtime diagnostics, and mandatory-item rules.
composes:
  - ../schemas/codebase-patterns.md
  - ../skills/test-standards.md
  - ../skills/plan-format-gate.md
  - ../rules/verification-absolute-behavior.md
---

# Workflow: Requirements Gathering

## Purpose
For a given C# project, produce:
1. A **Functionality Worktree** — a completeness checklist and (where applicable) a Mermaid class diagram, derived from the provided `AnalysisSource` and grounded in runtime evidence.
2. A **Test Plan** — a named xUnit test list for every unchecked checklist item, produced by the Test Standards skill.
3. A **Discovered Gaps** appendix — runtime-diagnosed failures, divergences, and test gaps that must be addressed in implementation.

Deliver all artefacts in a single structured document so the developer can start implementation immediately.

All outputs from this workflow must satisfy the Absolute Behavior Verification rule:
- Every checklist item must map to tests that prove executable runtime behavior.
- Metadata-only assertions are insufficient and cannot satisfy checklist completion.
- API tests must always verify runtime behavior thoroughly in integration (owner resolution, business semantics, DI lifetime path, correlation continuity, and isolation).
- Plans that fail this rule must be revised before save.

## Input (supplied by caller or collected interactively)

```
CsProject:        <Target C# project name, e.g. Lovelace.Integer>
TestProject:      <Corresponding test project; required for Step 0 diagnostics>
AnalysisSource:   <How to derive the checklist — e.g. "invoke an analysis skill with specific inputs",
                   "audit session artifacts at <path>", or a free-form description>
DiagnosticsSource:<How to gather runtime evidence — "run tests with filter <pattern> and verbosity normal",
                   "audit session event journals at <path>", "run host with log level debug",
                   or "none" to skip runtime diagnostics>
MandatoryItems:   <List of mandatory checklist entries with tags, or "none">
PlanType:         <Value to pass to the Format Gate, e.g. "requirements" or "generic">
OutputPath:       <Save location; defaults to harness/requirements/<CsProject>.md>
OutputTitle:      <H1 title of the output document>
ClosingMessage:   <Message to display after saving>
```

> **If invoked directly without a rule file supplying these parameters**, ask the user to provide each missing input before proceeding.

## Procedure

### Step 0 — Gather runtime evidence (unless DiagnosticsSource is "none")

Before deriving the checklist, collect live evidence so the plan is grounded in actual behavior, not assumptions.

Interpret `DiagnosticsSource`:

1. **If it says "run tests"**: execute `dotnet test <TestProject>` (with any specified filter and verbosity). Capture:
   - Total passed / failed / skipped counts.
   - Full names of every failed test and the first 10 lines of its error message.
   - Any warnings or transient errors in test output (e.g., "Transient error listing artifacts").

2. **If it says "audit session event journals"**: read the latest session directories under the specified path. For each session, parse `session-state.json` and `event-journal.ndjson`. Capture:
   - Session state (Running, Editing, Completed, etc.) and workflow outcome.
   - Stage execution order and per-stage producer capability IDs.
   - Any `WorkflowExecuted` events with diagnostics keys `outcome`, `command`.
   - Artifact references per stage and their roles.

3. **If it says "run host with log level debug"**: start the app process with the requested log level, let it run long enough to complete at least one iteration, capture stderr/stdout, then stop the process. Extract:
   - Startup configuration (resolved options, DI registrations).
   - Per-stage timing and outcome.
   - Any exceptions, retries, or fallback code paths hit.
   - Final status before shutdown.

4. **If it references a named skill**: invoke that skill and collect its output.

Record findings in a `DiagnosticsSummary` block:

```
## Runtime Diagnostics

| Signal | Detail |
|--------|--------|
| Tests passed | <count> |
| Tests failed | <count / list names> |
| Build warnings | <count> |
| Session outcomes | <summary per session> |
| Critical failures found | <list> |
```

Every critical failure discovered becomes a **mandatory unchecked item** injected into the checklist in Step 3.

### Step 1a — Source and intent fidelity (mandatory for a named source or user-named mechanism)

Skip this step only when the request names no external system and no mechanism the user wants kept.

1. Quote the user's request verbatim in the output document.
2. List every named mechanism/entity from the request and from the source's defining vocabulary.
3. Build the fit matrix with `Owner` and `Sign-off` columns:
   | Mechanism | Source evidence | Target constraint | Verdict | Owner | Sign-off |
4. Any `Reject` or `Defer` of a **headline mechanism** is a user-owned scope decision, not a finding. Put it in the `Scope Decisions Requiring Sign-Off` table with `Owner: user` and `Status: pending`, and ask the user the decision directly.
5. Do not proceed to Step 2 or write an implementation sequence while a pending headline decision exists. The correct output is a `DRAFT — BLOCKED ON SIGN-OFF` document plus the question.
6. Record constraint provenance: user-stated, repo-documented, or agent-inferred. An agent-inferred constraint may not exclude a user-named mechanism.

### Step 1 — Derive the completeness checklist

Use `AnalysisSource` to produce the checklist. Combine the analysis source with the `DiagnosticsSummary` from Step 0:
- If `AnalysisSource` references a named skill, invoke that skill with the appropriate inputs and wait for it to finish (zero Falsified rows). Cross-reference its output against runtime evidence — flag any checklist item that contradicts observed behavior.
- If `AnalysisSource` says "audit an existing C# class" or references a source file, read the source and enumerate every public member, interface implementation, and DI registration point as a checklist item.
- If `AnalysisSource` says "audit session artifacts", parse the stage execution JSON artifacts to derive the workflow stage graph and enumerate each stage as a checklist item.
- Otherwise, derive the checklist directly from the provided description, listing each distinct piece of functionality as an unchecked item.

Collect the mapping table, Mermaid class diagram (if applicable), and unchecked checklist items ordered by dependency.

### Step 2 — Run the Test Standards skill for each checklist item

For every unchecked item in the checklist (in dependency order):
1. Derive a plain-English functional description from `AnalysisSource` and the `DiagnosticsSummary` (e.g., from source code, interface contract, session artifacts, or provided description).
2. Invoke the Test Standards skill with the C# method signature and that description.
3. Wait for the skill to finish (zero Falsified rows) and collect the named test list.
4. Ensure each planned test for the item contains behavior-proof assertions (DI resolution, runtime dispatch, scheduled execution, or deterministic runtime block).
5. If the item only has metadata/documentation assertions, add behavior-proof tests until compliant.
6. **If the `DiagnosticsSummary` shows a failing test that covers this item**, include a test that reproduces the exact failure and a fix test that proves remediation.

### Step 3 — Enforce mandatory items

For each entry in `MandatoryItems`:
- If the item is already present in the checklist, leave it as-is.
- If it is missing, add it as a **mandatory unchecked item** with its specified tag.
- For each newly added mandatory item, generate test cases following Step 2 rules.

**Inject diagnostics-driven mandatory items**: for every critical failure discovered in Step 0, add a mandatory unchecked item tagged `[mandatory — runtime failure]` that requires a behavior-proof test reproducing and fixing the failure.

If `MandatoryItems` is `"none"`, skip the caller items but still inject diagnostics-driven items.

Regardless of caller input, inject the following mandatory compliance item when missing:
- `Enforce absolute behavior-proof verification for every planned integration test [mandatory - behavior-proof policy]`

When diagnostics reveal **diverging implementations** of the same concern in different files (e.g., two classes with different `IsLibraryEntryViable` logic), inject:
- `Reconcile diverging implementations of <concern> — unify to a single source of truth [mandatory — divergence found in diagnostics]`

When diagnostics reveal **transient errors in infrastructure** (e.g., "Transient error listing artifacts"), inject:
- `Harden <infrastructure component> against transient failures — add retry with backoff and surface as degraded, not silent [mandatory — transient errors in diagnostics]`

### Step 4 — Assemble the output

Combine the checklist and test plan into a single structured document using `OutputTitle` as the H1 heading and a brief scope statement as the blockquote.

Include the `DiagnosticsSummary` block from Step 0 between the scope statement and the Functionality Worktree section.

### Step 5 — Validate format

Invoke the Plan Format Gate skill with the value of `PlanType` against the assembled document from Step 4.

- If the verdict is **FAIL**, fix every listed violation in-place and re-run the gate until it returns **PASS**.
- Only proceed to Step 6 after a **PASS** verdict.

After format PASS, run an explicit compliance check against Absolute Behavior Verification:
- If any checklist item lacks behavior-proof tests, treat as FAIL and repair before save.
- If any planned test is metadata-only, treat as FAIL and repair before save.
- If any API-focused test lacks thorough integration gates, treat as FAIL and repair before save.

**Additional gate — Diagnostics coverage**: verify that every critical failure from the `DiagnosticsSummary` has a corresponding checklist item with behavior-proof tests. If any critical failure is unaddressed, treat as FAIL and add the missing item.

### Step 6 — Build and test gate (unless DiagnosticsSource was "none")

Before saving, verify the project's current state:

1. Run `dotnet build <CsProject>` — if it fails, add a mandatory item to fix the build before any other work.
2. Run `dotnet test <TestProject>` — capture all failing tests.
3. For each failing test, verify it has a corresponding checklist item. If not, add one.
4. Record the build/test gate result in the output document under a `## Build/Test Gate` section.

### Step 7 — Save to the output path

Write the assembled document to `OutputPath`.
Create the file if it does not exist; overwrite it if it does.
**This step is mandatory — do not skip it.**

## Output Format

```markdown
<OutputTitle>

> <Scope statement derived from caller inputs>

---

## Runtime Diagnostics

| Signal | Detail |
|--------|--------|
| ... | ... |

---

## Build/Test Gate

| Check | Status |
|-------|--------|
| dotnet build | passed / failed |
| dotnet test | N passed, M failed |
| Failures addressed in plan | yes / no — <list missing> |

---

## Scope Decisions Requiring Sign-Off

| ID | Decision | Source mechanism | Alternatives | Owner | Status |
|---|---|---|---|---|---|
| D-01 | <decision> | <mechanism> | <alternatives> | user | pending / approved / rejected |

> A `pending` headline row makes this document `DRAFT — BLOCKED ON SIGN-OFF`. Do not save it as build-ready and do not create a build goal from it.

---

## Functionality Worktree

### Verification Policy

- Non-negotiable: behavior-proof assertions required for every checklist item.
- Metadata-only assertions are supporting evidence only.
- API tests are valid only when thorough integration gates are asserted.
- Include absolute schedule gates when scheduled jobs are in scope.

### Class Diagram

<Mermaid diagram, if produced by AnalysisSource; omit section if not applicable>

### Completeness Checklist

- [ ] <item 1> [<tag>]
- [ ] <item 2> [<tag>]
...

---

## Test Plan

### `<Method or feature name>`

1. `<TestName_GivenScenario_ExpectedResult>`
   *Assumption*: ...
...

---

## Discovered Gaps

<Items found during diagnostics that must be addressed, with references to the checklist items that cover them.>

---

*All assumptions verified by Falsify Claims. Zero Falsified rows.*
```

After saving the file, display `ClosingMessage`.
