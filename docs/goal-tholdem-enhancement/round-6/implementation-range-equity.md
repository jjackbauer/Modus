# Round 6 implementation — weighted opponent ranges and range-conditioned equity (EQ-004)

Status: complete. Scope: design sections 1–3. Only the four scoped code files were written or edited,
plus this document. No existing test was changed and the uniform sampler's distribution is unchanged.

## Files

| File | Change |
| --- | --- |
| `WIP/Wip.Tholdem.Agents/Equity/OpponentRange.cs` | New: the 169 canonical preflop classes, per-combo weights, canonical JSON, content hash, combo materialization and validation. |
| `WIP/Wip.Tholdem.Agents/Equity/MultiwayEquity.cs` | Additive: `RangeMethod`, the range-conditioned `Estimate` overload, and the explicit `"uniform"` model id on the existing overload. |
| `WIP/Wip.Tholdem.Agents/Equity/EquityEstimate.cs` | Additive: init-only `RangeModelId` property, default `"uniform"`. |
| `WIP/Wip.Tholdem.Tests/Equity/OpponentRangeTests.cs` | New: the six contracted tests. |
| `docs/goal-tholdem-enhancement/round-6/implementation-range-equity.md` | New: this evidence record. |

## Contract as implemented

- **169 classes, canonical order:** pairs `AA`..`22`, then suited `AKs`..`32s`, then offsuit
  `AKo`..`32o`, each block descending by high card then low card. Every legal two-card hand
  classifies to exactly one class; pair/suited/offsuit combo counts are 6/4/12.
- **Weights are per combo.** `Uniform` sets all 169 classes to weight 1.0, so every one of the
  13·6 + 78·4 + 78·12 = 1,326 legal combos carries weight 1.0: the control is combo-uniform.
- **`Parse`** rejects an empty/whitespace id, unknown class names, non-finite or negative weights,
  and an all-zero range with `ArgumentException`. Only positive classes are stored, in canonical
  order. Omitted classes have weight 0.
- **Canonical JSON** has fixed key order and only positive classes, for example
  `{"schema":"tholdem.opponent-range.v1","rangeId":"aces-only","classes":[{"class":"AA","weight":1}]}`.
  Numbers use the invariant `R` format via the internal `CanonicalJsonWriter`.
  `RangeHash = StrategyArtifact.ComputeHash(ToCanonicalJson())`.
- **Range estimator:** 1–5 ranges (one per opponent); null entries, an empty list and six ranges are
  refused. Per sample, each range in order contributes its combos that do not collide with the
  hero, the board or already-sampled opponent cards, picked with probability proportional to combo
  weight. A range with no available combo throws
  `InvalidOperationException("range-exhausted")`. After all opponents are sampled the runout is
  drawn without replacement; players are evaluated on seven cards and a `k`-way tie splits the pot
  `1/k`.
- **`RangeModelId`:** the uniform overload reports `"uniform"`; the range overload reports
  `"range:" + first16(StrategyArtifact.ComputeHash(string.Join("\n", range hashes)))`. The range
  `Method` is `monte-carlo-range-1-to-5-v2`.
- No clock, culture, GUID or ambient randomness is used. The only randomness is the seeded internal
  `Xorshift64Star`; the unit draw is `(Next() >> 11) * 2^-53`.

### Test 5 correction

The dispatch note ("`Ah Ad` alone exhausts the `AA` range") is wrong: `Ac` and `As` still form a
legal `AA` combo. The test blocks all four aces with hero `Ah Ad` and board `Ac As 2h`, which is
the only way the `AA` range has zero available combos at sample 0. The exception type and the
`range-exhausted` token are unchanged.

## Test-first: observed red output

The six tests were written first. `OpponentRange` members and the range `Estimate` overload were
stubbed with `throw new NotImplementedException()` so the test assembly compiled. The stub build
succeeded (`0 Error(s)`) and the new class produced:

```text
FAIL OpponentRangeTests.Estimate_GivenAcesOnlyOpponent_KingKingEquityFalls: NotImplementedException: The method or operation is not implemented.
FAIL OpponentRangeTests.Estimate_GivenInvalidRangeList_Throws: ThrowsAnyException: Assert.ThrowsAny() Failure: Exception type was not compatible
Expected: typeof(System.ArgumentException)
Actual:   typeof(System.NotImplementedException)
FAIL OpponentRangeTests.Estimate_GivenRangeExhaustedByBoard_Throws: NotImplementedException: The method or operation is not implemented.
FAIL OpponentRangeTests.Parse_GivenAcesOnly_WeightsOnlyAces: NotImplementedException: The method or operation is not implemented.
FAIL OpponentRangeTests.Parse_GivenUnknownClassOrNegativeWeight_Throws: ThrowsException: Assert.Throws() Failure: Exception type was not an exact match
Expected: typeof(System.ArgumentException)
Actual:   typeof(System.NotImplementedException)
FAIL OpponentRangeTests.Uniform_GivenDefault_Has169ClassesAndStableHash: NotImplementedException: The method or operation is not implemented.
TOTAL 6 PASSED 0 FAILED 6 SKIPPED 0
```

The test file was not changed after this red run; the implementation replaced every stub.

## Implementation notes

- **Dead-card representation:** a `bool[52]` in deterministic suit-major order
  (`suit * 13 + rank - 2`). Hero and board are marked before sampling; each sample copies that base
  mask and adds the two cards of each picked opponent combo. Collision filtering is therefore a
  two-array-lookup test per combo.
- **Weighted pick:** the available combo weights are summed, a threshold is drawn in
  `[0, total)`, and the pick is the first combo whose cumulative weight exceeds the threshold. A
  last-eligible-combo fallback covers the floating-point tail; a second zero-weight guard also
  throws `range-exhausted`.
- **Runout:** after the opponents are fixed, the pool is rebuilt from the 52-card order excluding
  every dead card, then partially Fisher–Yates shuffled for `5 - board.Count` draws. No card can
  repeat, and the fixed board cards are never redrawn.
- **Input validation order** (range overload): null arguments, hole-card count 2, board size
  0/3/4/5, range count 1–5, sample count >= 1, null range entries, then duplicate hero/board cards.
  Count violations throw `ArgumentOutOfRangeException`; shape, duplicate and null-entry violations
  throw `ArgumentException`.
- **Statistics:** identical definitions to the uniform estimator — win is sole best, tie is shared
  best, loss is not best; share is `1/k`; standard deviation uses the n−1 denominator (zero for
  one sample); the interval is the mean ± 1.96·SE clamped to [0, 1]. Record equality includes
  `RangeModelId`, so the repeated-call assertion covers the model label too.

## Observed AA-only vs uniform pot shares

An out-of-tree .NET 10 file-based app referenced the built
`WIP/Wip.Tholdem.Agents/Wip.Tholdem.Agents.csproj`; it is not part of the repository. Hero
`Kh Kd`, empty board, one opponent, seed `20240601`, 20,000 samples:

```text
uniform=0.8245 win=0.8218 tie=0.0054
aa-only=0.182375 win=0.18 tie=0.00475
delta=0.6421250000000001
uniformRangeModelId=uniform
rangeModelId=range:db361314bb6ba002
uniformHash=0bafae21765440803779ec9406fdbdd131d9211493fb80dd359d87569741ed49
```

| Quantity | Uniform control | AA-only opponent | Difference |
| --- | --- | --- | --- |
| Expected pot share | 0.8245 | 0.182375 | 0.642125 |
| Win probability | 0.8218 | 0.18 | — |
| Tie probability | 0.0054 | 0.00475 | — |
| RangeModelId | `uniform` | `range:db361314bb6ba002` | — |

The test's directional contract is `AA-only < uniform − 0.30`; observed margin is 0.642125, and the
AA-only share sits near the theoretical `KK` vs `AA` equity of about 0.18. The uniform range's
content address is `0bafae21…41ed49` (64 lowercase hex characters).

## Observed green output

Required build:

```text
dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

A forced rebuild (`-t:Rebuild`) reported 0 errors; filtering its warning lines for
`OpponentRange`, `MultiwayEquity` and `EquityEstimate` returned no lines. The warnings it reports
are pre-existing in unrelated Learning/Training test files.

New test class:

```text
dotnet run --project .tmp/tholdem-verify/RunXunit -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll Tests.Equity.OpponentRangeTests
TOTAL 6 PASSED 6 FAILED 0 SKIPPED 0
```

Full suite:

```text
dotnet run --project .tmp/tholdem-verify/RunXunit -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
TOTAL 513 PASSED 513 FAILED 0 SKIPPED 0
```

The baseline observed before any change was `TOTAL 507 PASSED 507 FAILED 0`; 513 = 507 + the six
new tests. All three commands exited 0.

## No stub remains

```text
Select-String -Path WIP/Wip.Tholdem.Agents/Equity/OpponentRange.cs,
  WIP/Wip.Tholdem.Agents/Equity/MultiwayEquity.cs -Pattern NotImplementedException
```

Observed: no matches (also no `TODO` or `FIXME` in `WIP/Wip.Tholdem.Agents/Equity`). All four
scoped C# files are UTF-8 without BOM, contain no null bytes, and the full suite is green.

## Commands used

```text
dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release
dotnet run --project .tmp/tholdem-verify/RunXunit -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll Tests.Equity.OpponentRangeTests
dotnet run --project .tmp/tholdem-verify/RunXunit -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
```
