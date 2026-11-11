# Round 5 report — typed equity and N-opponent expected pot share

**Status:** verified slice; D3 Partial. The goal remains active.

## Delivered

- `EquityEstimate`: win probability, tie probability, expected pot share, sample count, opponent count,
  method, counts, standard error and a 95% interval. Equity is now a probability estimate, not a
  hand-category rank.
- `HandCategoryScore`: a typed relative rank score with no implicit/explicit conversion to `double`
  or `EquityEstimate`.
- `MultiwayEquity.Estimate`: seeded Monte Carlo for 1–5 opponents, sampling every hand and the runout
  without replacement, splitting ties `1/k`.
- Runtime `equity-calculator`: optional `opponents` (1–5). One opponent dispatches to the unchanged
  legacy estimator; 2–5 dispatch to the multiway estimator and report `expectedPotShare`.
- The catalog keeps exactly four tools and the heads-up output fields are unchanged.

## Verified

| Check | Observed |
|---|---|
| Exhaustive river oracle, 2 opponents | sampled **0.79785** vs exhaustive **0.7961475217289171**, delta **0.00170** over 148,995 four-card sets / 446,985 pairings |
| Four-way board tie | win 0.0, tie 1.0, pot share **0.25** exactly |
| Five opponents | share 0.5609, counts sum to samples |
| Heads-up tool path | every legacy field exactly equal; `ExpectedPotShare == Equity`, `Opponents == 1` |
| Equity test filter | **12/12** |
| Full suite | **507/507**, 0 failed |
| Build | 0 errors |

## Not done (D3 still Partial)

- **EQ-004**: opponent hands are still sampled uniformly; range-conditioned sampling with the uniform
  sampler as a labelled control is not implemented.
- **EQ-005**: no contribution/side-pot-aware call EV; only showdown pot share exists.
- **EQ-006**: intervals are reported, but no decision rule refuses confidence-free decisions near
  action thresholds.
- Anchor 3 (equal-budget non-LLM learning ablation) is untouched. This round does not substitute
  equity work for that claim.

## Reproduction

```
dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release
dotnet run --project .tmp/tholdem-verify/RunXunit -c Release -- \
  WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll Tests.Equity
dotnet run --project .tmp/tholdem-verify/RunXunit -c Release -- \
  WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
```
