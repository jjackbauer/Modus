# Round 1 — versioned game contract (implementer)

Scope: the versioned `GameConfiguration` contract in `Wip.Tholdem.Agents.Evaluation`, its exact
refusal codes and canonical JSON, plus the seven `GameConfigurationTests`. No other production file
was edited; `CanonicalJsonWriter`, `SimulationOptions`, `MatchSimulator` and every existing file are
untouched.

## Red (observed before implementation)

The tests were written first, against a production stub whose members threw
`NotImplementedException` and whose constants/properties existed only so the tests would compile.

```powershell
dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release
```

Observed: `Build succeeded.` with `0 Error(s)` (14 pre-existing warnings).

```powershell
dotnet run --project .tmp/tholdem-verify/RunXunit -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll Tests.Evaluation.GameConfigurationTests
```

```text
FAIL GameConfigurationTests.Ante_GivenPositive_RefusedAsUnsupported: ThrowsException: Assert.Throws() Failure: Exception type was not an exact match
Expected: typeof(Wip.Tholdem.Agents.Evaluation.GameConfigurationException)
Actual:   typeof(System.NotImplementedException)
FAIL GameConfigurationTests.ConfigHash_GivenDifferentSeats_Changes: NotImplementedException: stub: ConfigHash
FAIL GameConfigurationTests.FourSeatCash_GivenDefaults_DeclaresEverySetting: NotImplementedException: stub: FourSeatCash
FAIL GameConfigurationTests.SmallBlind_GivenAboveBigBlind_Refused: ThrowsException: Assert.Throws() Failure: Exception type was not an exact match
Expected: typeof(Wip.Tholdem.Agents.Evaluation.GameConfigurationException)
Actual:   typeof(System.NotImplementedException)
FAIL GameConfigurationTests.StartingStacks_GivenWrongCount_Refused: ThrowsException: Assert.Throws() Failure: Exception type was not an exact match
Expected: typeof(Wip.Tholdem.Agents.Evaluation.GameConfigurationException)
Actual:   typeof(System.NotImplementedException)
FAIL GameConfigurationTests.ToSimulationOptions_GivenConfig_MapsEveryField: NotImplementedException: stub: ToSimulationOptions
FAIL GameConfigurationTests.TournamentMode_GivenPersistentStacks_RefusedAsNotImplemented: ThrowsException: Assert.Throws() Failure: Exception type was not an exact match
Expected: typeof(Wip.Tholdem.Agents.Evaluation.GameConfigurationException)
Actual:   typeof(System.NotImplementedException)
TOTAL 7 PASSED 0 FAILED 7 SKIPPED 0
```

## Green (observed after implementation)

Build:

```powershell
dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release
```

```text
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

Filtered tests:

```powershell
dotnet run --project .tmp/tholdem-verify/RunXunit -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll Tests.Evaluation.GameConfigurationTests
```

```text
TOTAL 7 PASSED 7 FAILED 0 SKIPPED 0
```

Full suite:

```powershell
dotnet run --project .tmp/tholdem-verify/RunXunit -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
```

```text
TOTAL 457 PASSED 457 FAILED 0 SKIPPED 0
```

457 is the pre-existing 450 tests plus the seven new facts; the 450 already includes the four
`DetailedSimulationTests` facts from the simulator slice. No existing test was changed, skipped or
weakened.

## Tests added

`WIP/Wip.Tholdem.Tests/Evaluation/GameConfigurationTests.cs`:

1. `FourSeatCash_GivenDefaults_DeclaresEverySetting` — 4 seats, 1000/5/10, cash metric id, the exact
   canonical JSON (which pins every key and the key order), hash stable across two equal instances and
   equal to `StrategyArtifact.ComputeHash` of that JSON.
2. `Ante_GivenPositive_RefusedAsUnsupported` — `ante-not-supported`.
3. `TournamentMode_GivenPersistentStacks_RefusedAsNotImplemented` —
   `mode-tournament-not-implemented`.
4. `StartingStacks_GivenWrongCount_Refused` — 3 stacks for 4 seats → `starting-stacks-count`.
5. `SmallBlind_GivenAboveBigBlind_Refused` — `small-blind-above-big-blind`.
6. `ToSimulationOptions_GivenConfig_MapsEveryField` — seed, hands, seats (stack-count), blinds,
   starting stack, per-seat stacks, action cap and history directory.
7. `ConfigHash_GivenDifferentSeats_Changes` — a 4-seat and a 3-seat hash differ and both are 64 hex
   characters.

## Implementation notes

- `Validate()` refuses in the design's order with the exact codes: `seats-out-of-range`,
  `blind-not-positive`, `small-blind-above-big-blind`, `stack-below-big-blind`,
  `starting-stacks-count`, `ante-not-supported`, `button-rule-unsupported`,
  `side-pot-rule-unsupported`, `action-cap-invalid`, `mode-tournament-not-implemented`.
  A supplied per-seat stack below the big blind also refuses with `stack-below-big-blind`.
- `Normalized()` validates and returns `this with { StartingStacks = StartingStacks?.ToArray() }`,
  so the per-seat list is a defensive array (or null) and record equality/immutability are preserved.
- `ToCanonicalJson()` uses `CanonicalJsonWriter` and the fixed key order
  `schema, seats, smallBlind, bigBlind, startingStack, startingStacks, ante, buttonRule,
  actionCapPerHand, sidePotRule, mode, metricId`; `startingStacks` is `null` or a compact array.
  The default serializes to the design's literal byte for byte.
- `ConfigHash => StrategyArtifact.ComputeHash(ToCanonicalJson())`, so equal configurations hash
  equally and a seat-count change changes the hash. There is no clock, GUID, culture or ambient
  randomness.
- `ToSimulationOptions(seed, hands, historyDirectory)` validates first, then maps seed, hands,
  starting stack, blinds, per-seat stacks, `AlternateButton: true` (the per-hand one-seat button
  rule), history directory and `ActionCapPerHand`.
- `FourSeatCash` builds the four-seat cash default and validates it.

## No stub remains

`GameConfiguration.cs` contains no `NotImplementedException` and no throw-only member; every
declared member is implemented. Confirmed by searching the file after the green run.

## Files changed

| File | Change |
|---|---|
| `WIP/Wip.Tholdem.Agents/Evaluation/GameConfiguration.cs` | new contract, exception and mode |
| `WIP/Wip.Tholdem.Tests/Evaluation/GameConfigurationTests.cs` | seven new facts |
| `docs/goal-tholdem-enhancement/round-1/implementation-game-config.md` | this record |

No other file was edited.
