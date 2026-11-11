# Round 5 design — typed equity contract and N-opponent expected pot share

**Objective:** make equity a probability estimate rather than a hand-category score, and compute
expected pot share against the exact number of live opponents (1–5) without replacement, wiring the
runtime equity tool additively so the existing heads-up output stays byte-identical.

**Milestone:** C (D3 / EQ-001, EQ-002, EQ-003, EQ-008 core; EQ-005 and range conditioning are later).

## 1. `Equity/EquityEstimate.cs` (new)

```csharp
public sealed record EquityEstimate(
    double WinProbability,
    double TieProbability,
    double ExpectedPotShare,
    int Samples,
    int OpponentCount,
    string Method,
    int Wins,
    int Ties,
    int Losses,
    double StandardError,
    ConfidenceInterval ExpectedPotShareInterval);
```

`ConfidenceInterval` is `Wip.Tholdem.Agents.Simulation.ConfidenceInterval`. This type is the only
input an equity-based decision may accept; `HandCategoryScore` is not convertible to it.

## 2. `Equity/HandCategoryScore.cs` (new)

```csharp
public readonly record struct HandCategoryScore(double Score)
{
    public static HandCategoryScore FromHeuristic(
        IReadOnlyList<Card> hole, IReadOnlyList<Card> board);
}
```

`FromHeuristic` wraps the existing `HandStrength.Estimate` (internal, same assembly). No implicit
conversion to `double` or `EquityEstimate`; the XML doc says it is a relative rank score, never
showdown equity.

## 3. `Equity/MultiwayEquity.cs` (new)

```csharp
public static class MultiwayEquity
{
    public const string Method = "monte-carlo-uniform-1-to-5-v2";
    public static EquityEstimate Estimate(
        IReadOnlyList<Card> ownHoleCards,
        IReadOnlyList<Card> board,
        int opponents,      // 1..5
        ulong seed,
        int samples);
}
```

Validate: exactly 2 hole cards; board 0/3/4/5; opponents 1..5; samples >= 1; no duplicate physical
card across hero, board or opponents (opponents are sampled, never supplied). Build the remaining
deck in canonical suit/rank order.

Per sample: partial Fisher–Yates over the remaining deck for `2 * opponents + (5 - board.Count)`
draws (no replacement across hero, board, every opponent hand and the runout). Evaluate hero and each
opponent on the complete board. Let `k` be the number of players tied for best. Hero's pot share is
`1/k` when hero is among the best, else 0. Track wins (sole best), ties (best but tied with at least
one opponent), losses, and the per-sample share.

Return: `WinProbability = wins/samples`, `TieProbability = ties/samples`,
`ExpectedPotShare = sum(share)/samples`, sample variance of the share,
`StandardError = sd/sqrt(samples)`, `ExpectedPotShareInterval = mean ± 1.96*SE` clamped to [0,1].
No clock, culture or ambient randomness; same inputs produce the same estimate.

## 4. `Runtime/EquityTool.cs` (edit, additive only)

- `EquityToolRequest(HoleCards, Board, Samples = 2000, Seed = null, Opponents = 1)` — append
  `Opponents` **after** `Seed` so existing positional callers still compile.
- `EquityToolResult(Equity, Samples, Wins, Ties, Losses, ExpectedPotShare = 0, Opponents = 1)` —
  append two optional members at the end.
- `ExecuteAsync`: if `Opponents <= 1`, call the legacy `MonteCarloEquity.Estimate` and return
  exactly the old fields plus `ExpectedPotShare = Equity`, `Opponents = 1`; otherwise call
  `MultiwayEquity.Estimate` and map `ExpectedPotShare`, counts and opponent count. `Opponents > 5`
  throws `ArgumentOutOfRangeException`.
- `Runtime/TholdemToolCatalog.cs`: add an optional `opponents` integer property (minimum 1) to the
  equity schema and update its description to "one to five random opponent hands".

The four-tool catalog, names, required fields and permission descriptors do not change.

## 5. Tests

`WIP/Wip.Tholdem.Tests/Equity/MultiwayEquityTests.cs`
1. `Estimate_GivenRiverBoardTwoOpponents_MatchesExhaustivePotShare` — 20,000 samples vs exhaustive
   C(45,4)=148,995 enumeration of both opponent hands and exact 1/k shares; assert
   `ExpectedPotShare` within 0.01 of the exhaustive value; counts sum to samples.
2. `Estimate_GivenBoardRoyalFlush_AllTieAndSplitEqually` — board `As Ks Qs Js Ts`, hero `2c 3d`,
   opponents 3: `WinProbability == 0`, `TieProbability == 1`, `ExpectedPotShare == 0.25`,
   `Ties == Samples`, `Wins == 0`.
3. `Estimate_GivenSameSeed_IsDeterministic` — two calls, equal records.
4. `Estimate_GivenFiveOpponents_ReturnsCountsAndValidInterval` — counts sum, interval contains the
   pot share, share in [0,1].
5. `Estimate_GivenInvalidInputs_Throws` — 0/6 opponents, duplicate hero/board cards, bad board
   sizes, zero samples.

`WIP/Wip.Tholdem.Tests/Equity/EquityToolOpponentsTests.cs`
1. `EquityTool_GivenDefaultOpponents_MatchesLegacyEstimate` — seed 7, 20,000 samples, `As Ah`;
   compare Equity/Samples/Wins/Ties/Losses exactly with `MonteCarloEquity.Estimate`;
   `ExpectedPotShare == Equity`, `Opponents == 1`.
2. `EquityTool_GivenThreeOpponents_ReturnsPotShareAndCounts` — river board; `Opponents == 3`,
   counts sum to samples, pot share in [0,1].
3. `EquityTool_GivenSixOpponents_Throws` — `ArgumentOutOfRangeException`.
4. `EquityTool_GivenCatalog_DeclaresOptionalOpponentsInteger` — schema has `opponents` with type
   `integer` and it is not in `required`.

`WIP/Wip.Tholdem.Tests/Equity/HandCategoryScoreTests.cs`
1. `FromHeuristic_GivenAcesPreflop_ReturnsUnitIntervalScore` — in [0,1] and > 0.7.
2. `FromHeuristic_GivenFullHouse_ReturnsHighScore` — in [0,1] and > 0.8.
3. `HandCategoryScore_GivenType_HasNoEquityConversion` — reflection: no implicit/explicit conversion
   to `EquityEstimate` or `double`.

## 6. Forbidden

Do not change `MonteCarloEquity` or `EquityResult` (their golden tests pin exact counts). Do not
change `HandStrength`, `StrategyAgent`, the four-tool count/names/required fields, or any existing
test. No clock/GUID/culture/ambient randomness. No test weakening.
