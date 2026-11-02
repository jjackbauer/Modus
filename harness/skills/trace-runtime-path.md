---
kind: skill
mode: agent
description: Before modifying any code path, trace the actual runtime execution flow — which classes are instantiated, by whom (DI vs framework), and in what order. Prevents wasted effort on dead code.
---

# Skill: Trace the Runtime Path Before Modifying

## Purpose
Before implementing a feature that integrates two subsystems (e.g., adding pool growth to an agent), trace the exact runtime execution path to identify which classes are actually instantiated and by what mechanism (DI, framework, manual `new`). This prevents wasting time modifying dead code.

## Hard Rules

1. **Never assume a class is used just because it exists.** The codebase may have multiple classes with similar names (e.g., `BonesCoLearningPonderTool` vs `BonesCoLearningPonderAgent`). Only one is on the hot path.

2. **Read the workflow/DI registration before touching any class.** The registration tells you exactly which types are resolved and how.

3. **Check how the framework resolves instances.** Frameworks like WipBuilder may use `Services.AddSingleton(type)` (type-based) which ignores factory registrations. Or they may use `ActivatorUtilities.CreateInstance` which bypasses DI entirely.

4. **Grep for `new ClassName(` to find instantiation sites.** This reveals whether instances are created via DI, manual construction, or framework.

## Procedure

### Step 1 — Identify the hot path

1. Read the workflow/workflow-map registration file (e.g., `*WorkflowRegistration.cs`).
2. Note every `AddAgent`, `AddTool`, `AddWorkflow` call — these are the capabilities that run.
3. For each capability, read the registered type to understand if it directly executes or delegates.

### Step 2 — Trace instantiation

For each type on the hot path:
1. Search for `new TypeName(` to find all instantiation sites.
2. Check if it's registered in DI (`AddSingleton`, `AddScoped`, etc.).
3. If registered via a framework method (e.g., `builder.AddAgent<T>`), read that framework method to understand how it resolves instances.

### Step 3 — Verify the chain end-to-end

1. Start from the entry point (e.g., `BonesLearningLoopHost`) and trace every method call through to the integration point.
2. Confirm that modifying the target class will actually affect runtime behavior.
3. If the class is never instantiated on the hot path, find the correct class.

## Output

A trace table:

| Step | Class | How Instantiated | Confirmed? |
|------|-------|-----------------|------------|
| 1 | BonesLearningLoopHost | DI singleton | ✅ |
| 2 | BonesCoLearningEnhanceAgent | WipBuilder.AddAgent → DI singleton | ✅ |
| 3 | BonesEnhanceStrategyAgent | DI singleton | ✅ |

Then state: the integration point is `<class>.<method>` at `<file>:<line>`.

## Common Pitfalls

- **Mistaking `*Tool` for `*Agent`**: Both may exist but only one is registered in the workflow.
- **Assuming `AddAgent` uses DI factories**: It calls `Services.AddSingleton(type)`, which is type-based. Factory registrations added before it are overridden.
- **Assuming constructor DI always works**: Optional parameters (`StrategyPool? pool = null`) won't be injected by type-based DI.
