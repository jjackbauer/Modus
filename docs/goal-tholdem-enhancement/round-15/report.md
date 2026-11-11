# Round 15 report — pre-registered equity-policy experiment: negative result

**Status:** verified negative result. HYP-002 falsified; no strategic-improvement claim. Goal active.

## Delivered

A pre-registered experiment through the new `evaluate4` CLI: equity-aware challenger vs legacy
strategy incumbent on the holdout opponent pool, with an anchor composition and the independent
verifier. The preregistration was written before the run.

## Verified

| Check | Observed |
|---|---|
| Pre-registration hash | `67e048bc…` (before evaluation) |
| Plan / anchor hashes | `3af4d905…` / `b67d7cad…` |
| Policy / incumbent hashes | `760ac54c…` / `f100e793…` |
| Primary mean / lower | **-93.83** / **-454.76 bb/100** |
| Gate | **Unsafe / unsafe-anchor-regression** |
| Anchor lower | **-961.73 bb/100** |
| Verifier | `verifier:verified` |
| Report hash | `4c18e8bc…`; equals the file SHA-256 |
| Pointer | none written |

## Interpretation

The default equity policy is passive and loses to the legacy heuristic in four-way play, with a
catastrophic anchor regression. This falsifies HYP-002 for this configuration. It is a legitimate
negative result: the gate and verifier worked, and no claim is being reframed as success.

## Next

Final consolidation (round 12): update the campaign report and coverage, state the open P0 rows
(D5, D8 full, D9, D10, D1 training path) and leave the goal active. Animating the equity policy
(less conservative uncertainty rule, more samples, better preflop thresholds) is future work, not a
claim made here.
