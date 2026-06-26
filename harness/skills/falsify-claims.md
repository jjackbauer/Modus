---
kind: skill
mode: agent
description: Verify or refute claims against source evidence in the workspace and optional reference code.
subagents:
  - falsifier-a
  - falsifier-b
  - falsifier-c
  - synthesizer
---

# Skill: Falsify Claims

## Purpose
Given a list of claims about behavior, structure, naming, or logic, search the workspace source files
(and optional external reference paths supplied by the caller) for supporting evidence or counterexamples.
Classify each claim as Supported or Falsified.

## Input
A numbered list of claims, supplied by the caller.
Optional scope constraints may be included by the caller, such as target folders or file types.

## Agent Roles

Use parallel subagents if available; otherwise run independent passes:

- Falsifier A: reviews all claims independently.
- Falsifier B: reviews all claims independently.
- Falsifier C: reviews all claims independently.
- Synthesizer: reconciles results and applies the loop instruction.

## Procedure

1. Use parallel subagents if available; otherwise run 3 independent falsifier passes (falsifier-a, falsifier-b, falsifier-c) with the full claim list.
2. Each falsifier pass, for every claim:
   a. Locate direct evidence in source code, tests, config, and docs.
   b. Attempt a concrete counterexample.
   c. Classify:
      - Supported: evidence confirms claim and no counterexample found.
      - Falsified: contradiction or counterexample exists.
   d. Return a complete Markdown table with evidence paths and line references.
3. Run synthesizer pass (use parallel subagent if available; otherwise run as a separate pass):
   - If all three agree, use consensus.
   - If disagreement exists, default to Falsified and include conflict note.
   - Produce final merged table sorted by claim number.

## Output Format

| # | Claim | Evidence (file:line) | Status | Reason |
|---|---|---|---|---|
| 1 | ... | src/file.cs:42 | Supported | Confirmed by implementation |
| 2 | ... | src/other.cs:31 | Falsified | Counterexample found |

## Loop Instruction

After producing the merged table, state the number of Falsified rows.
If any rows are Falsified, instruct caller to revise those claims and re-run until zero Falsified rows remain.
