---
kind: rule
mode: agent
description: Preserve the user's named mechanisms through evaluation, requirements, goals, and completion.
---

# Rule: Intent Fidelity

## Purpose

Prevent a locally rigorous plan from delivering a different product than the one the user named.

## Non-Negotiable Policy

- Quote the user's request verbatim in the requirements document or goal contract.
- List every named mechanism/entity from the request and from the source's defining vocabulary.
- A verdict of `Reject` or `Defer` on a **headline mechanism** is a user-owned scope decision, not a finding. Put it in the `Scope Decisions Requiring Sign-Off` table with `Owner: user` and `Status: pending`, and ask the user.
- A requirements document with a pending headline decision is `DRAFT - BLOCKED ON SIGN-OFF`. It may be reviewed, but it may not be used as a build spec and no goal may be created from it.
- Record constraint provenance: `user` / `repo` / `agent`. An agent-inferred constraint may not exclude a user-named mechanism.
- A measurable proxy that removes a named mechanism is a different goal, not a measurement choice.
- A scripted model is a unit-test double; it is never evidence that a model drove the harness.

## Scope Sign-Off Condition

A requirements document is build-ready only when:

1. The verbatim request and named mechanisms are present.
2. Every headline mechanism is either landed in a requirement or has a user-approved exclusion.
3. The Scope Decisions table has no pending headline row.
4. The test plan covers the mechanism the user named, not only the proxy metric.

If any condition fails, the document must be revised or the decision escalated before save.

## Goal Contract Condition

A goal created from this document must:

1. Store the verbatim request and the named mechanisms (anchors) with the goal.
2. Re-inject them at the start of every continuation round.
3. Reject an objective that drops an anchor without a user-approved exclusion.
4. Carry an anchor-to-artifact/evidence trace before it is marked complete.

If the goal tool cannot store anchors, record them in `goal.md` and treat them as a hard gate in every round.