---
kind: schema
description: Shared boilerplate for requirements documents produced by requirements-gathering and related workflows.
---

# Requirements Document Template

> **Usage**: Reference when assembling or validating requirements output. Workflows should populate placeholders and run the plan-format-gate and absolute-behavior compliance checks before save.

---

## Document Skeleton

```markdown
# <OutputTitle>

> <Scope statement derived from caller inputs>

---

## Functionality Worktree

### Verification Policy

- Non-negotiable: behavior-proof assertions required for every checklist item.
- Metadata-only assertions are supporting evidence only.
- API/provider tests are valid only when thorough runtime integration gates are asserted.
- Negative-path tests must prove deterministic rejection and no side-effect execution.

### Coverage Inputs

| Input | Value |
|---|---|
| CsProject | <Target C# project name> |
| AnalysisSource | <How the checklist was derived> |
| MandatoryItems | <List with tags, or none> |
| PlanType | <requirements | generic | architecture requirements> |
| OutputPath | harness/requirements/<CsProject>.md |

### Class Diagram

<Mermaid classDiagram block when applicable; omit section if not produced>

### Completeness Checklist

- [ ] <item 1> [<dependency or mandatory tag>]
- [ ] <item 2> [depends on <prior item>]
- [ ] Enforce absolute behavior-proof verification for every planned integration test [mandatory - behavior-proof policy]

### Checklist to Runtime-Proof Matrix

| Checklist Item | Primary Runtime Proof Path | Minimum Evidence |
|---|---|---|
| <item summary> | <DI / API / scheduled / negative path> | <executable assertion summary> |

---

## Test Plan

### `<Method or feature name>`

1. `<TestName_GivenScenario_ExpectedResult>`
   *Assumption*: <one sentence requiring executable runtime proof>

2. `<TestName_GivenScenario_ExpectedResult>`
   *Assumption*: <one sentence>

### Absolute Behavior-Proof Compliance Gate

1. `BehaviorProofCompliance_GivenChecklistItems_RequiresExecutableRuntimeProofForEachItem`
   *Assumption*: Every checklist item is compliant only when backed by executable runtime assertions.

2. `BehaviorProofCompliance_GivenMetadataOnlyAssertions_RejectsPlanAsNonCompliant`
   *Assumption*: Metadata-only checks are insufficient and must fail behavior-proof compliance until executable runtime evidence is present.

3. `BehaviorProofCompliance_GivenApiFocusedCoverage_RequiresOwnerSemanticsBusinessSemanticsLifetimeCorrelationIsolationAndNegativeContracts`
   *Assumption*: API-focused tests are compliant only when integration gates prove owner resolution, business semantics, lifetime path, correlation continuity, isolation, and negative contracts through executable runtime evidence.

---

## Format Gate Results

| # | Rule | Status | Violations |
|---|---|---|---|
| 1 | Heading structure and separators | ✅ Pass / ❌ Fail | — |
| 2 | Scope statement under H1 | ✅ Pass / ❌ Fail | — |
| 3 | Pipe tables present | ✅ Pass / ❌ Fail | — |
| 4 | Checklists with dependency tags | ✅ Pass / ❌ Fail | — |
| 5 | Mermaid diagrams (conditional) | ✅ Pass / ⏭️ Skipped / ❌ Fail | — |
| 6 | Verification gate evidence | ✅ Pass / ❌ Fail | — |
| 7 | Closing verification line | ✅ Pass / ❌ Fail | — |
| 8 | Numbered test plan items | ✅ Pass / ❌ Fail | — |

**PASS** — document conforms to plan format.

---

## Absolute Behavior Verification Compliance Check

| Condition | Result | Evidence |
|---|---|---|
| Every checklist item maps to named tests | Pass / Fail | <brief evidence> |
| Behavior-proof assertions present for every item | Pass / Fail | <brief evidence> |
| Metadata-only tests absent as sole evidence | Pass / Fail | <brief evidence> |
| API-focused items include absolute integration gates | Pass / Fail | <brief evidence> |

---

*All assumptions verified by Falsify Claims. Zero Falsified rows.*
```

## Mandatory Sections

Every saved requirements document must include:

1. **Verification Policy** — behavior-proof is non-negotiable; metadata-only is supporting evidence only.
2. **Completeness Checklist** — unchecked items with bracketed dependency or mandatory tags.
3. **Test Plan** — numbered items with backtick-wrapped test names and `*Assumption*:` lines.
4. **Format Gate Results** — PASS verdict from plan-format-gate (or self-healed to PASS).
5. **Absolute Behavior Verification Compliance Check** — explicit pass/fail table before save.
6. **Closing verification line** — italicized line in the last five lines containing `Zero Falsified`.

## Behavior-Proof Minimum Gates

Each checklist item must map to tests that prove at least one runtime path:

| Path | When required |
|---|---|
| DI resolver | Plugin/capability resolution and lifetime |
| API dispatch | Endpoint or operation invocation with payload semantics |
| Scheduled execution | Timer or recurring job outcomes |
| Negative / isolation | Deterministic rejection without side effects |

API-focused items must additionally assert owner resolution, business semantics, DI lifetime, correlation continuity, isolation after failure, and negative contract semantics — not HTTP status alone.

## Workflow Integration

Requirements-gathering workflows must:

1. Assemble content using this skeleton.
2. Run plan-format-gate with the caller's `PlanType` until PASS.
3. Run absolute-behavior compliance check; treat missing behavior-proof tests as FAIL.
4. Save to `OutputPath` under `harness/requirements/`.
5. Append the closing verification line before finishing.
