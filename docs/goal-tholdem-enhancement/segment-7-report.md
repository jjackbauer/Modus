# Segment report — policy-family feasibility probe (round 24)

**Question:** does the four-threshold family contain any genome that clears the gate against the
default, or is the family itself the bottleneck?

**Answer:** it does contain one. Of 144 pre-registered grid candidates:

| Genome (raise/call/bluff/sizing) | Verdict | Mean | Lower |
|---|---|---|---|
| 0.95 / 0.20 / 0.40 / 0.75 | **VerifiedImprovement** | +150.625 | +71.452 |
| 0.40 / 0.40 / 0.20 / 1.25 | Unsafe | +1646.25 | +594.86 |

Only 1 of 144 verified; the highest-mean candidate was Unsafe at the anchor. This is an **existence**
result: the family can represent a gate-passing improvement over the default, so the attack surface
is the proposal/search process, not only the family.

**Not claimed:** that any proposer finds this candidate, that the diagnosis/LLM arms are better, or
any learning competence. Anchor 3 remains null.

**Next:** target-reachability test — can the diagnosis proposer reach the verified region under the
same budget, and do the controls fail to? If yes, a learning signal exists; if no, the proposer is the
bottleneck.
