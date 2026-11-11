# Round 11 implementation — measured VPIP with explicit missingness (OBS-005, DIA-005)

Status: complete. The simulator measures voluntary preflop participation per seat, the analyzer
carries `null` plus the denominator instead of a fabricated zero, and the model prompt serializes
`vpip`, `vpipHands` and `vpipVoluntary`. Five new tests pass; the full suite is 549/549. The
canonical report JSON and `AgentMetrics` are unchanged.

## Files

| File | Change |
| --- | --- |
| `WIP/Wip.Tholdem.Agents/Simulation/HandOutcome.cs` | Additive: `VoluntaryPreflop` init property, default empty. The positional constructor is unchanged. |
| `WIP/Wip.Tholdem.Agents/Simulation/SimulationReport.cs` | Additive: `VpipSummary` record and `SimulationReport.Vpip` init property, default empty. |
| `WIP/Wip.Tholdem.Agents/Simulation/MatchSimulator.cs` | Records one voluntary-preflop flag per seat per hand and fills one `VpipSummary` per seat. Canonical report writer untouched. |
| `WIP/Wip.Tholdem.Agents/Learning/SessionAnalysis.cs` | `Vpip` is `double?`; adds `VpipHands`/`VpipVoluntaryHands`; the analyzer copies the seat-zero summary or returns null/0/0. |
| `WIP/Wip.Tholdem.ModelHarness/LlmPromptBuilder.cs` | Writes `vpip` as `null` or a number, then `vpipHands` and `vpipVoluntary`. |
| `WIP/Wip.Tholdem.Tests/Learning/VpipObservationTests.cs` | New: the five design tests and two deterministic test-only agents. |
| `WIP/Wip.Tholdem.Tests/Learning/StrategyAnalyzerTests.cs` | Authorized design section 4 update: only the VPIP assertion/disposition. |
| `WIP/Wip.Tholdem.Tests/ModelHarness/LlmPromptBuilderTests.cs` | Recorded exception: only the analysis member-count assertion (6 -> 8). |
| `docs/goal-tholdem-enhancement/round-11/implementation-vpip.md` | New: this evidence record. |

`VpipSummary` lives in `SimulationReport.cs` because that file is inside the round's scope and the
new report type belongs to the same simulation contract.

## Contract as implemented

```csharp
public sealed record VpipSummary(int Hands, int VoluntaryPreflopHands)
{
    public double? Vpip => Hands == 0 ? null : VoluntaryPreflopHands / (double)Hands;
}
```

`HandOutcome` gains `public IReadOnlyList<bool> VoluntaryPreflop { get; init; } =
Array.Empty<bool>();`. `SimulationReport` gains `public IReadOnlyList<VpipSummary> Vpip { get;
init; } = Array.Empty<VpipSummary>();`.

For every applied action, `MatchSimulator` sets seat `s`'s flag when the pre-action street is
`Preflop` and the action kind is `Call`, `Bet`, `Raise` or `AllIn`. Checks and posted blinds are
never counted. The hand outcome carries the array; after the batch, each seat's summary is
`new VpipSummary(options.Hands, count-of-true-flags)`. Because `options.Hands` is at least one, a
simulator-produced summary is never 0/0.

`SessionAnalysis.Vpip` is `double?`; `VpipHands` and `VpipVoluntaryHands` are init properties
defaulting to 0. `StrategyAnalyzer.Analyze` copies `report.Vpip[0]` when present; otherwise it
returns `Vpip = null`, `VpipHands = 0`, `VpipVoluntaryHands = 0`. No path writes `0.0` for
missing data.

`LlmPromptBuilder` writes `"vpip":null` for missing data and `"vpip":0` for a measured zero, then
`"vpipHands":<n>` and `"vpipVoluntary":<n>`. Existing numeric members keep their order and format.

## Prompt snippets observed

The exact `analysis` object text, observed with a temporary print inside test 5 (removed before the
final build):

```json
{"hands":10,"winRate":0.5,"bbPer100":0,"aggressionFactor":1,"showdownRate":0.2,"vpip":null,"vpipHands":0,"vpipVoluntary":0}
{"hands":10,"winRate":0.5,"bbPer100":0,"aggressionFactor":1,"showdownRate":0.2,"vpip":0,"vpipHands":10,"vpipVoluntary":0}
```

The first line is the missing case (`vpip` null, denominator 0); the second is the measured 0/10
case (`vpip` 0, `vpipHands` 10, `vpipVoluntary` 0). The permanent test parses the same JSON and
asserts the same values; the print is not part of the delivered test.

## Test-first: observed red output

The API types and properties were added first with default values, the simulator still produced no
summaries, the analyzer returned null only when no summary existed, and the prompt wrote `null` but
not yet the denominators. The new test assembly built with 0 errors and the filtered run exited 1:

```text
FAIL VpipObservationTests.LlmPromptBuilder_GivenNullAndMeasuredZero_WritesNullAndZeroWithDenominators: KeyNotFoundException: The given key was not present in the dictionary.
FAIL VpipObservationTests.RunDetailed_GivenCallingSeat0_ReportsPositiveVpip: EqualException: Assert.Equal() Failure: Values differ
Expected: 2
Actual:   0
FAIL VpipObservationTests.RunDetailed_GivenFoldEveryHandSeat0_ReportsMeasuredZeroVpip: EqualException: Assert.Equal() Failure: Values differ
Expected: 2
Actual:   0
FAIL VpipObservationTests.StrategyAnalyzer_GivenMeasuredReport_CopiesVpipAndDenominator: ArgumentOutOfRangeException: Index was out of range. Must be non-negative and less than the size of the collection. (Parameter 'index')
TOTAL 5 PASSED 1 FAILED 4 SKIPPED 0
```

Four of the five tests drove the missing behaviour: no per-seat summaries, no denominator copies,
no prompt denominator members. Test 4 (`StrategyAnalyzer_GivenReportWithoutVpip_ReturnsNull`) was
already green in this run because the nullable `SessionAnalysis` API and the no-summary -> null
branch were part of the compiling stub. It is the missingness guard (it pins the absence of a
fabricated zero); it is not evidence that the simulator measured anything. Tests 1, 2, 3 and 5 are
the driving red tests.

## Observed green

```text
dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release
Build succeeded. 0 Warning(s) 0 Error(s)

dotnet run --project .tmp/tholdem-verify/RunXunit -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll Tests.Learning.VpipObservationTests
TOTAL 5 PASSED 5 FAILED 0 SKIPPED 0
```

The brief's project path `WIP/Wip.Tholdem.Tests/Wip.Wip.Tholdem.Tests.csproj` does not exist in
this checkout; the commands above use the real path, `Wip.Tholdem.Tests.csproj`.

## Measured VPIP values

Both scenarios run seed `20261110`, 10 hands, alternating button, with the test-only deterministic
fold/check and call/check agents.

| Seat-0 policy | Hands | Voluntary preflop hands | VPIP |
| --- | --- | --- | --- |
| Fold/check only | 10 | 0 | 0.0 (0/10) |
| Call/check | 10 | 5 | 0.5 (5/10) |

The 5/10 value is observed, not inferred: with the alternating button seat 0 posts the small blind
and calls in five hands and checks as the big blind in the other five. The temporary print used to
observe both rows was removed; the permanent test asserts denominator 10, voluntary 0, VPIP 0.0 for
the folder and a positive VPIP in (0, 1] with a 10-hand denominator for the caller.

## Full suite: observed 549 total, 549 passed, 0 failed

```text
dotnet run --project .tmp/tholdem-verify/RunXunit -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
TOTAL 549 PASSED 549 FAILED 0 SKIPPED 0
```

549 is exactly the recorded 544-test baseline plus the five new tests. The baseline was re-run
before the change and observed at `TOTAL 544 PASSED 544 FAILED 0 SKIPPED 0`.

## Canonical report JSON and AgentMetrics unchanged

`MatchSimulator.BuildReport` and `AppendMetric` were not touched, and `Vpip` is deliberately not
serialized into `reportJson`. `AgentMetrics.cs` was not touched. The full suite still passes every
reproducibility, canonical-JSON, manifest-hash, history and replay test, including the tests that
pin report bytes from the simulator.

## Existing tests updated

| Test file | Change | Reason |
| --- | --- | --- |
| `WIP/Wip.Tholdem.Tests/Learning/StrategyAnalyzerTests.cs` | Replaced `Assert.Equal(0.0, analysis.Vpip)` with equality against `report.Vpip[0].Vpip`, `report.Vpip[0].Hands` and `report.Vpip[0].VoluntaryPreflopHands`. | Design section 4, OBS-005: the old assertion pinned the fabricated zero for a report that now measures seat-0 VPIP. This is the authorized correction, not a weakening. |
| `WIP/Wip.Tholdem.Tests/ModelHarness/LlmPromptBuilderTests.cs` | One line: `Assert.Equal(6, analysis.EnumerateObject().Count())` -> `Assert.Equal(8, ...)`. | Recorded exception explicitly authorized by the delegating agent: the design adds exactly `vpipHands` and `vpipVoluntary` to the `analysis` object, so the exact member count necessarily grows from 6 to 8. Every other assertion is intact; member names are not enumerated by this test. |

No other existing test was edited.