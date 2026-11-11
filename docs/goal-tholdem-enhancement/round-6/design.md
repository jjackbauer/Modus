# Round 6 design — range-conditioned opponent sampling (EQ-004)

**Objective:** condition the N-opponent sampler on weighted preflop ranges derived from public
information, keep the uniform sampler as a labelled control, and prove the direction changes with a
narrow vs broad range. Side-pot call EV (EQ-005) and confidence-gated decisions (EQ-006) are the next
slices.

## 1. `Equity/OpponentRange.cs` (new)

```csharp
public sealed class OpponentRange
{
    public const string Schema = "tholdem.opponent-range.v1";
    public static OpponentRange Uniform { get; }           // all 169 classes at weight 1
    public string RangeId { get; }
    public IReadOnlyDictionary<string, double> Weights { get; }  // only weight > 0
    public string ToCanonicalJson();
    public string RangeHash { get; }
    public double WeightFor(Card first, Card second);
    public static OpponentRange Parse(string rangeId, IReadOnlyDictionary<string, double> weights);
    internal IReadOnlyList<(Card First, Card Second, double Weight)> Combos { get; }
}
```

- 169 preflop classes: pairs `AA`..`22`, suited `AKs`..`32s`, offsuit `AKo`..`32o`. Every legal
  two-card hand maps to exactly one class.
- Weights are **per combo**. `Uniform` sets every class to 1.0, which combined with the combo counts
  (6 pair, 4 suited, 12 offsuit) is combo-uniform.
- `Parse` validates: non-empty id; known class names; finite weight >= 0; at least one positive
  weight. Unknown class or negative/NaN weight -> `ArgumentException`.
- Canonical JSON: fixed key order, classes in canonical index order, only positive weights:
  `{"schema":"tholdem.opponent-range.v1","rangeId":"...","classes":[{"class":"AA","weight":1},...]}`.
  `RangeHash = StrategyArtifact.ComputeHash(ToCanonicalJson())`.
- `Combos` materializes every two-card combination whose class weight is positive, with that weight.

## 2. `Equity/MultiwayEquity.cs` (edit, additive)

Add:

```csharp
public const string RangeMethod = "monte-carlo-range-1-to-5-v2";

public static EquityEstimate Estimate(
    IReadOnlyList<Card> ownHoleCards,
    IReadOnlyList<Card> board,
    IReadOnlyList<OpponentRange> opponentRanges,
    ulong seed,
    int samples);
```

- 1–5 ranges, one per opponent; null entries refused.
- Per sample: start from hero + board as dead cards; for each opponent in order, collect its combos
  that do not collide with the dead set, throw `InvalidOperationException("range-exhausted")` if none,
  choose one weighted by combo weight, add its two cards to dead; then draw the board runout from the
  remaining deck without replacement; evaluate all players and split the pot `1/k` among ties.
- Same win/tie/share/SE/interval fields as the uniform estimator.
- `RangeModelId` (an `init` property added to `EquityEstimate`, default `"uniform"`) is set to
  `"range:" + first16(StrategyArtifact.ComputeHash(string.Join("\n", ranges.Select(r => r.RangeHash))))`.
- The existing uniform overload keeps its behaviour and sets `RangeModelId = "uniform"`.
- No clock, culture, GUID or ambient randomness.

## 3. Tests — `WIP/Wip.Tholdem.Tests/Equity/OpponentRangeTests.cs`

1. `Uniform_GivenDefault_Has169ClassesAndStableHash` — 169 positive weights, hash 64 hex, stable.
2. `Parse_GivenAcesOnly_WeightsOnlyAces` — `WeightFor(Ac, Ad) == 1`, `WeightFor(Kc, Kd) == 0`,
   canonical JSON contains exactly one class.
3. `Parse_GivenUnknownClassOrNegativeWeight_Throws`.
4. `Estimate_GivenAcesOnlyOpponent_KingKingEquityFalls` — hero `Kh Kd`, empty board, 1 opponent,
   20,000 samples: AA-only expected pot share < uniform expected pot share by at least 0.30; both in
   [0,1]; deterministic records for a repeated call.
5. `Estimate_GivenRangeExhaustedByBoard_Throws` — hero `Ah Ad`, opponent `AA` range -> no combo
   remains -> `InvalidOperationException` whose message contains `range-exhausted`.
6. `Estimate_GivenInvalidRangeList_Throws` — null entry, empty list, six ranges.

## 4. Forbidden

Do not change `MonteCarloEquity`, the legacy uniform sampling path's distribution, the equity tool,
`HandCategoryScore`, or any existing test. No test weakening. No clock/culture/ambient randomness.
