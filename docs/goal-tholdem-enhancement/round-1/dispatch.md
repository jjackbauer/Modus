# Round 1 dispatch — four-seat evaluation foundation

**Mode:** Probe-free Implementer (1 agent). **Objective:** SIM-001..005 contract foundation.

## Envelope

GOAL: A scientifically defensible four-player Hold'em benchmark that preserves the 2–6-seat engine.

OBJECTIVE: Implement the versioned four-seat evaluation contract — `GameConfiguration`,
`MatchSimulator.RunDetailed` with per-hand outcomes and hand seeds, `SeedDomain`, and
`EvaluationPlanV2` — test-first, with all new tests passing and the legacy suite still green.

SCOPE (only these files):
- `WIP/Wip.Tholdem.Agents/Simulation/MatchSimulation.cs` (new)
- `WIP/Wip.Tholdem.Agents/Simulation/HandActionCapExceededException.cs` (new)
- `WIP/Wip.Tholdem.Agents/Simulation/MatchSimulator.cs` (edit)
- `WIP/Wip.Tholdem.Agents/Simulation/SimulationOptions.cs` (edit)
- `WIP/Wip.Tholdem.Agents/Evaluation/GameConfiguration.cs` (new)
- `WIP/Wip.Tholdem.Agents/Evaluation/SeedDomain.cs` (new)
- `WIP/Wip.Tholdem.Agents/Evaluation/EvaluationPlanV2.cs` (new)
- `WIP/Wip.Tholdem.Tests/Evaluation/GameConfigurationTests.cs` (new)
- `WIP/Wip.Tholdem.Tests/Evaluation/DetailedSimulationTests.cs` (new)
- `WIP/Wip.Tholdem.Tests/Evaluation/SeedDomainTests.cs` (new)
- `WIP/Wip.Tholdem.Tests/Evaluation/EvaluationPlanV2Tests.cs` (new)
- `docs/goal-tholdem-enhancement/round-1/implementation.md` (new)

OUT OF SCOPE: every other file. Do not touch the engine, history, v1 evaluator, training loop, host,
web project, solution files, or generated stubs.

DELIVERABLE: the files above, plus `implementation.md` containing:
1. the exact test-first command and its OBSERVED failing output (compile failure is acceptable as red
   only if it names the missing member; otherwise stub the types and observe runtime failures);
2. the exact build command and its OBSERVED output;
3. the exact `RunXunit` command and its OBSERVED `TOTAL ... FAILED 0` line for the new tests;
4. the full-suite `RunXunit` command and its OBSERVED `TOTAL ... FAILED 0` line, with the count;
5. a list of every public member implemented and confirmation no `NotImplementedException` remains.

EVIDENCE: every claim cites `file:line` or a command and its observed output. No citation, no claim.

FORBIDDEN: speculation presented as fact; edits outside SCOPE; reporting a command you did not run;
reporting success you did not observe; weakening/skipping/deleting a test; changing
`MatchSimulator`'s manifest/report/session-id construction; using `dotnet test` (the VSTest
testhost is broken on this machine); changing the existing 446-test behaviour; adding a clock, GUID,
culture-dependent formatting or ambient randomness.

RETURN: <=200 words — the test-first failure observed, the final pass observed, the commands run,
anything not passing, the artifact path.
