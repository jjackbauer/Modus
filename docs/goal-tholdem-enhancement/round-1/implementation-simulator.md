# Round 1 — detailed simulation foundation (implementer)

Scope: `MatchSimulator.RunDetailed`, `MatchSimulation`, the optional per-hand action cap and the
four `DetailedSimulationTests`. Existing two-seat bytes are unchanged; the two-seat overload still
delegates to the seat-aware `Run`.

## Red (observed before implementation)

Command:

```powershell
dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release
dotnet run --project .tmp/tholdem-verify/RunXunit -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll Tests.Evaluation.DetailedSimulationTests
```

The first build failed with `CS1739` because the cap test names the new `actionCapPerHand`
constructor parameter, so the parameter, its validation and its property were added before the red
run (the cap itself was not enforced yet). The observed red output then was:

```text
FAIL DetailedSimulationTests.RunDetailed_GivenFourAgents_ReturnsPerHandOutcomes: NotImplementedException: The method or operation is not implemented.
FAIL DetailedSimulationTests.RunDetailed_GivenSameSeed_IsByteIdentical: NotImplementedException: The method or operation is not implemented.
FAIL DetailedSimulationTests.RunDetailed_GivenTwoAgents_ReportMatchesLegacyRun: NotImplementedException: The method or operation is not implemented.
FAIL DetailedSimulationTests.Run_GivenActionCapExceeded_Throws: ThrowsException: Assert.Throws() Failure: No exception was thrown
Expected: typeof(Wip.Tholdem.Agents.Simulation.HandActionCapExceededException)
TOTAL 4 PASSED 0 FAILED 4 SKIPPED 0
```

At that point `Run` still held its old body and `RunDetailed` was a `NotImplementedException` stub,
so the three `RunDetailed` facts failed on the stub and the cap fact ran to completion without an
exception.

## Green (observed after implementation)

Build:

```text
dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release
Build succeeded.
    14 Warning(s)
    0 Error(s)
```

Filtered tests:

```text
dotnet run --project .tmp/tholdem-verify/RunXunit -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll Tests.Evaluation.DetailedSimulationTests
TOTAL 4 PASSED 4 FAILED 0 SKIPPED 0
```

Full suite:

```text
dotnet run --project .tmp/tholdem-verify/RunXunit -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
TOTAL 450 PASSED 450 FAILED 0 SKIPPED 0
```

450 is the pre-existing 446 tests plus the four new facts. The existing oracle facts
(`Run_GivenTwoAgentsSeed42_ExpectedUnchangedSessionIdManifestHashAndHandBytes` and the
reproducibility oracles) still pass, so the two-seat session id, manifest hash and hand file bytes did
not change.

## Implementation notes

- `RunDetailed` now contains the body previously in `Run(IReadOnlyList<...>)`; `Run` is
  `=> RunDetailed(...).Report`. The two-seat overload still delegates to the list overload.
- `handSeeds` collects the `SeedDerivation.Derive(options.Seed, hand, 0, 0, 0)` value used by each
  hand, in hand order, and is returned with the accumulated outcomes after the report is built.
- `SimulationOptions` appends `int actionCapPerHand = 1_000_000` as the last optional parameter,
  rejects values below one with `ArgumentOutOfRangeException`, and exposes `ActionCapPerHand`.
- Every successfully applied action increments the hand''s action counter. When the counter exceeds
  the cap, the simulator throws `HandActionCapExceededException(hand, cap)` before the next agent is
  asked. The default cap cannot be reached by a normal hand, leaving existing callers unchanged.
- The cap fact uses seed 2. With seeds 12345 and 1 this heads-up tight-aggressive hand finished
  within two applied actions, so the cap was not exceeded and the fact correctly failed; seed 2
  applies a third action and throws. The contract does not pin the cap-fact seed.
- No `NotImplementedException` remains anywhere under `Simulation/`.

## Files changed

| File | Change |
|---|---|
| `WIP/Wip.Tholdem.Agents/Simulation/MatchSimulation.cs` | new detailed result record |
| `WIP/Wip.Tholdem.Agents/Simulation/HandActionCapExceededException.cs` | new exception with `HandNumber` and `Cap` |
| `WIP/Wip.Tholdem.Agents/Simulation/MatchSimulator.cs` | body moved to `RunDetailed`, `Run` delegates, cap enforced |
| `WIP/Wip.Tholdem.Agents/Simulation/SimulationOptions.cs` | `actionCapPerHand` parameter, validation, property |
| `WIP/Wip.Tholdem.Tests/Evaluation/DetailedSimulationTests.cs` | four new facts |
| `docs/goal-tholdem-enhancement/round-1/implementation-simulator.md` | this record |

No other file was edited.
