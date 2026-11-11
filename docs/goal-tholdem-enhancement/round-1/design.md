# Round 1 design contract — four-seat evaluation foundation

**Objective:** add the versioned four-seat game contract, per-hand outcome access, protected seed
domains and a pre-registered evaluation plan, test-first, with the legacy 446-test suite unchanged
and green.

**Namespace:** `Wip.Tholdem.Agents.Evaluation` (new folder `WIP/Wip.Tholdem.Agents/Evaluation/`).
**Project:** `WIP/Wip.Tholdem.Agents/Wip.Tholdem.Agents.csproj` (references `Wip.Tholdem`; already has
`HandOutcome`, `SimulationOptions`, `CanonicalJsonWriter`).

## 1. `Simulation/MatchSimulation.cs` (new) + `MatchSimulator` (edit)

```csharp
namespace Wip.Tholdem.Agents.Simulation;

public sealed record MatchSimulation(
    SimulationReport Report,
    IReadOnlyList<HandOutcome> Outcomes,
    IReadOnlyList<ulong> HandSeeds);
```

Add to `MatchSimulator`:

```csharp
public static MatchSimulation RunDetailed(
    IReadOnlyList<IPokerAgent> agents,
    SimulationOptions options,
    CancellationToken cancellationToken = default)
```

Move the existing body of `Run(IReadOnlyList<IPokerAgent>, ...)` into `RunDetailed`; return the
`SimulationReport`, the accumulated `outcomes`, and the per-hand `handSeed` values actually used
(one per hand, in order). `HandSeeds` is what lets a paired evaluator prove both policies saw the
same deals. `Run(IReadOnlyList<...>)` becomes
`=> RunDetailed(agents, options, cancellationToken).Report;`. The two-seat `Run` overload is
unchanged. **The report/manifest/hand bytes must not change**: same loops, same order, same
`BuildManifest`/`BuildReport`.

## 2. `SimulationOptions` (edit)

Append one optional constructor parameter and property:

```csharp
int actionCapPerHand = 1_000_000
...
public int ActionCapPerHand { get; init; }
```

Validate `actionCapPerHand >= 1` (`ArgumentOutOfRangeException`). In `MatchSimulator`, count every
applied action in a hand; when the count would exceed the cap, throw
`HandActionCapExceededException(int handNumber, int cap)` (new file `Simulation/HandActionCapExceededException.cs`,
`sealed class ... : InvalidOperationException`) **before the next agent is asked**. Default cap keeps
every existing caller byte-identical.

## 3. `Evaluation/GameConfiguration.cs` (new)

```csharp
namespace Wip.Tholdem.Agents.Evaluation;

public enum SimulationMode { CashIndependentHands = 0, TournamentPersistentStacks = 1 }

public sealed class GameConfigurationException : ArgumentException
{
    public GameConfigurationException(string code, string message) : base(message) => Code = code;
    public string Code { get; }
}

public sealed record GameConfiguration
{
    public const string Schema = "tholdem.game.v1";
    public const string CashMetricId = "tholdem.cash.bb100.v1";
    public const string ButtonRulePerHand = "tholdem.button.per-hand-one-seat.v1";
    public const string SidePotRuleLayered = "tholdem.sidepot.layered.v1";
    public const int DefaultActionCapPerHand = 1000;

    public int Seats { get; init; } = 4;                 // 2..6
    public long SmallBlind { get; init; } = 5;
    public long BigBlind { get; init; } = 10;
    public long StartingStack { get; init; } = 1000;     // 100 bb default
    public IReadOnlyList<long>? StartingStacks { get; init; }
    public long Ante { get; init; }
    public string ButtonRule { get; init; } = ButtonRulePerHand;
    public int ActionCapPerHand { get; init; } = DefaultActionCapPerHand;
    public string SidePotRule { get; init; } = SidePotRuleLayered;
    public SimulationMode Mode { get; init; } = SimulationMode.CashIndependentHands;

    public string MetricId => CashMetricId;              // tournament refuses before this is used
    public void Validate();
    public GameConfiguration Normalized();
    public SimulationOptions ToSimulationOptions(ulong seed, int hands, string? historyDirectory = null);
    public string ToCanonicalJson();
    public string ConfigHash { get; }
    public static GameConfiguration FourSeatCash(long startingStack = 1000, long smallBlind = 5, long bigBlind = 10);
}
```

Refusals (all `GameConfigurationException`, exact codes): `seats-out-of-range`,
`blind-not-positive`, `small-blind-above-big-blind`, `stack-below-big-blind`,
`starting-stacks-count`, `ante-not-supported` (ante must be 0; the v1 engine has none),
`button-rule-unsupported`, `side-pot-rule-unsupported`, `action-cap-invalid`,
`mode-tournament-not-implemented` (the simulator resets stacks every hand, so a tournament config is
refused rather than silently treated as cash). `Normalized()` validates and returns a copy whose
`StartingStacks` is a defensive array (or null). `ToSimulationOptions` maps every field, including
`ActionCapPerHand`.

Canonical JSON, fixed key order:
`{"schema":"tholdem.game.v1","seats":4,"smallBlind":5,"bigBlind":10,"startingStack":1000,"startingStacks":null,"ante":0,"buttonRule":"tholdem.button.per-hand-one-seat.v1","actionCapPerHand":1000,"sidePotRule":"tholdem.sidepot.layered.v1","mode":"CashIndependentHands","metricId":"tholdem.cash.bb100.v1"}`
Use `CanonicalJsonWriter` (internal, same assembly). `ConfigHash = StrategyArtifact.ComputeHash(ToCanonicalJson())`.

## 4. `Evaluation/SeedDomain.cs` (new)

```csharp
public enum SeedPurpose { Training = 0, Selection = 1, Holdout = 2, Audit = 3 }

public sealed class SeedDomainOverlapException : InvalidOperationException
{
    public SeedDomainOverlapException(string leftId, string rightId, ulong seed);
    public string LeftDomainId { get; }
    public string RightDomainId { get; }
    public ulong Seed { get; }
}

public sealed record SeedDomain
{
    public SeedDomain(string domainId, SeedPurpose purpose, ulong baseSeed, int count); // count >= 1
    public string DomainId { get; }
    public SeedPurpose Purpose { get; }
    public ulong BaseSeed { get; }
    public int Count { get; }
    public IReadOnlyList<ulong> Seeds { get; }
    public string ToCanonicalJson();
    public string DomainHash { get; }
    public static void ValidateDisjoint(IEnumerable<SeedDomain> domains);
}
```

Each seed is `SeedDerivation.Derive(baseSeed, PurposeSalt(purpose), index + 1, 0, 0)` with salts
Training=1, Selection=2, Holdout=3, Audit=4. Materialize once in the constructor, expose as a
defensive `IReadOnlyList<ulong>` (array). Reject empty `domainId`, `count < 1`.
`ValidateDisjoint` intersects concrete seeds across all domains and throws on the first overlap,
with the two domain ids and the shared seed. Canonical JSON:
`{"schema":"tholdem.seed-domain.v1","domainId":"...","purpose":"Holdout","baseSeed":123,"count":20,"seeds":[...]}`
`DomainHash = StrategyArtifact.ComputeHash(ToCanonicalJson())`.

## 5. `Evaluation/EvaluationPlanV2.cs` (new)

```csharp
public sealed record EvaluationPlanV2
{
    public const string Schema = "tholdem.evaluation-plan.v2";
    public const string EvaluatorVersion = "tholdem.population-evaluator.v2.0.0";

    public GameConfiguration Game { get; }
    public string OpponentPoolId { get; }
    public IReadOnlyList<string> Compositions { get; }
    public IReadOnlyList<SeedDomain> Domains { get; }
    public int HandsPerSeatMatch { get; }
    public double MinimumPracticalEffectBbPer100 { get; }
    public double ConfidenceLevel { get; }        // (0,1), default 0.95
    public int PlannedCandidates { get; }         // >= 1
    public double AnchorRegressionToleranceBbPer100 { get; }

    public SeedDomain Training { get; }
    public SeedDomain Selection { get; }
    public SeedDomain Holdout { get; }
    public SeedDomain Audit { get; }
    public int SeedBlocks => Holdout.Count;
    public double Alpha => 1.0 - ConfidenceLevel;
    public string PlanHash { get; }
    public string ToCanonicalJson();

    public static EvaluationPlanV2 Create(
        GameConfiguration game,
        string opponentPoolId,
        IReadOnlyList<string> compositions,
        IReadOnlyList<SeedDomain> domains,
        int handsPerSeatMatch,
        double minimumPracticalEffectBbPer100,
        double confidenceLevel,
        int plannedCandidates,
        double anchorRegressionToleranceBbPer100);
}
```

Validation: exactly one domain per purpose (missing/duplicate → `InvalidOperationException`);
`SeedDomain.ValidateDisjoint`; `game.Validate()`; non-empty pool id and distinct non-empty
composition ids; `handsPerSeatMatch >= 1`; margin >= 0; confidence in (0,1); candidates >= 1;
anchor tolerance >= 0. Canonical JSON embeds the game's canonical object **verbatim** (not a hash
stand-in), each domain's canonical object, and every setting; fixed order:
`{"schema":"tholdem.evaluation-plan.v2","evaluatorVersion":"...","game":{...},"opponentPoolId":"...","compositions":[...],"domains":[...],"handsPerSeatMatch":20,"minimumPracticalEffectBbPer100":2,"confidenceLevel":0.95,"plannedCandidates":1,"anchorRegressionToleranceBbPer100":2}`
`PlanHash = StrategyArtifact.ComputeHash(ToCanonicalJson())`. No clock, no GUID, no culture.

## 6. Tests (must fail first, then pass)

`WIP/Wip.Tholdem.Tests/Evaluation/GameConfigurationTests.cs`
1. `FourSeatCash_GivenDefaults_DeclaresEverySetting` — seats 4, 1000/5/10, metric id, all JSON keys, hash stable across two equal instances.
2. `Ante_GivenPositive_RefusedAsUnsupported` — code `ante-not-supported`.
3. `TournamentMode_GivenPersistentStacks_RefusedAsNotImplemented` — code `mode-tournament-not-implemented`.
4. `StartingStacks_GivenWrongCount_Refused` — 3 stacks for 4 seats.
5. `SmallBlind_GivenAboveBigBlind_Refused`.
6. `ToSimulationOptions_GivenConfig_MapsEveryField` — seats/blinds/stacks/cap.
7. `ConfigHash_GivenDifferentSeats_Changes`.

`WIP/Wip.Tholdem.Tests/Evaluation/DetailedSimulationTests.cs`
1. `RunDetailed_GivenTwoAgents_ReportMatchesLegacyRun` — same options, `Run` vs `RunDetailed.Report` canonical JSON equal.
2. `RunDetailed_GivenFourAgents_ReturnsPerHandOutcomes` — 5 hands, 4 seats, 5 outcomes, each `FinalStacks.Count == 4`, chip sum per hand equals 4000.
3. `RunDetailed_GivenSameSeed_IsByteIdentical` — twice, same JSON and same `HandSeeds` sequence.
4. `Run_GivenActionCapExceeded_Throws` — cap 2, 1 hand, tight-aggressive agents; `HandActionCapExceededException` with HandNumber 1 and Cap 2.

`WIP/Wip.Tholdem.Tests/Evaluation/SeedDomainTests.cs`
1. `Seeds_GivenCount_AreDeterministicDistinctAndImmutable` (mutating the returned array must not change a second read; return a copy).
2. `Seeds_GivenDifferentPurpose_Differ`.
3. `ValidateDisjoint_GivenIdenticalDomains_ThrowsWithSharedSeed`.
4. `ValidateDisjoint_GivenSameBaseDifferentPurposes_DoesNotThrow`.
5. `DomainHash_GivenSameSeeds_IsStable`.

`WIP/Wip.Tholdem.Tests/Evaluation/EvaluationPlanV2Tests.cs`
1. `Create_GivenValidDomains_DerivesSeedBlocksFromHoldout`.
2. `Create_GivenOverlappingDomains_Throws`.
3. `Create_GivenMissingAuditDomain_Throws`.
4. `Create_GivenEmptyCompositions_Throws`.
5. `PlanHash_GivenSameInputs_Stable_AndChangesWithMargin`.
6. `ToCanonicalJson_GivenPlan_EmbedsGameObjectAndAllDomains`.

## 7. Forbidden

- Do not modify `HoldemHandEngine`, `HandRecord`, `PlayerObservation`, `MatchEvaluator`, `TrainingLoop`, `PromotionPolicy`, `PromotionVerifier`, the host, the web project, or v1 report types.
- Do not change `MatchSimulator`'s manifest/report/session-id construction or ordering.
- No clock, GUID, culture-dependent formatting, or ambient randomness.
- No weakening of existing tests; no skip.
