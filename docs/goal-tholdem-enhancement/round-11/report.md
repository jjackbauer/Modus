# Round 11 report — measured VPIP with explicit missingness

**Status:** verified; hidden-data lie fixed. D2 remains Partial; goal active.

## Delivered

- `HandOutcome.VoluntaryPreflop` records, per seat and hand, whether the seat voluntarily put chips
  in preflop (call/bet/raise/all-in; checks and blinds excluded).
- `VpipSummary(Hands, VoluntaryPreflopHands)` with `Vpip => Hands == 0 ? null : rate`.
- `SimulationReport.Vpip` carries one summary per seat; it is deliberately not serialized.
- `SessionAnalysis.Vpip` is `double?` with `VpipHands` and `VpipVoluntaryHands`.
- `StrategyAnalyzer` copies the summary or returns null/0 counts when absent — never a fabricated 0.
- The model prompt writes `"vpip":null` or the measured number, plus `vpipHands` and `vpipVoluntary`.

## Verified

| Check | Observed |
|---|---|
| Fold/check seat, 10 hands | 0/10, VPIP **0.0** (measured zero) |
| Call/check seat, 10 hands | 5/10, VPIP **0.5** |
| Hand-built report without a summary | `Vpip is null`, counts 0 — not zero |
| Prompt | `"vpip":null,"vpipHands":0,"vpipVoluntary":0` and `"vpip":0,"vpipHands":10,...` |
| Canonical report JSON / AgentMetrics | untouched; all reproducibility tests pass |
| Tests / suite / build | 5/5 new, **549/549**, 0 errors |

## Authorized existing-test updates

- `StrategyAnalyzerTests`: the old assertion pinned the fabricated zero; it now checks the measured
  value and denominator (DEC-006).
- `LlmPromptBuilderTests`: the analysis-member count grew from 6 to 8 because the prompt contract
  now carries the two denominator fields.

Both changes are semantic/additive corrections required by OBS-005, not weakened assertions.

## Still open

- Only seat-0 VPIP is wired into `SessionAnalysis`; PFR, three-bet and fold-to-raise remain absent
  (OBS-006).
- D2 still lacks a versioned `DecisionContext`; D5 diagnostics are unstarted.
- Anchor 3 remains the round-7 null; no learning claim.

## Next

Integrate `CallEvCalculator` and preflop situation branches into `EquityPolicyAgent` using the new
totals/history, then add the four-seat CLI surface. A credible anchor-3 retry needs that richer
policy family.
