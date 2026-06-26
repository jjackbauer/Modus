---
kind: skill
mode: agent
description: Metrics role â€” compute all convergence metrics from current journal and distilled state, evaluate stopping criteria, and update convergence-metrics.md.
composes:
  - ../schemas/journal-schema.md
---

# Skill: Convergence Metrics

## Purpose

Read the current state of all journal files and distilled documents. Compute the 14 convergence
metrics, evaluate the five stopping criteria, and write an updated snapshot to
`.github/state/convergence-metrics.md`.

## Input (supplied by caller)

```
JournalDir:   <Path to journal files; defaults to harness/archive/journals/>
DistilledDir: <Path to distilled documents; defaults to harness/archive/distilled/>
StateDir:     <Path to state files; defaults to .github/state/>
```

## Role: Metrics Reporter

You operate as a **Metrics Reporter**. Your job is to provide an honest, quantitative view of
exploration progress. Do not omit metrics that show low coverage â€” accurate metrics are more
valuable than optimistic ones.

## Metric Definitions

| Metric | How to compute |
|---|---|
| **Modules explored** | Count distinct C# projects that have at least one OBS entry with a Source in that project's directory |
| **Execution flows mapped** | Count OBS entries whose Fact explicitly describes a call chain, delegation path, or data flow |
| **Dependencies verified** | Count OBS or VAL entries that confirm an inter-project or external dependency |
| **Observations recorded (OBS)** | Count all entries in `observations.md` |
| **Hypotheses proposed (HYP)** | Count all entries in `hypotheses.md` |
| **Hypotheses supported** | Count HYP entries with `Status: Supported` |
| **Hypotheses falsified** | Count HYP entries with `Status: Falsified` |
| **Validations completed (VAL)** | Count all entries in `validations.md` |
| **Open questions â€” P0 (OQ)** | Count OQ entries in `open-questions.md` with `Priority: P0` and `Status` â‰  Resolved |
| **Risks identified (RISK)** | Count all entries in `risks.md` |
| **Distilled docs updated** | Count distilled documents with `Last updated` â‰  `â€”` |
| **Artifact confidence (avg)** | Average confidence of ART entries in `artifact-index.md` (High=3, Medium=2, Low=1); report as number and label |
| **Inferred vs. observed claims ratio** | Count â“ claims across all distilled docs / total claim count; report as decimal |
| **Repeated uncertainty hotspots** | Count modules or concepts that appear in both `unresolved-areas.md` and `open-questions.md` |

## Stopping Criteria

| # | Criterion | Met when |
|---|---|---|
| 1 | Module coverage â‰¥ 100% | All C# projects in the solution have â‰¥ 1 OBS entry |
| 2 | Zero P0 open questions | No OQ entries with Priority: P0 and unresolved status |
| 3 | All distilled docs at â‰¥ Medium confidence | Every distilled document header shows Confidence: Medium or High |
| 4 | Inferred:observed ratio < 0.1 | â“ claims < 10% of total distilled claims |
| 5 | All artifacts have â‰¥ 3 evidence links | Every ART entry in `artifact-index.md` has â‰¥ 3 Supporting evidence IDs |

## Procedure

### Step 1 â€” Read all journals and distilled documents

Read every file in `JournalDir` and `DistilledDir`. Count entries and claims as defined above.

### Step 2 â€” Compute trend values

Read the previous `.github/state/convergence-metrics.md` snapshot (if it exists).
For each metric, compare to the previous value:
- â†‘ if the value increased
- â†“ if the value decreased
- â†’ if unchanged
- â€” if no previous value exists

### Step 3 â€” Evaluate stopping criteria

For each of the five criteria, determine Met (âœ…) or Not met (âŒ) based on computed values.

### Step 4 â€” Write the updated metrics file

Overwrite `.github/state/convergence-metrics.md` with the full updated content, following the
structure in the existing file (Metrics Table + Stopping Criteria + Update Notes).

Set **Last updated** in the file header to today's date.

### Step 5 â€” Report summary

```
Metrics updated: <YYYY-MM-DD>
Stopping criteria met: <N> of 5
Blocking criteria:     <list of unmet criteria, or "none â€” exploration complete">
```

If all five stopping criteria are met, output:
> **Convergence reached. Exploration may stop. All distilled artifacts are at sufficient evidence depth.**