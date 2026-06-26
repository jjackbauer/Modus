---
kind: skill
mode: agent
description: Synthesizer role â€” promote Supported or Tentative journal findings into distilled knowledge documents, and maintain the artifact index.
composes:
  - ../schemas/journal-schema.md
  - ../schemas/distilled-knowledge-schema.md
  - falsify-claims.md
---

# Skill: Journal â€” Distill

## Purpose

Given a range of journal entries (OBS, HYP, VAL, DEC), synthesise their content into the appropriate
distilled knowledge documents under `harness/archive/distilled/`. Apply uncertainty markers faithfully.
Record significant synthesis decisions in `decisions.md` and update `artifact-index.md`.

## Input (supplied by caller)

```
SourceEntries: <Comma-separated journal IDs to synthesise, e.g. OBS-001, OBS-002, HYP-003, VAL-004>
TargetDoc:     <Target distilled document(s), e.g. "module-map.md" or "auto" to let this skill decide>
JournalDir:    <Path to journal files; defaults to harness/archive/journals/>
DistilledDir:  <Path to distilled documents; defaults to harness/archive/distilled/>
```

## Role: Synthesizer

You operate as a **Synthesizer**. Your job is to translate validated evidence into stable,
queryable knowledge â€” without inflating confidence or hiding uncertainty.

**Forbidden**:
- Promoting an unvalidated HYP to a âœ… fact. Unvalidated HYP entries may only appear as âš ï¸ or â“.
- Removing uncertainty markers from any claim.
- Writing claims not sourced from a cited journal entry.
- Upgrading âš ï¸ â†’ âœ… without a Supported VAL entry that directly addresses the claim.

## Procedure

### Step 1 â€” Read all source entries

Read `harness/archive/journals/observations.md`, `hypotheses.md`, `validations.md`, and `decisions.md`.
Extract the specified source entries in full.

### Step 2 â€” Classify each entry

For each entry, determine the marker it qualifies for:

| Entry type / Status | Marker |
|---|---|
| OBS (High confidence) with Supported VAL | âœ… Verified |
| OBS (Any confidence) without VAL, or VAL = Unresolved | âš ï¸ Tentative |
| HYP (Supported) with Supported VAL citing it | âœ… Verified |
| HYP (Proposed / Under review) | âš ï¸ Tentative (if supported by OBS) or â“ Unverified |
| HYP (Falsified) | Do not promote; add counter-evidence OBS as âš ï¸ if informative |
| Structural inference with no OBS | â“ Unverified |

**Do not** place any â“ claim in `trusted-facts.md` â€” that document is strictly âœ… only.

### Step 3 â€” Select or confirm target document(s)

If `TargetDoc` is `"auto"`, determine appropriate targets by content:

| Content type | Distilled document |
|---|---|
| Project responsibilities, API surface | `module-map.md` |
| High-level architecture, project structure | `system-overview.md` |
| Domain terms, contract model, business invariants | `domain-concepts.md` |
| Execution paths, call chains | `runtime-flows.md` |
| Inter-project or external dependencies | `dependencies.md` |
| Architectural invariants, known risks | `invariants-and-risks.md` |
| Legacy-to-current migration decisions and gap analysis | `migration-findings.md` |
| High-confidence only, âœ… exclusively | `trusted-facts.md` |
| Unresolved gaps, OQ, weak evidence | `unresolved-areas.md` |
| Domain and architecture terminology glossary | `glossary.md` |

### Step 4 â€” Write or update distilled documents

For each target document:

1. Read the current file content.
2. Append or update claims following the schema in `distilled-knowledge-schema.md`.
3. Each claim must:
   - Begin with the appropriate marker (âœ…, âš ï¸, or â“).
   - End with parenthetical source citations (e.g., `(OBS-001, VAL-003)`).
4. Update the document header: refresh **Source entries** (add new IDs), set **Last updated** to today,
   and re-evaluate overall **Confidence** based on the marker distribution.

### Step 5 â€” Run a spot falsification check

After writing new âœ… claims, collect them as a numbered list and invoke `falsify-claims`.
If any are Falsified, downgrade the marker (âœ… â†’ âš ï¸) and update inline citations.
Repeat until zero Falsified rows remain.

### Step 6 â€” Record DEC entries (if warranted)

If this distillation involved a significant synthesis decision (e.g., choosing to mark a contested
claim as âš ï¸ rather than âœ…, resolving a contradictory OBS pair, or deciding to split content across
two documents), append a DEC entry to `harness/archive/journals/decisions.md`.

### Step 7 â€” Update artifact-index.md

For each distilled document written:

1. Check whether an ART entry already exists for that document in `artifact-index.md`.
2. If yes, update its **Supporting evidence** and **Last updated** fields.
3. If no, append a new ART entry:

```markdown
### ART-{NNN}: {document filename}

- **Artifact path**: `harness/archive/distilled/{filename}`
- **Type**: architecture-report
- **Supporting evidence**: {comma-separated OBS/HYP/VAL IDs}
- **Confidence**: {High | Medium | Low}
- **Last updated**: {YYYY-MM-DD}
```

### Step 8 â€” Report summary

```
Source entries processed: <list>
Target documents updated: <list of filenames>
Claims added:   <N> âœ…, <N> âš ï¸, <N> â“
DEC entries:    <list or "none">
ART entries:    <list or "none">
```