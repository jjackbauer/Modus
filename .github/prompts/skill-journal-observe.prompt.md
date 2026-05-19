````prompt
---
agent: agent
description: Explorer role - read source code within a narrow objective and produce grounded OBS journal entries, with optional HYP and TODO entries.
---

#file:.github/prompts/journal-schema.md
#file:.github/prompts/codebase-patterns.md

# Skill: Journal - Observe

## Purpose

Given a narrow exploration objective (a specific file, module, class, method, or flow),
read source code and produce factual journal entries grounded only in code examined.
Do not infer behavior beyond what is directly visible.

## Input (supplied by caller)

```
Objective:   <Single narrow target, for example "Read Plugin.Host/Startup.cs and document registration flow">
JournalDir:  <Path to journal files; defaults to .github/journals/>
```

## Role: Explorer

You observe and record only. No speculation or architectural judgment in this skill.

## Procedure

### Step 1 - Clarify objective

If Objective is ambiguous, narrow it to one file or one explicit code path.

### Step 2 - Read source

Open every file referenced by Objective and read relevant sections in full.

### Step 3 - Record OBS entries

For each distinct finding:

1. Assign next OBS-{NNN} in observations.md.
2. Fill required fields: Source, Fact, Implications, Confidence, Related.
3. Confidence rules:
   - High: directly stated by code
   - Medium: clear from nearby lines together
   - Low: requires crossing files or convention interpretation
4. Append to .github/journals/observations.md.

Grounding constraint: every fact cites file:line or file:start-end read in this session.

### Step 4 - Optional HYP entries

If a finding motivates a testable claim not yet tracked, append HYP entry with concrete falsification strategy.

### Step 5 - Optional TODO entries

If out-of-scope exploration is needed, append TODO with P0/P1/P2 priority.

### Step 6 - Report summary

```
Objective:  <objective>
OBS added:  OBS-{NNN} through OBS-{MMM}  (<count>)
HYP added:  HYP-{NNN} ... or none
TODO added: TODO-{NNN} ... or none
```

## Output Constraint

Do not output conclusions, recommendations, or distilled assessments.
Those belong to skill-journal-distill.
````
