# Round 11 design — measured VPIP with explicit missingness (OBS-005, DIA-005)

**Objective:** stop reporting `SessionAnalysis.Vpip = 0` when VPIP was never measured. Measure
voluntary preflop participation from the simulator, carry the denominator, and serialize `null` plus
the sample counts into the model prompt. 0/20 is a measured zero; 0/0 is missing.

## 1. Simulator (`Simulation/HandOutcome.cs`, `SimulationReport.cs`, `MatchSimulator.cs`)

- `HandOutcome` gains an init property
  `IReadOnlyList<bool> VoluntaryPreflop { get; init; } = Array.Empty<bool>();` — one flag per seat:
  did the seat voluntarily put chips in preflop (Call, Bet, Raise or AllIn while the street is
  Preflop)? Checks and posted blinds do not count.
- `MatchSimulator` records the flags per hand and attaches them to the `HandOutcome`.
- New `public sealed record VpipSummary(int Hands, int VoluntaryPreflopHands)` with
  `public double? Vpip => Hands == 0 ? null : VoluntaryPreflopHands / (double)Hands;`.
- `SimulationReport` gains `IReadOnlyList<VpipSummary> Vpip { get; init; } = Array.Empty<VpipSummary>();`
  and `MatchSimulator` fills one summary per seat. The canonical report JSON and every existing
  constructor call are unchanged.

## 2. Learning (`Learning/SessionAnalysis.cs`)

- `SessionAnalysis.Vpip` becomes `double?`; add init properties `int VpipHands` and
  `int VpipVoluntaryHands` (default 0).
- `StrategyAnalyzer.Analyze`: when `report.Vpip` has a seat-0 summary, copy its nullable Vpip and its
  counts; when the report carries no summary (e.g. a hand-built report), use `null`, 0 and 0. It must
  never fabricate 0 for missing data.

## 3. Prompt (`ModelHarness/LlmPromptBuilder.cs`)

In the `analysis` object write `"vpip":null` when missing, otherwise the number; add
`"vpipHands":<n>` and `"vpipVoluntary":<n>` after it. Existing numeric values keep their order and
format.

## 4. Tests

`WIP/Wip.Tholdem.Tests/Learning/VpipObservationTests.cs` (five):
1. `RunDetailed_GivenFoldEveryHandSeat0_ReportsMeasuredZeroVpip`: a fold/check-only agent at seat 0,
   10 hands -> `Vpip[0].Hands == 10`, `VoluntaryPreflopHands == 0`, `Vpip == 0.0`.
2. `RunDetailed_GivenCallingSeat0_ReportsPositiveVpip`: a call/check agent at seat 0 ->
   `Vpip > 0`, `<= 1`, denominator 10.
3. `StrategyAnalyzer_GivenMeasuredReport_CopiesVpipAndDenominator`: the report from (1) ->
   `analysis.Vpip == 0.0`, `VpipHands == 10`, `VpipVoluntaryHands == 0`.
4. `StrategyAnalyzer_GivenReportWithoutVpip_ReturnsNull`: a hand-built `SimulationReport` with no
   Vpip summary -> `analysis.Vpip is null`, `VpipHands == 0` (not zero).
5. `LlmPromptBuilder_GivenNullAndMeasuredZero_WritesNullAndZeroWithDenominators`: parse the prompt
   JSON; missing -> `vpip` null, `vpipHands` 0; measured 0/10 -> `vpip` 0, `vpipHands` 10,
   `vpipVoluntary` 0.

Update the existing `StrategyAnalyzerTests.Analyze_GivenSimulationReport_...` assertions from
`Assert.Equal(0.0, analysis.Vpip)` to the measured value copied from `report.Vpip[0]` plus the
denominator. This is the OBS-005 correction, not a weakening: the old assertion pinned the lie.
Any other existing test that assumes Vpip 0 for a simulator report may only be updated the same way
and must be listed in the implementation document.

## 5. Forbidden

Do not change the canonical report/manifest JSON, `AgentMetrics`, the evaluator, the gate, or the
engine. No clock/culture/GUID/ambient randomness. No fabricated zero for missing data.
