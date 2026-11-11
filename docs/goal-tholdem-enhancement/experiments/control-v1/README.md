# Control experiment v1 — pre-registered dominant/dominated check

**Question:** does the four-player paired evaluator and the pre-registered statistical gate detect a
real payoff difference between two scripted policies, on data and opponents that were fixed before
the run?

**Hypothesis (written before the run):** a scripted **fold-only** policy beats a scripted **call-any**
policy on the pre-registered four-seat holdout population. This is a *negative control pair*, not a
strategic champion: fold-only is a disciplined policy that never pays off, call-any is dominated
because it pays every bet. The point is to prove the instrument can detect a known difference.

## Pre-registration

`preregistration.json` was written **before** either report. It fixes:

- the game: four seats, 1000 chips, 5/10 blinds, cash independent hands, action cap 1000;
- the pool: `tholdem.opponents.holdout.v1` (five held-out identities, disjoint from the selection pool);
- the compositions: `primary-mixed`, `three-tag`, `three-station`, `pressure`;
- the seed domains: training 64 / selection 64 / holdout 288 / audit 32, all disjoint;
- 20 hands per seat match, focal rotations (4 seat assignments per block);
- the decision rule: minimum practical effect 10 bb/100, confidence 0.95, 1 planned candidate,
  anchor regression tolerance 5 bb/100;
- the anchor plan: same game and the same holdout seed domain, composition `primary-mixed`;
- both policies and their content hashes.

Pre-registration file hash: `0f7313586f4da03b812fbe80bda68c7fca7a915615d334eba91845f025e51770`
(`preregistration.sha256` contains exactly this value).

## Observed result

Primary report (4 compositions × 288 blocks = 1,152 units):

| Metric | Value |
|---|---|
| Mean paired difference | **+199.738 bb/100** |
| 95% block lower bound (normal + bootstrap, conservative min) | **+176.917 bb/100** |
| 95% block upper bound | +222.559 bb/100 |
| Units / seed blocks | 1,152 / 288 |
| Report hash | `40201148ffa5e1d4cbc01306f4535b70653af9754e71d1451670e84b52f151df` |

Anchor report (1 composition × 288 blocks = 288 units):

| Metric | Value |
|---|---|
| Mean paired difference | +144.651 bb/100 |
| 95% block lower bound | **+115.038 bb/100** |
| Report hash | `f403567400a33c77036be4f9d4bb7953a7b1a6b6dba75d67aa6ccffade7debf4` |

Gate verdict: **VerifiedImprovement / verified-improvement**, lower bound +176.917 > margin 10, and
the anchor lower bound +115.038 > -5, so there is no anchor regression.

Independent verifier: **true / verifier:verified**. The verifier re-ran both evaluations with fresh
agent instances and required the recomputed report hashes to equal the claimed hashes, then required
the supplied decision to be exactly the one the gate reaches.

Negative controls (same run):

- a forced "verified improvement" decision on the honest reports → **refused**, `verifier:decision-mismatch`;
- a report whose unit means were shifted by −1000 bb/100 → **refused**, `verifier:report-mismatch`.

## Cold reproduction

```
dotnet run --project docs/goal-tholdem-enhancement/experiments/control-v1/harness -c Release -- \
  docs/goal-tholdem-enhancement/experiments/control-v1/results
```

The harness writes the pre-registration first, then the reports, decision, verification and controls,
and returns 0 only when the verifier accepts and both negative controls are refused. A second
process wrote to `results-rerun/`; every one of the six artifacts is **byte-identical** to the first
run (SHA-256 equal), including both reports and the decision.

## What this does not claim

- It does **not** promote a poker strategy or move any champion pointer. Both policies are scripted
  controls; the gate verdict is evidence about the evaluator, not a strategic improvement claim.
- It does **not** exercise an LLM. The challenge procedure (propose → select → hold out) is not run
  here; only the evaluation/gate/verifier stage is.
- It does **not** prove the four-seat population is representative of real opponents. The pool is
  frozen and synthetic; its purpose is reproducibility, not realism.
- The fold-only policy is not "good poker". Its value is that its dominance over call-any is known
  and large, which makes a false negative or a false promotion visible.

## Artifacts

```
preregistration.json   0f7313586f4da03b812fbe80bda68c7fca7a915615d334eba91845f025e51770
preregistration.sha256 (the same hash, as text)
primary-report.json    40201148ffa5e1d4cbc01306f4535b70653af9754e71d1451670e84b52f151df
anchor-report.json     f403567400a33c77036be4f9d4bb7953a7b1a6b6dba75d67aa6ccffade7debf4
decision.json          66248016ad7b53bb307e17d982744ba25d3862a921fe9e0e2670f4ece145f9d6
verification.json      04614beb53e87ae9cf58eac4ba21aef71d1c15f12e032f798b7df98d56d1acd2
controls.json          fb434910f59ce28cc9abb68ddf4410d09890143c33bc24a3a7ae3434173aff4a
```
