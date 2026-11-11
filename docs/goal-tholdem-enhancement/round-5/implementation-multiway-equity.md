# Round 5 implementation — multiway equity (`EquityEstimate`, `MultiwayEquity`)

Status: complete. Scope: design sections 1 and 3, plus the five tests in section 5 items 1–5. No
existing file was modified and no existing test was changed.

## Files

| File | Change |
| --- | --- |
| `WIP/Wip.Tholdem.Agents/Equity/EquityEstimate.cs` | New: the immutable equity estimate contract record. |
| `WIP/Wip.Tholdem.Agents/Equity/MultiwayEquity.cs` | New: validation, seeded N-opponent sampling, tie splitting and the normal confidence interval. |
| `WIP/Wip.Tholdem.Tests/Equity/MultiwayEquityTests.cs` | New: the five tests. |

## Test-first: observed red output

`EquityEstimate` was defined with the contracted signature and `MultiwayEquity.Estimate` was
stubbed with `throw new NotImplementedException(...)` so the five tests compiled. The stubbed build
succeeded and the class run produced:

```text
FAIL MultiwayEquityTests.Estimate_GivenBoardRoyalFlush_AllTieAndSplitEqually: NotImplementedException: MultiwayEquity.Estimate is not implemented yet.
FAIL MultiwayEquityTests.Estimate_GivenFiveOpponents_ReturnsCountsAndValidInterval: NotImplementedException: MultiwayEquity.Estimate is not implemented yet.
FAIL MultiwayEquityTests.Estimate_GivenInvalidInputs_Throws: ThrowsException: Assert.Throws() Failure: Exception type was not an exact match
Expected: typeof(System.ArgumentNullException)
Actual:   typeof(System.NotImplementedException)
FAIL MultiwayEquityTests.Estimate_GivenRiverBoardTwoOpponents_MatchesExhaustivePotShare: NotImplementedException: MultiwayEquity.Estimate is not implemented yet.
FAIL MultiwayEquityTests.Estimate_GivenSameSeed_IsDeterministic: NotImplementedException: MultiwayEquity.Estimate is not implemented yet.
TOTAL 5 PASSED 0 FAILED 5 SKIPPED 0
```

Command:

```text
dotnet run --project .tmp/tholdem-verify/RunXunit -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll Tests.Equity.MultiwayEquityTests
```

The supplementary runner matches type names by substring, so this filter selects
`Wip.Tholdem.Tests.Equity.MultiwayEquityTests`. The red output above was re-observed against the
final test file by temporarily restoring the stub and force-rebuilding; the implementation was then
restored and the build and green runs repeated.

## Implementation notes

- Validation order: null arguments, exactly two hole cards, board size 0/3/4/5, opponents 1–5,
  samples >= 1, then duplicate cards (within the hole cards, then between the hole cards and the
  board). Count violations throw `ArgumentOutOfRangeException`; shape and duplicate violations throw
  `ArgumentException`.
- The dead set is removed from the canonical deck built in suit order 0..3 and rank order 2..14.
- Per sample the deck is restored and partially shuffled for
  `draws = 2 * opponents + (5 - board.Count)` draws using the internal
  `Xorshift64Star`: `swap = draw + (int)(Next() % (deck.Length - draw))`. A card cannot be drawn
  twice, so no physical card appears twice in any hero/opponent/runout sample.
- Opponent hole cards are `deck[0..2*opponents)`; the runout is the following
  `5 - board.Count` cards. Every player is evaluated by `HandEvaluator` on the complete seven-card
  hand (hole cards + fixed board + runout).
- `k` is the number of players tied for the best rank; hero's pot share is `1/k` when hero is among
  the best and `0` otherwise. A win is the sole best hand, a tie is a shared best hand, a loss is any
  sample where hero is not best; wins + ties + losses == samples.
- `WinProbability` and `TieProbability` are wins/samples and ties/samples; `ExpectedPotShare` is the
  mean share. The sample variance uses the n−1 denominator (zero when `samples == 1`),
  `StandardError = sd / sqrt(samples)`, and the interval is the mean ± 1.96·SE clamped to [0, 1].
- `Method` is the constant `monte-carlo-uniform-1-to-5-v2`. No clock, GUID, culture or ambient
  randomness is used; `Estimate_GivenSameSeed_IsDeterministic` asserts record equality across calls.

## Exhaustive oracle and observed values

Test 1 enumerates the C(45,4) = 148,995 four-card sets for both opponent hands. A uniform deal is an
ordered pair of opponent hands, so each four-card set has three partitions — (ij|kl), (ik|jl),
(il|jk) — that are averaged; that is 446,985 share evaluations. A single-pairing oracle would be
biased because the pairing determines which hole cards share an opponent hand and therefore changes
the 1/k shares. Both counts are asserted in the test.

Values observed with a temporary out-of-tree console harness referencing the built assembly:

| Quantity | Observed |
| --- | --- |
| Exhaustive four-card sets / share evaluations | 148,995 / 446,985 |
| Exhaustive expected pot share | 0.7961475217289171 |
| Sampled share (seed 12345, 20,000 samples) | 0.79785 |
| Absolute difference (tolerance 0.02) | 0.0017024782710829 |
| Sampled win/tie/loss counts | 15,940 / 34 / 4,026 |
| Sampled standard error | 0.002836092566552141 |
| Sampled interval | [0.7922912585695577, 0.8034087414304422] |
| Royal-flush board, 3 opponents: win/tie/share | 0 / 1 / 0.25 (Ties = 500, SE = 0, interval [0.25, 0.25]) |
| Five opponents (seed 7, 10,000): share | 0.5609, counts 5,595 / 28 / 4,377, SE 0.00495596359525731, interval [0.5511863113532957, 0.5706136886467043] |

The test asserts the river sample is within 0.02 of the exhaustive value; the observed difference is
0.0017.

## Observed green output

Required build:

```text
dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

A forced rebuild (`-t:Rebuild`) reported 16 warnings, all pre-existing in Learning/Training tests;
searching that build output for the three new file names found no warning lines.

New test class:

```text
dotnet run --project .tmp/tholdem-verify/RunXunit -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll Tests.Equity.MultiwayEquityTests
TOTAL 5 PASSED 5 FAILED 0 SKIPPED 0
```

Full suite:

```text
dotnet run --project .tmp/tholdem-verify/RunXunit -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
TOTAL 500 PASSED 500 FAILED 0 SKIPPED 0
```

500 = the 495 pre-existing tests plus the 5 new tests. Both suite commands exited 0.

## No stub remains

```text
Select-String -Path WIP/Wip.Tholdem.Agents/Equity/EquityEstimate.cs,
  WIP/Wip.Tholdem.Agents/Equity/MultiwayEquity.cs,
  WIP/Wip.Tholdem.Tests/Equity/MultiwayEquityTests.cs -Pattern NotImplementedException
```

Observed: no matches. All three new files are UTF-8 without BOM and contain no null bytes.
`git status --porcelain` for the scoped paths lists only the three new files and this document; no
existing file was modified.
