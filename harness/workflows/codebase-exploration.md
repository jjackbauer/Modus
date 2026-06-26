---
kind: workflow
mode: agent
description: Orchestrate one complete codebase exploration cycle in 7 steps using the journal-driven skill suite.
composes:
  - ../schemas/journal-schema.md
  - ../schemas/distilled-knowledge-schema.md
  - ../skills/journal-observe.md
  - ../skills/journal-hypothesize.md
  - ../skills/journal-validate.md
  - ../skills/journal-distill.md
  - ../skills/completeness-review.md
  - ../skills/convergence-metrics.md
---

# Workflow: Codebase Exploration

## Purpose

Orchestrate one complete journal-driven exploration cycle.
Run repeatedly until convergence-metrics reports all stopping criteria met.

## Input (supplied by caller or rule)

```
Objective:       <Optional narrow target, for example "Plugin contracts public API">
ExplorationMode: <focused | broad | auto>
DistillIfNew:    <yes | no | auto>
TargetDocs:      <Optional comma-separated distilled targets>
```

## Procedure

### Step 1 - Read current state

Read convergence metrics and open TODO/OQ/HYP entries.
Stop only if convergence criteria are already met.

### Step 2 - Select objective

Priority order:
1. Caller objective
2. Highest-priority P0 TODO
3. Oldest HYP under review
4. Least-explored module or concern from metrics

### Step 3 - Explore

Invoke journal-observe on chosen objective.
If zero OBS added, choose another objective and repeat Step 3.
Optionally invoke journal-hypothesize.

### Step 4 - Validate

If relevant HYP entries exist for explored area, invoke journal-validate.
Otherwise skip with note.

### Step 5 - Assess risks and open questions

Update or close related OQ entries, add new RISK entries where needed.
If mode is broad, invoke completeness-review for explored scope.

### Step 6 - Distill

Distill when any condition holds:
- DistillIfNew is yes
- DistillIfNew is auto and at least 3 OBS added
- At least one Supported VAL added
- A P0 OQ was resolved

Invoke journal-distill with source entry IDs and target docs.

### Step 7 - Metrics

Always invoke convergence-metrics and output cycle summary:

```
Cycle summary
-------------
Objective:          <objective>
Exploration mode:   <mode>
OBS added:          <count>
HYP added:          <count>
Validated:          <supported>/<falsified>/<unresolved>
Distilled:          <targets or skipped>
Stopping criteria:  <N of 5 met>
Next recommended:   <next objective>
```

If all stopping criteria are met, output convergence reached.