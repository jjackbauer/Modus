---
agent: agent
description: Implement one checklist item end-to-end - write xUnit tests, implement feature, build, test, and mark the item done.
---

#file:.github/prompts/codebase-patterns.md
#file:.github/prompts/skill-falsify-claims.prompt.md
#file:.github/prompts/skill-test-standards.prompt.md
#file:.github/prompts/skill-impl-completeness.prompt.md

# Workflow: Iterative Implementation

## Purpose
Take one unchecked requirements item and deliver it to a fully passing state:

- Functional xUnit tests added and passing
- Implementation complete for covered members
- dotnet build passes
- dotnet test passes

One checklist item per invocation.

## Input (supplied by caller)

```
ChecklistItem:  <Exact text of unchecked item>
CsProject:      <Target C# project>
TestProject:    <Corresponding test project>
RequirementsDoc:<Optional path; defaults to .github/requirements/<CsProject>.md>
```

## Hard Rules

1. Every new test must exercise functional behavior with concrete inputs and concrete expected outputs.
2. No throw new NotImplementedException stubs may remain in members covered by the checklist item.
3. Tests must fail before implementation and pass after implementation.
4. Do not weaken assertions to get green tests.

## Procedure

### Step 1 - Derive tests

- Pull the named test list from prior requirements output, or re-run skill-test-standards.
- Keep only tests that satisfy hard rules.
- Run skill-falsify-claims on test assumptions and iterate until zero Falsified rows.

### Step 2 - Write xUnit tests

- Add tests under TestProject in a topic-appropriate file.
- Use Fact for single-case and Theory plus InlineData or MemberData for parameterized cases.
- Use only public API of CsProject.

Run:

```
dotnet build <TestProject>
dotnet test <TestProject>
```

If tests pass before implementation exists, replace non-functional tests.

### Step 3 - Implement feature

- Use requirements and completeness mapping as source of truth.
- Capture implementation claims and run skill-falsify-claims.
- Implement members covered by ChecklistItem.
- Remove NotImplementedException stubs for covered members.

### Step 4 - Build and test loop

Repeat until both commands succeed:

```
dotnet build <CsProject>
dotnet test  <TestProject> --no-build
```

Fix root cause on each failure. Only change tests when falsification proves expectation is wrong.

### Step 5 - Mark done and report

When all checks pass:

1. Update requirements doc checklist item from [ ] to [x].
2. Update related mapping table status for implemented members.
3. Report tests added, members implemented, build status, test status, and remaining unchecked items.

If all checklist items are complete, update project readiness notes in repository docs.

## Output Summary Template

```markdown
## Completed: <ChecklistItem>

Functional tests added: <list>
Implementation updated: <list of members/files>
Build: passed
Tests: passed
Remaining checklist items: <list or none>
```
