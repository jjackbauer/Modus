````prompt
---
agent: agent
description: Reviewer role - audit current distilled knowledge for coverage gaps and produce OQ and TODO entries.
---

#file:.github/prompts/journal-schema.md
#file:.github/prompts/skill-impl-completeness.prompt.md

# Skill: Completeness Review

## Purpose

Given the current state of distilled knowledge documents and journal files, audit coverage across
12 architecture and delivery dimensions. For each gap, produce a new OQ entry (if investigation
is needed) or TODO entry (if targeted exploration is needed).

## Input (supplied by caller)

```
Scope:        <Modules or concerns to review, for example all or Plugin.Host only>
JournalDir:   <Path to journal files; defaults to .github/journals/>
DistilledDir: <Path to distilled docs; defaults to .github/distilled/>
```

## Role: Reviewer

Assume coverage is incomplete until evidence proves otherwise.

## Coverage Dimensions

| # | Dimension | Key questions |
|---|---|---|
| 1 | Modules | Have all modules/projects been explored? |
| 2 | Domain entities | Are core aggregates, value objects, and plugin descriptors documented? |
| 3 | Execution flows | Are startup, request handling, and plugin lifecycle flows traced end-to-end? |
| 4 | Critical dependencies | Are inter-module and external dependencies observed and validated? |
| 5 | Configuration surfaces | Are runtime settings and feature toggles documented? |
| 6 | Test coverage | Are tests cross-checked against implementation and contracts? |
| 7 | Thread safety | Is shared mutable state identified and evaluated? |
| 8 | Edge cases | Are null/empty/invalid/boundary scenarios covered? |
| 9 | Source-target gaps | If comparing systems, are missing or stubbed capabilities mapped? |
| 10 | Background operations | Are async jobs, timers, queues, or schedulers documented? |
| 11 | Trust boundaries | Are input validation, auth, and authorization boundaries documented? |
| 12 | Failure behavior | Are exception paths, degradation behavior, and operational assumptions documented? |

## Procedure

### Step 1 - Read journal state

Read journal files and distilled documents. Build coverage index by dimension.

### Step 2 - Optional completeness mapping

If Scope includes source-target comparison, invoke skill-impl-completeness for selected pairs.

### Step 3 - Evaluate each dimension

Assign status per dimension: Covered, Partial, or None, with concrete gap description.

### Step 4 - Produce coverage table

| # | Dimension | Status | Gap Description |
|---|---|---|---|
| 1 | Modules | Covered / Partial / None | <description or -> |

### Step 5 - Record OQ and TODO entries

- Investigation gaps: append OQ with priority P0/P1/P2.
- Targeted exploration gaps: append TODO with priority P0/P1/P2.

### Step 6 - Report summary

```
Scope reviewed:    <scope>
Dimensions:        <N> Covered, <N> Partial, <N> None
OQ added:          OQ-{NNN} through OQ-{MMM}  (<count> entries)
TODO added:        TODO-{NNN} through TODO-{MMM}  (<count> entries)
Blocking gaps (P0): <list or none>
```
````
