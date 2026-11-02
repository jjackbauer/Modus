# Round 2 implementation — unbounded refusal and split gate arena

Workspace: `C:\Users\ricar\dev\Modus`. No API key was used or printed. No existing assertion was weakened, widened, deleted, or skipped.

## A. Host refuses an effective zero cap without an explicit opt-in

### Test-first failure (before the change)

Test added first, compiling only against the pre-existing `BonesHostApplication.Build`:
`WIP/Wip.Bones.Host.Tests/BonesHostUnboundedStartupTests.cs` —
`Build_GivenEffectiveZeroMaxGamesPerRunWithoutOptIn_ExpectedStartupRefusal`.

```powershell
dotnet test WIP/Wip.Bones.Host.Tests/Wip.Bones.Host.Tests.csproj --filter FullyQualifiedName~BonesHostUnboundedStartupTests --nologo -v minimal
```

Observed (exit code 1):

```text
  Failed Wip.Bones.Host.Tests.BonesHostUnboundedStartupTests.Build_GivenEffectiveZeroMaxGamesPerRunWithoutOptIn_ExpectedStartupRefusal [81 ms]
  Error Message:
   Assert.IsType() Failure: Value is null
Expected: typeof(System.InvalidOperationException)
Actual:   null
  Stack Trace:
     at Wip.Bones.Host.Tests.BonesHostUnboundedStartupTests.Build_GivenEffectiveZeroMaxGamesPerRunWithoutOptIn_ExpectedStartupRefusal() in C:\Users\ricar\dev\Modus\WIP\Wip.Bones.Host.Tests\BonesHostUnboundedStartupTests.cs:line 41
Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1, Duration: 81 ms
EXIT_CODE=1
```

The pre-change launcher also accepted an effective-zero spelling. Command and observed output (exit code 0; no refusal):

```powershell
$env:BONES_MAX_GAMES_PER_RUN='00'; Remove-Item Env:BONES_ALLOW_UNBOUNDED -ErrorAction SilentlyContinue; & .\bones.ps1 help
```

```text
Bones Learning Loop
  resume       Resume from library
  cold         Cold start - re-ponder
  stub         Offline stub mode
  ...
SCRIPT_EXIT=
```

### Observed pass after the change

Same focused test (exit code 0):

```text
Passed!  - Failed:     0, Passed:     1, Skipped:     0, Total:     1, Duration: 55 ms - Wip.Bones.Host.Tests.dll (net10.0)
EXIT_CODE=0
```

Host process with `MaxGamesPerRun=0`, no opt-in, stub provider, temporary data directory
(exit code `-532462766` / unsigned `3762504530`, the .NET unhandled-exception code):

```powershell
Remove-Item Env:BONES_ALLOW_UNBOUNDED -ErrorAction SilentlyContinue
$env:BONES_MAX_GAMES_PER_RUN='0'
$env:BonesHost__ModelProvider='stub'
$env:BonesHost__DataDirectory='.tmp/goal-round2/host-refusal-data'
$env:ASPNETCORE_URLS='http://127.0.0.1:51234'
& WIP/Wip.Bones.Host/bin/Debug/net10.0/Wip.Bones.Host.exe
```

```text
startup-config: maxGamesPerRun=0
startup-config: allowUnbounded=false
startup-plan: plan.warning=UNBOUNDED: MaxGamesPerRun=0 disables the hard cap. ...
Unhandled exception. System.InvalidOperationException: Bones host configuration is invalid:
MaxGamesPerRun=0 disables the per-run game cap. Set MaxGamesPerRun to a positive integer, or set
BonesHost:AllowUnbounded=true (BONES_ALLOW_UNBOUNDED=true or BonesHost__AllowUnbounded=true) to explicitly opt in to
an unbounded run.
   at Wip.Bones.Host.BonesStartupPlanner.ValidateOrThrow(BonesHostOptions options) in ...\BonesStartupPlan.cs:line 39
   at Wip.Bones.Host.BonesHostApplication.Build(String[] args) in ...\BonesHostApplication.cs:line 37
EXIT_CODE=-532462766
```

Launcher after the parse fix (exit code 0 for the harness; all expected outcomes observed):

```text
value='00' allow='' -> REFUSED BONES_MAX_GAMES_PER_RUN=0 (unlimited) requires explicit BONES_ALLOW_UNBOUNDED=true.
value=' 0' allow='' -> REFUSED BONES_MAX_GAMES_PER_RUN=0 (unlimited) requires explicit BONES_ALLOW_UNBOUNDED=true.
value='+0' allow='' -> REFUSED BONES_MAX_GAMES_PER_RUN=0 (unlimited) requires explicit BONES_ALLOW_UNBOUNDED=true.
value='0' allow='' -> REFUSED BONES_MAX_GAMES_PER_RUN=0 (unlimited) requires explicit BONES_ALLOW_UNBOUNDED=true.
value='0' allow='true' -> ACCEPTED
value='10' allow='' -> ACCEPTED
```

Focused A suites after the change (exit code 0):

```text
Passed!  - Failed:     0, Passed:    15, Skipped:     0, Total:    15, Duration: 98 ms
```

## B. Gate opponent field is separate from scored truth

### Test-first failure (before the change)

The benchmark was run with the new option before adding it. The argument parser silently stored
unknown options, so `--opponents-arena` was ignored and the held-out arena supplied both fields.

```powershell
WIP/Wip.Bones.Benchmark/bin/Debug/net10.0/Wip.Bones.Benchmark.exe gate --snapshot .tmp/bench/history.json --arena .tmp/bench/heldout2000.json --opponents-arena .tmp/bench/dev2000.json --out .tmp/goal-round2/gate-prefix.json --reps 200 --matches 8 --seed 700001 --target 12
```

Observed (exit code 0; no `gate arenas` line, held-out opponents):

```text
gate study: pairs=48 reps=200 maxMatches=32 seed=700001 opponents=s1:initial-seat-1-v1@03b5cfa1,s2:initial-seat-2-v7@01d525a3,s3:initial-seat-3-v2@0044fb80,s4:initial-seat-4-v9@00b1a655
```

### Observed pass after the change

Exact required command (exit code 0):

```powershell
WIP/Wip.Bones.Benchmark/bin/Debug/net10.0/Wip.Bones.Benchmark.exe gate --snapshot .tmp/bench/history.json --arena .tmp/bench/heldout2000.json --opponents-arena .tmp/bench/dev2000.json --out .tmp/goal-round2/gate-clean.json --reps 200 --matches 8 --seed 700001 --target 12
```

```text
gate arenas: truth='heldout' (.tmp/bench/heldout2000.json); opponents='dev' (.tmp/bench/dev2000.json)
gate study: pairs=48 reps=200 maxMatches=32 seed=700001 opponents=s1:initial-seat-1-v2@004987b1,s2:initial-seat-2-v3@32543a2f,s3:initial-seat-3-v5@01d7ec22,s4:initial-seat-4-v7@002e82b4
...
-> .tmp/goal-round2/gate-clean.json
EXIT_CODE=0
```

The printed opponents equal the `dev2000.json` per-seat maxima exactly, not the held-out maxima.
Split-arena `confidence_paired_n8` result: `+0.041 [+0.028, +0.055]` (dev opponents, held-out truth).

### Existing behavior preserved when the option is absent

```powershell
WIP/Wip.Bones.Benchmark/bin/Debug/net10.0/Wip.Bones.Benchmark.exe gate --snapshot .tmp/bench/history.json --arena .tmp/bench/heldout2000.json --out .tmp/goal-round2/gate-absent.json --reps 200 --matches 8 --seed 700001 --target 12
```

Observed (exit code 0):

```text
gate arenas: truth='heldout' (.tmp/bench/heldout2000.json); opponents='heldout' (.tmp/bench/heldout2000.json)
gate study: pairs=48 reps=200 maxMatches=32 seed=700001 opponents=s1:initial-seat-1-v1@03b5cfa1,s2:initial-seat-2-v7@01d525a3,s3:initial-seat-3-v2@0044fb80,s4:initial-seat-4-v9@00b1a655
| confidence_paired_n8 | yes | 8 | -14.428 | [-14.437, -14.420] | -0.072 | +0.042 [+0.031, +0.053] | 19.8% | 17.2% | 22.4% |
```

`gate-prefix.json` and `gate-absent.json` are identical excluding `ElapsedMilliseconds`:

```text
gate-prefix vs gate-absent identical (excluding ElapsedMilliseconds): True
arms prefix=6 absent=6
```

### Frozen benchmark claims reproduced

Arena rerun (exit code 0):

```powershell
WIP/Wip.Bones.Benchmark/bin/Debug/net10.0/Wip.Bones.Benchmark.exe arena --snapshot .tmp/bench/history.json --out .tmp/goal-round2/arena-round2.json --games 2000 --master-seed 910001 --label dev --target 12
```

```text
arena 'dev': strategies=65 field=70 gamesPerStrategy=2000 seed=910001
games=130000
```

Comparison with the frozen `.tmp/bench/dev2000.json`:

```text
strategies recorded=65 repeat=65 missing=0
winMismatch=0 diffMismatch=0
```

The frozen gate claim `confidence_paired_n8 DeltaVsBaseline = +0.042 [0.031, 0.053]` also reproduced
with the unchanged single-arena protocol (above).

## Required build/test commands

The task's project paths contained typos; the correct checked-in paths were used.

```powershell
dotnet build WIP/Wip.Bones.Host/Wip.Bones.Host.csproj --nologo -v minimal
```

```text
Build succeeded.
    0 Warning(s)
    0 Error(s)
EXIT_CODE=0
```

```powershell
dotnet test WIP/Wip.Bones.Host.Tests/Wip.Bones.Host.Tests.csproj --filter BonesStartupPlanTests --nologo -v minimal
```

```text
Passed!  - Failed:     0, Passed:     9, Skipped:     0, Total:     9, Duration: 15 ms - Wip.Bones.Host.Tests.dll (net10.0)
EXIT_CODE=0
```

Benchmark build:

```powershell
dotnet build WIP/Wip.Bones.Benchmark/Wip.Bones.Benchmark.csproj --nologo -v minimal
```

```text
Build succeeded.
    0 Warning(s)
    0 Error(s)
EXIT_CODE=0
```

## Files changed

| File | Change |
|---|---|
| `WIP/Wip.Bones.Host/BonesHostOptions.cs` | Added `AllowUnbounded` (default false), binding precedence `BONES_ALLOW_UNBOUNDED` → `BonesHost__AllowUnbounded` → `BonesHost:AllowUnbounded`, and `allowUnbounded=` startup diagnostic. |
| `WIP/Wip.Bones.Host/BonesStartupPlan.cs` | Added `BonesStartupPlanner.ValidateOrThrow`; throws `InvalidOperationException` when `MaxGamesPerRun <= 0 && !AllowUnbounded`. |
| `WIP/Wip.Bones.Host/BonesHostApplication.cs` | Calls `ValidateOrThrow` after the startup plan is printed and before services are built. |
| `WIP/Wip.Bones.Host/appsettings.json` | Added `"AllowUnbounded": false`. |
| `WIP/Wip.Bones.Host/README.md` | Added the `AllowUnbounded` row to the configuration table (table only). |
| `WIP/Wip.Bones.Host.Tests/BonesHostTestFactory.cs` | Test factory default `BonesHost:AllowUnbounded=true`, preserving tests that set `MaxGamesPerRun=0`. |
| `WIP/Wip.Bones.Host.Tests/BonesStartupPlanTests.cs` | Added `ValidateOrThrow` refusal/allow/negative-cap tests and checked-in `AllowUnbounded=false` assertion. |
| `WIP/Wip.Bones.Host.Tests/BonesHostUnboundedStartupTests.cs` | New test-first behavioral test: `Build` refuses effective 0 without opt-in. |
| `WIP/Wip.Bones.Host.Tests/BonesHostAllowUnboundedOptionsTests.cs` | New binding tests for default, config, double-underscore env, dedicated env, and dedicated-env veto. |
| `WIP/Wip.Bones.Benchmark/Program.cs` | Added optional `--opponents-arena`; opponents from that arena, truth strengths always from `--arena`; prints both labels; help updated. |
| `WIP/Wip.Bones.Benchmark/README.md` | Documents split opponent/truth arenas, labels, and updated study command. |
| `bones.ps1` | Parses `BONES_MAX_GAMES_PER_RUN` with `[int]::TryParse`; refuses effective 0 unless `BONES_ALLOW_UNBOUNDED` parses true; rejects non-integer values. |
| `docs/goal-bones-v3-learning/round-2/implementation.md` | This evidence record. |

## What did not pass

- `BonesCoLearningHostTests.BonesCoLearningHost_GivenRestartWithResumeFromBest_ExpectedAllFourSeatsLoadLibraryStrategies`
  still fails: `Expected at least 1 seat with library entry and MatchesPlayed>=1 after first run; got 0 total entries, 0 with matches.`
  This is a pre-existing, documented open remediation item (R4 in `harness/requirements/Wip.Bones.CoLearningRemediation.md`),
  not caused by this change: the host reaches `iterationCount>=1` and `MaxGamesPerRun=0` now passes only through the
  factory's explicit `AllowUnbounded=true`. The other 5 co-learning host tests passed with that factory default
  (observed: `Failed: 1, Passed: 5, Total: 6`).
- The benchmark CLI has no in-scope xUnit test project, so `--opponents-arena` proof is the before/after CLI evidence above,
  not a unit test. No assertion was weakened or skipped.
- The host README prose sentence outside the configuration table (stating that `0` still means unlimited) was left untouched
  per the "configuration table only" scope; the new table row records that the opt-in is required.

Evidence logs are under `.tmp/goal-round2/` (`a-prefix-test.log`, `a-postfix-test.log`,
`a-launcher-postfix.log`, `a-host-process-refusal.log`, `b-gate-prefix.log`, `b-gate-postfix.log`,
`b-gate-absent.log`, `repro-arena.log`, `host-build.log`, `host-startup-plan-tests.log`,
`b-build.log`, `a-focused-tests.log`).
