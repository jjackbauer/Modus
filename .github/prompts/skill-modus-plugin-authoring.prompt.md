---
agent: agent
description: Specialized skill for creating Modus plugins with spec generation, iterative implementation, test validation, and real Host CLI execution.
---

#file:.github/prompts/workflow-requirements-gathering.prompt.md
#file:.github/prompts/skill-iterative-implementation-orchestrator.prompt.md
#file:.github/prompts/skill-falsify-claims.prompt.md
#file:.github/prompts/skill-plan-format-gate.prompt.md

# Skill: Modus Plugin Authoring End-to-End

## Purpose
Create a Modus plugin end-to-end, ensuring:

1. Formal plugin requirements spec (requirements doc) generated via `workflow-requirements-gathering.prompt.md`.
2. Checklist-driven iterative implementation until all items are complete.
3. Full verification using automated tests.
4. Functional verification in real Host runtime via CLI (not only automated tests).

## Inputs

```text
PluginName:          <Nome do plugin, ex: Plugin.Notifications.Email>
PluginType:          <standard | scheduled | timer-extension>
CsProject:           <Target plugin project>
TestProject:         <Corresponding xUnit project>
HostProject:         <Host project, default: src/Modus.Host/Modus.Host.csproj>
RequirementsDoc:     <Optional; default: .github/requirements/<PluginName>.md>
PluginsPath:         <Optional; default: plugins>
AnalysisSource:      <Functional description, contract, or implementation reference>
MandatoryItems:      <Optional; default: this skill's mandatory list>
PlanType:            <Optional; default: requirements>
MaxRepairRounds:     <Optional; default: 3>
```

> If any required input is missing, ask for values before execution.

## Default MandatoryItems (if not provided)

Use these mandatory checklist items:

- Document plugin artifact and metadata requirements for Host discovery [onboarding]
- Document plugin contract and lifecycle requirements (`IPluginContract`, `IPluginLifecycle`) [contracts]
- Document plugin-owned contract interface requirements (for example `I<PluginName>Contract : IPluginContract`) and DI registration under that interface [plugin-contract-interface]
- Document plugin operation catalog behavior and deterministic ordering [operations]
- Document diagnostics and failure semantics for startup/activation/operation [diagnostics]
- Document regression workflow and runtime CLI evidence requirements [verification]

For `PluginType=scheduled`, add:

- Document recurring schedule registration requirements (`IPluginScheduledEvents`, `IPluginScheduler.ScheduleRecurring`) [scheduling]

For `PluginType=timer-extension`, add:

- Document timer extension ownership and dispatch requirements (`IScheduledTimerTaskExtension`) [timer-extension]

## Hard Rules

1. The spec must be generated using `workflow-requirements-gathering.prompt.md`.
2. No checklist item may be marked complete without concrete verification (build, test, and evidence).
3. Final validation must include real Host CLI execution with observable plugin output.
4. Do not accept validation based only on automated tests.
5. On failures, run repair loops up to `MaxRepairRounds` per item.
6. Every plugin must declare its own contract interface in plugin code (for example `I<PluginName>Contract : IPluginContract`) and register the plugin instance under that interface in `IPluginDependencyRegistrar.Register`.

## Procedure

### Step 1 - Generate plugin spec

Run the requirements workflow with:

- `CsProject=<CsProject>`
- `AnalysisSource=<AnalysisSource>`
- `MandatoryItems=<MandatoryItems or default>`
- `PlanType=<PlanType>`
- `OutputPath=<RequirementsDoc>`
- `OutputTitle=# Requirements: <PluginName> Authoring Workflow`
- `ClosingMessage=Plugin requirements generated successfully.`

Exit criteria:

- Document saved at `RequirementsDoc`
- Format validated (PASS in format gate)
- Test plan created for all incomplete items

### Step 2 - Checklist-driven iterative implementation

Run `skill-iterative-implementation-orchestrator.prompt.md` with:

- `CsProject=<CsProject>`
- `TestProject=<TestProject>`
- `RequirementsDoc=<RequirementsDoc>`
- `Scope=all unchecked items`
- `MaxRepairRounds=<MaxRepairRounds>`

Exit criteria:

- All items in `RequirementsDoc` marked as `[x]`, or
- Explicit blocker with concrete gaps after exceeding repair rounds

### Step 3 - Required minimum automated verification

Run and record evidence:

```bash
dotnet build <CsProject>
dotnet build <TestProject>
dotnet test <TestProject> --no-build
```

If the plugin impacts Host/runtime, also run:

```bash
dotnet build <HostProject>
dotnet test tests/Modus.Host.IntegrationTests/Modus.Host.IntegrationTests.csproj --no-build
```

### Step 4 - Functional Host CLI verification (required)

Run the Host against the real plugins directory and collect output.

Base command:

```bash
dotnet run --project <HostProject> -- <PluginsPath>
```

Runtime evidence requirements:

1. Healthy startup diagnostics (`stage=startup ... outcome=success`).
2. Discovery/validation/activation of the target plugin (`stage=discovery|validation|activation plugin=<PluginName> ... outcome=success`).
3. Functional plugin evidence:
   - `standard`: plugin operation executed and logged in `stage=operation`.
   - `scheduled`: schedule registration plus observable execution of the scheduled operation.
   - `timer-extension`: routing to extension operation and expected output behavior.

To avoid a hanging session, it is allowed to:

- start in continuous mode,
- wait for minimum observable evidence,
- stop with controlled interruption (Ctrl+C equivalent).

### Step 5 - Completion

Mark as `COMPLETE` only when:

1. Requirements doc is consistent and complete.
2. Required automated tests are green.
3. Evidence of real Host CLI execution was captured.

Otherwise return `BLOCKED` with:

- executed command,
- collected evidence,
- objective failure,
- recommended next repair.

## Output Format

```markdown
## Modus Plugin Authoring Result

PluginName: <PluginName>
PluginType: <PluginType>
CsProject: <CsProject>
TestProject: <TestProject>
RequirementsDoc: <RequirementsDoc>

Requirements status:
- Checklist items done: <n>
- Checklist items remaining: <n>

Automated verification:
- dotnet build <CsProject>: passed/failed
- dotnet test <TestProject> --no-build: passed/failed
- Host integration tests (if applied): passed/failed

CLI runtime verification:
- Host command: <exact command>
- Startup diagnostics: observed/not observed
- Plugin activation diagnostics: observed/not observed
- Functional plugin evidence in output: observed/not observed

Overall status: COMPLETE | BLOCKED
```
