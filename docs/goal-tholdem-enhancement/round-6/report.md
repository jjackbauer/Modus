# Round 6 report — range conditioning and confidence-gated decisions

**Status:** verified slice; D3 now lacks only EQ-005. Goal remains active.

## Delivered

- Weighted 169-class preflop opponent ranges (`OpponentRange`): per-combo weights, canonical JSON,
  content hash, 1,326 materialized combos, and a combo-uniform `Uniform` control.
- Range-conditioned N-opponent sampler (1–5 ranges): collision-free weighted combo sampling, runout
  without replacement, 1/k tie split, `RangeModelId` provenance.
- `EquityThresholdPolicy`: Call only when the whole pot-share interval is above break-even, Fold only
  when it is below, Uncertain at a straddle or boundary.

## Verified

| Check | Observed |
|---|---|
| KhKd vs one opponent, 20,000 samples | uniform pot share **0.8245**; AA-only **0.182375**; delta **0.642125** |
| Blocked range | hero AhAd + board AcAs2h -> `InvalidOperationException: range-exhausted` |
| Confidence gate | above -> Call, below -> Fold, straddle/boundary -> Uncertain; invalid threshold refused |
| Equity namespace | 12 tests before this round, 6 range tests, 4 threshold tests — all pass |
| Full suite | **517/517**, 0 failed |
| Build | 0 errors |

## Still open

- **EQ-005**: contribution/side-pot-aware call EV is not implemented.
- **EQ-004 inference provenance**: ranges are accepted as inputs; inferring them from public
  position/action history is not yet wired to the runtime agent.
- Anchor 3 (equal-budget non-LLM learning ablation) remains untouched; no proxy claim.

## Reproduction

```
dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release
dotnet run --project .tmp/tholdem-verify/RunXunit -c Release -- \
  WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll Tests.Equity
dotnet run --project .tmp/tholdem-verify/RunXunit -c Release -- \
  WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
```
