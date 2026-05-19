---
agent: agent
description: Orchestrate workflow-iterative-implementation across all unchecked items using execution and verification subagents until completion.
---

#file:.github/prompts/workflow-iterative-implementation.prompt.md
#file:.github/prompts/skill-falsify-claims.prompt.md
#file:.github/prompts/skill-test-standards.prompt.md
#file:.github/prompts/skill-impl-completeness.prompt.md

# Skill: Iterative Implementation Orchestrator

## Purpose
Run `workflow-iterative-implementation.prompt.md` repeatedly until every checklist item is complete for a target project.

This skill must use:
- one execution subagent to implement each checklist item end-to-end
- one separate verification subagent to concretely verify the result before moving forward

## Input (supplied by caller)

```text
CsProject:       <Target C# project, for example src/Modus.Core/Modus.Core.csproj>
TestProject:     <Corresponding xUnit project, for example tests/Modus.Core.Tests/Modus.Core.Tests.csproj>
RequirementsDoc: <Optional path; defaults to .github/requirements/<CsProjectName>.md>
Scope:           <Optional filter for checklist section; default all unchecked items>
MaxRepairRounds: <Optional integer; default 3 per checklist item>
```

## Hard Rules

1. Use subagents for all checklist-item execution. Do not execute item implementation directly in the parent run.
2. Use a different subagent invocation for verification after each execution attempt.
3. Verification must be concrete and evidence-based: file diffs, checklist state, and build/test results.
4. Do not mark an item complete unless verification passes.
5. Keep functional-test standards from `workflow-iterative-implementation.prompt.md` unchanged.
6. Stop only when either:
   - all in-scope checklist items are `[x]`, or
   - a blocker remains unresolved after `MaxRepairRounds`.

## Procedure

### Step 1 - Resolve scope and queue

1. Read `RequirementsDoc`.
2. Collect all unchecked checklist items (`- [ ] ...`) in scope order.
3. Build a work queue with exact checklist text preserved.

### Step 2 - Per-item execution loop

For each queued checklist item:

1. Start an **Execution Subagent** run with this payload:

```text
Run #file:.github/prompts/workflow-iterative-implementation.prompt.md for exactly one item.
ChecklistItem: <Exact unchecked line text>
CsProject: <CsProject>
TestProject: <TestProject>
RequirementsDoc: <RequirementsDoc>

Required output:
- tests added/updated (with file paths)
- implementation members/files changed
- command evidence for build/test
- checklist update and remaining unchecked count
```

2. Start a separate **Verification Subagent** run with this payload:

```text
Verify concretely whether the previous execution actually completed the same checklist item.
Check:
1) RequirementsDoc item changed from [ ] to [x]
2) Tests are functional and target concrete behavior
3) Implemented members exist and no NotImplementedException remains for covered members
4) dotnet build <CsProject> succeeds
5) dotnet test <TestProject> --no-build succeeds

Return:
- PASS or FAIL
- evidence table (claim -> evidence)
- exact failing gaps if FAIL
```

3. If verification is `FAIL`:
   - increment repair round for that item
   - send verifier gaps into a new Execution Subagent run for the same item
   - re-run Verification Subagent
   - stop and report blocker when repair rounds exceed `MaxRepairRounds`

4. If verification is `PASS`:
   - mark item as complete in orchestrator state
   - continue to next unchecked item

### Step 3 - Final completion verification

After queue is exhausted, run one final **Verification Subagent** pass:

1. Confirm no unchecked in-scope checklist items remain in `RequirementsDoc`.
2. Run and verify:

```text
dotnet build <CsProject>
dotnet test <TestProject> --no-build
```

3. Return final readiness decision: `COMPLETE` or `BLOCKED`.

## Output Summary Template

```markdown
## Iterative Implementation Orchestration Result

CsProject: <CsProject>
TestProject: <TestProject>
RequirementsDoc: <RequirementsDoc>

Completed items:
- <item 1>
- <item 2>

Blocked items:
- <item>: <reason>

Verification summary:
- Per-item verification: PASS/FAIL counts
- Final build: passed/failed
- Final tests: passed/failed

Overall status: COMPLETE | BLOCKED
```

## Notes

- This skill is an orchestrator. It delegates implementation to an execution subagent and enforces independent concrete verification via a second subagent invocation.
- Keep checklist text exact to avoid mismatching similarly worded requirements items.
