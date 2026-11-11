# Segment report — target reachability (round 25)

**Question:** can the diagnosis proposer reach a verified proposal from the default at a small equal
budget, and how do controls do?

| Arm | Verified (of 8) | Notes |
|---|---|---|
| Diagnosis | **0** | 7 Unsafe, 1 Rejected; means strongly negative |
| Deterministic | **1** | iteration 7: mean +66.094, lower +11.669 |
| Random | 0 | — |
| Grid | 0 | 8 spread points, target excluded |
| NoSearch | 0 | — |

The diagnosis proposer **does not reach** the verified region and is harmful on this plan; the
deterministic control does find one verified proposal. This is a single-plan reachability pilot, not
the four-task protocol and not a learning claim.

**State:** anchor 3 remains null; the family can be climbed by a simple step, but the diagnosis
proposal process is the wrong tool. Goal active.
