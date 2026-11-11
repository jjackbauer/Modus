# Round 1 — protected seed domains and evaluation plan (implementer)

Scope: `SeedDomain` and `EvaluationPlanV2` in `Wip.Tholdem.Agents.Evaluation`, with the five
`SeedDomainTests` and six `EvaluationPlanV2Tests`. No existing production file, test, simulation
file or game contract was edited.

## Red (observed before implementation)

The two test files were written first against production stubs whose constructors, properties and
factory threw `NotImplementedException`, so the tests compiled and failed for the right reason.
Build:

```powershell
dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release
```

Observed: `Build succeeded.` with `0 Error(s)` (14 pre-existing warnings).

```powershell
dotnet run --project .tmp/tholdem-verify/RunXunit -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll Tests.Evaluation.SeedDomainTests
```

```text
FAIL SeedDomainTests.DomainHash_GivenSameSeeds_IsStable: NotImplementedException: stub: SeedDomain
FAIL SeedDomainTests.Seeds_GivenCount_AreDeterministicDistinctAndImmutable: NotImplementedException: stub: SeedDomain
FAIL SeedDomainTests.Seeds_GivenDifferentPurpose_Differ: NotImplementedException: stub: SeedDomain
FAIL SeedDomainTests.ValidateDisjoint_GivenIdenticalDomains_ThrowsWithSharedSeed: NotImplementedException: stub: SeedDomain
FAIL SeedDomainTests.ValidateDisjoint_GivenSameBaseDifferentPurposes_DoesNotThrow: NotImplementedException: stub: SeedDomain
TOTAL 5 PASSED 0 FAILED 5 SKIPPED 0
```

```powershell
dotnet run --project .tmp/tholdem-verify/RunXunit -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll Tests.Evaluation.EvaluationPlanV2Tests
```

```text
FAIL EvaluationPlanV2Tests.Create_GivenDuplicatePurpose_Throws: NotImplementedException: stub: SeedDomain
FAIL EvaluationPlanV2Tests.Create_GivenEmptyCompositions_Throws: ThrowsException: Assert.Throws() Failure: Exception type was not an exact match
Expected: typeof(System.ArgumentException)
Actual:   typeof(System.NotImplementedException)
FAIL EvaluationPlanV2Tests.Create_GivenMissingAuditDomain_Throws: NotImplementedException: stub: SeedDomain
FAIL EvaluationPlanV2Tests.Create_GivenValidDomains_DerivesSeedBlocksFromHoldout: NotImplementedException: stub: SeedDomain
FAIL EvaluationPlanV2Tests.PlanHash_GivenSameInputs_Stable_AndChangesWithMargin: NotImplementedException: stub: SeedDomain
FAIL EvaluationPlanV2Tests.ToCanonicalJson_GivenPlan_EmbedsGameObjectAndAllDomains: NotImplementedException: stub: SeedDomain
TOTAL 6 PASSED 0 FAILED 6 SKIPPED 0
```

## Green (observed after implementation)

Build:

```text
Build succeeded.
    14 Warning(s)
    0 Error(s)
```

Filtered tests:

```text
TOTAL 5 PASSED 5 FAILED 0 SKIPPED 0
TOTAL 6 PASSED 6 FAILED 0 SKIPPED 0
```

Full suite:

```powershell
dotnet run --project .tmp/tholdem-verify/RunXunit -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
```

```text
TOTAL 468 PASSED 468 FAILED 0 SKIPPED 0
```

468 is the pre-existing 457 tests plus the eleven new facts. No existing test was changed, skipped or
weakened.

## Tests added

`WIP/Wip.Tholdem.Tests/Evaluation/SeedDomainTests.cs`:

1. `Seeds_GivenCount_AreDeterministicDistinctAndImmutable` — equal parameters produce equal
   sequences twice, all five values are distinct, every read returns a fresh array, and mutating the
   returned array neither changes a second read nor the first value.
2. `Seeds_GivenDifferentPurpose_Differ` — the same domain id/base seed/count under Training and
   Holdout produce different sequences.
3. `ValidateDisjoint_GivenIdenticalDomains_ThrowsWithSharedSeed` — two identical domains throw
   `SeedDomainOverlapException`; `Seed` equals `left.Seeds[0]` and both domain ids are reported.
4. `ValidateDisjoint_GivenSameBaseDifferentPurposes_DoesNotThrow` — the four purpose salts yield 24
   distinct seeds for one base seed.
5. `DomainHash_GivenSameSeeds_IsStable` — pins the exact canonical JSON (base seed 123, Holdout,
   two seeds `1302179516557818905` and `9576118770318801856`), equal domains hash equally, the
   hash is 64 characters, equals `StrategyArtifact.ComputeHash` of the JSON, and changes with the
   base seed.

`WIP/Wip.Tholdem.Tests/Evaluation/EvaluationPlanV2Tests.cs`:

1. `Create_GivenValidDomains_DerivesSeedBlocksFromHoldout` — 12 holdout vs 4 others gives
   `SeedBlocks == 12`; `Domains` is in the fixed order Training, Selection, Holdout, Audit even when
   supplied scrambled.
2. `Create_GivenDuplicatePurpose_Throws` — a second Holdout with all four purposes present throws
   `InvalidOperationException`.
3. `Create_GivenMissingAuditDomain_Throws` — no Audit domain throws `InvalidOperationException`.
4. `Create_GivenEmptyCompositions_Throws` — an empty composition list throws `ArgumentException`.
5. `PlanHash_GivenSameInputs_Stable_AndChangesWithMargin` — equal inputs hash equally, the hash is
   64 characters and equals `StrategyArtifact.ComputeHash` of the canonical JSON; margin 5 vs 6
   changes the hash.
6. `ToCanonicalJson_GivenPlan_EmbedsGameObjectAndAllDomains` — the JSON contains
   `"game":" + game.ToCanonicalJson()` verbatim, each domain's canonical object, all four domain ids,
   `"schema":"tholdem.evaluation-plan.v2"`, the evaluator version and every plan setting, with the
   game before the domains.

## Implementation notes

- `SeedDomain` rejects an empty/whitespace `domainId` (`ArgumentException`) and `count < 1`
  (`ArgumentOutOfRangeException`). It materializes exactly `count` seeds at construction:
  `SeedDerivation.Derive(baseSeed, PurposeSalt(purpose), index + 1, 0, 0)`, with salts Training=1,
  Selection=2, Holdout=3, Audit=4 (an unknown purpose is refused). `Seeds` returns
  `(ulong[])seeds.Clone()`, so a caller cannot mutate the domain.
- `SeedDomain.ValidateDisjoint` walks domains in declaration order and, for each new domain, its
  seeds in index order against every earlier domain, throwing on the first concrete overlap with
  `SeedDomainOverlapException(leftId, rightId, seed)`.
- `SeedDomain.ToCanonicalJson` uses `CanonicalJsonWriter` and the fixed order
  `schema, domainId, purpose, baseSeed, count, seeds`; `DomainHash` is
  `StrategyArtifact.ComputeHash(ToCanonicalJson())`.
- `EvaluationPlanV2.Create` first refuses a missing or duplicate purpose with
  `InvalidOperationException` (a `Dictionary<SeedPurpose, SeedDomain>` `TryAdd`), then calls
  `SeedDomain.ValidateDisjoint`, `game.Validate()`, and validates non-empty/distinct composition ids,
  non-empty pool id, hands >= 1, finite margin >= 0, finite confidence in (0,1), candidates >= 1 and
  finite anchor tolerance >= 0. Argument-shaped refusals are `ArgumentException`; an invalid game
  surfaces its `GameConfigurationException`.
- The plan stores a defensive composition array and `game.Normalized()`, and orders `Domains` as
  Training, Selection, Holdout, Audit. `SeedBlocks => Holdout.Count` and `Alpha => 1 - ConfidenceLevel`.
- `ToCanonicalJson` embeds `game.ToCanonicalJson()` and each `domain.ToCanonicalJson()` verbatim,
  in the design's fixed key order; `PlanHash = StrategyArtifact.ComputeHash(ToCanonicalJson())`.
  `CanonicalJsonWriter` formats doubles with `"R"` and invariant culture, so there is no clock, GUID,
  culture or ambient randomness in either identity.
- The pinned seed literals in the tests are the design's derivation evaluated independently, so a
  wrong salt or index would fail `DomainHash_GivenSameSeeds_IsStable` rather than pass by comparing
  the implementation with itself.

## No stub remains

After the green run, searching `WIP/Wip.Tholdem.Agents/Evaluation` for
`NotImplementedException` and `stub:` returned no matches. Every declared member of both types is
implemented; no throw-only member remains.

## Files changed

| File | Change |
|---|---|
| `WIP/Wip.Tholdem.Agents/Evaluation/SeedDomain.cs` | new purpose enum, overlap exception and content-addressed seed domain |
| `WIP/Wip.Tholdem.Agents/Evaluation/EvaluationPlanV2.cs` | new validated, content-addressed evaluation plan |
| `WIP/Wip.Tholdem.Tests/Evaluation/SeedDomainTests.cs` | five new facts |
| `WIP/Wip.Tholdem.Tests/Evaluation/EvaluationPlanV2Tests.cs` | six new facts |
| `docs/goal-tholdem-enhancement/round-1/implementation-seed-plan.md` | this record |

No other file was edited.
