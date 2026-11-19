# Status glossary and precedence

Normative vocabulary for every Wip.Factory Control Plane surface. A page may not invent a status,
synonym or colour meaning outside this document. The UI must read these values from
`control-plane-domain::labels`; the adapter remains the authority for the underlying facts.

## 1. The four classes of status

| Class | Question it answers | Source | Rendering rule |
|---|---|---|---|
| Authoritative factory state | Where is this work item in the factory state machine? | `WorkItemSummary.state` | Full label in the page header; never duplicated as a chip in the body. |
| Derived indicator / health | What is happening or waiting right now? | `derived_indicator`, `execution_health` | Secondary, immediately after state, only when it differs from the state label. |
| Verification verdict | Did the merged revision pass engineering verification? | `Release.verification.verified` | Attached to the release record, never to the work item header. |
| Available action | What can an operator do, and with what authority? | `next_actions`, role checks | Buttons carry the action verb; unavailable actions are disabled with a stated reason. |

One fact has one class. A chip is never the only carrier of meaning: every status is also written
in words in the surrounding layout.

## 2. Authoritative factory states

Canonical enum is PascalCase in the adapter; the UI renders the human label.

| Enum | Human label | Category | Actionable | Tone |
|---|---|---|---|---|
| Proposed | Proposed | in-flight | Yes (approve contract) | neutral |
| RequirementsAligned | Requirements aligned | in-flight | No | neutral |
| ArchitectureRefined | Architecture refined | in-flight | No | neutral |
| ReadyForImplementation | Ready for implementation | in-flight | Yes (dispatch) | neutral |
| InImplementation | In implementation | in-flight | No | active |
| InReview | In review | in-flight | No | active |
| ReadyForDelivery | Ready for delivery | in-flight | Yes (authorize merge) | waiting |
| DeliveryValidation | Delivery validation | in-flight | No | active |
| ReleaseCandidate | Release candidate | in-flight | No | active |
| Released | Released | in-flight | Yes (start/stop serve) | success |
| OperationallyValidated | Operationally validated | in-flight | Yes (start/stop serve) | success |
| Blocked | Blocked | attention | No (terminal here) | warn |
| NeedsRework | Needs rework | attention | Yes (dispatch a rework attempt) | warn |
| Failed | Failed | terminal | No | danger |
| Cancelled | Cancelled | terminal | No | warn |
| Closed | Closed | terminal | No | neutral |

Unknown values are rendered verbatim with tone `neutral`; they are never mapped to a nearby state.

## 3. Derived indicators (precedence order, highest first)

The first matching rule wins. An indicator is shown only when its label differs from the state label.

1. `terminal-failure` — state Failed.
2. `blocked` — state Blocked.
3. `waiting-contract-approval` — state Proposed and a proposal digest exists and no contract is frozen.
4. `waiting-merge-approval` — state ReadyForDelivery, an accepted review digest exists, and no merge
   approval bound to that digest is recorded.
5. `verification-failed` — release verification exists with `verified = false`.
6. `implementation-failed` — last failure category is an implementation failure.
7. `needs-rework` — state NeedsRework.
8. `active-implementation` — state InImplementation and a dispatch is active.
9. `running-verification` — state InReview or DeliveryValidation.
10. `released` / `operationally-validated` — the corresponding state.
11. `delivery-in-progress` / `release-in-progress` — the corresponding state.
12. `awaiting-intake-dispatch` — state ReadyForImplementation with no dispatch active.

Definitions used above:

- **blocked** — the factory state machine refuses normal transitions; only a recorded factory
  transition or a new work item can change it.
- **terminal failure** — state Failed; the item is not reopened by this control plane.
- **verification failed** — post-merge verification ran and did not pass, or passed with a verdict
  that does not cover the merged revision.
- **needs attention** — any Attention item with severity `critical` or `warning`.
- **pending authorization** — an action that requires a human, exact-digest decision before the
  factory will advance.

## 4. Verification verdicts

| Code | Meaning | Where shown |
|---|---|---|
| verified | Build, existing regressions and the frozen contract passed on the exact merged revision, bound to the contract and evidence digests. | Release detail, release list |
| not-verified | No verification record exists for this candidate. | Release detail, release list |
| failed | Verification ran and did not pass. | Release detail |

A merge alone never renders as verified, and independent acceptance is never inferred from any of
these.

## 5. Serving states

The journal is historical evidence, not process liveness. After an adapter restart a journal row
that says `serving` must not render as serving. `serving_view` enforces this once for every surface.

| Code | Label | Meaning | Tone |
|---|---|---|---|
| serving | Serving | The current adapter tracks a running supervised process. | success |
| starting | Starting | The adapter is starting the supervised process. | active |
| stopping | Stopping | A stop was requested and is being applied. | active |
| stopped | Stopped | The last window ended normally. | neutral |
| expired | Serve window expired | The bounded window ended. | neutral |
| failed | Serve failed | The supervised process failed or crashed. | danger |
| not-served | Not serving | No serve record exists for this revision. | neutral |
| unknown | Not currently serving | Last journal record looks live but no process is tracked by this adapter. | warn |

`Can start` requires a released/served state and no active window; `can stop` requires an active
window. Both are governed mutations and use the same confirmation dialog.

## 6. Attention and deduplication

An attention item is one actionable operator decision. Its identity is:

```
key = work_item_id | action          (when both are present)
key = "id|" + attention_id           (otherwise)
```

`action` is the governed verb: `approve-contract`, `approve-merge`, `approve-template`,
`start-serve`, `stop-serve`, `dispatch`, `inspect-events`, `inspect-release`, `inspect-settings`.

Consequences:

- One pending merge produces exactly one attention row even though the approvals inbox and the work
  detail projection both describe it. The row keeps the fuller work-detail explanation and the
  inbox endpoint.
- Distinct actions on the same work item are distinct rows.
- Items without a work item (template policy) fall back to their id and never collapse.

## 7. Never say

| Forbidden | Use instead |
|---|---|
| "Active" (unqualified, for a released item) | Serving / Running (with the process fact) |
| "Done", "Complete" for a work item | Released / Closed, whichever is true |
| "Approved" for a merge authorization | Merge authorized |
| "Verified" for a merge or a passing local test | Release verified (only after post-merge verification) |
| "Failed" for a transport disconnect | Activity disconnected / reconnecting |
| "0" for an unmeasured value | Not measured |
| "Paused", "Scheduled", "Retrying" | Only the factory states that exist; this control plane has no such verbs |

## 8. Source of truth

- Labels and tones: `control-plane/domain/src/labels.rs`.
- Serving reconciliation: `control-plane/domain/src/labels.rs::serving_view` plus
  `Wip.Factory.ControlApi/Services/ServeManager.cs::GetServing`.
- Attention identities: `Wip.Factory.ControlApi/ReadModels/ReadModelWorkDetail.cs::BuildAttention`
  and `control-plane/server/src/overview.rs::canonical_attention`.
