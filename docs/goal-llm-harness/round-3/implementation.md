# Round 3 - WP-4 Live Configuration and WP-5 Arm Runner

Scope: the live DeepSeek configuration and operator secret-file loader outside `Learning`, the
scripted loop/gate tests, and the scratch two-arm runner with a re-derivation verify mode. No live
model call and no network call was made. `--arm llm` was not run and `--secret-file` was not
passed; the operator secret was neither read nor printed.

## (a) Test-first command and the observed failing output

The two test files were written first:

- `WIP/Wip.Tholdem.Tests/ModelHarness/LiveModelConfigurationTests.cs`
- `WIP/Wip.Tholdem.Tests/ModelHarness/TrainingLoopLlmPolicyTests.cs`

No production type existed yet, so the exact test-first command was:

```
dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release
```

Observed output (exit code 1):

```
C:\Users\ricar\dev\Modus\WIP\Wip.Tholdem.Tests\ModelHarness\LiveModelConfigurationTests.cs(109,19): error CS0103: The name 'LiveModelConfiguration' does not exist in the current context [C:\Users\ricar\dev\Modus\WIP\Wip.Tholdem.Tests\Wip.Tholdem.Tests.csproj]
C:\Users\ricar\dev\Modus\WIP\Wip.Tholdem.Tests\ModelHarness\LiveModelConfigurationTests.cs(114,27): error CS0103: The name 'LlmSecretFile' does not exist in the current context [C:\Users\ricar\dev\Modus\WIP\Wip.Tholdem.Tests\Wip.Tholdem.Tests.csproj]
...
Build FAILED.
    12 Warning(s)
    38 Error(s)
```

The test assembly could not compile, so this is a compile-time red state rather than a runnable red
suite. The implementation was then written against those tests.

## (b) Build, test and run commands with observed summaries

| # | Command | Observed summary |
|---|---|---|
| 1 | `dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release` (baseline, before edits) | `Build succeeded.` / `0 Warning(s)` / `0 Error(s)` |
| 2 | `.tmp/tholdem-verify/RunXunit/bin/Release/net10.0/RunXunit.exe WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll` (baseline) | `TOTAL 418 PASSED 418 FAILED 0 SKIPPED 0` |
| 3 | `dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release` (tests only, no production types) | `Build FAILED.` / 12 warnings / 38 errors; see (a) |
| 4 | `dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release` (implementation present) | `Build succeeded.` / `12 Warning(s)` / `0 Error(s)` |
| 5 | `.tmp/tholdem-verify/RunXunit/bin/Release/net10.0/RunXunit.exe WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll LiveModelConfigurationTests` | `TOTAL 5 PASSED 5 FAILED 0 SKIPPED 0` |
| 6 | `.tmp/tholdem-verify/RunXunit/bin/Release/net10.0/RunXunit.exe WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll TrainingLoopLlmPolicyTests` | `TOTAL 2 PASSED 2 FAILED 0 SKIPPED 0` |
| 7 | `dotnet build .tmp/goal-llm-harness/arms/Arms.csproj -c Release` (first attempt) | `Build FAILED.` / 2 errors: `CS0246: IRefinementPolicy` and `CS1503` at the `TrainingLoop.Run` call |
| 8 | `dotnet build .tmp/goal-llm-harness/arms/Arms.csproj -c Release` (after adding `using Wip.Tholdem.Agents.Learning.Refinement;`) | `Build succeeded.` / `0 Warning(s)` / `0 Error(s)` |
| 9 | `dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release --no-incremental` | `Build succeeded.` / `16 Warning(s)` / `0 Error(s)`; all 16 are pre-existing (AgentExecutor CS8425, DeepSeekChatModel CA2024, TrainCommand CS8602 x2, TrainingLoopTests CS8602/CS8604 x9, LearningWebIntegrationTests CS8602 x2, ThreatControlTests xUnit2013) and none is in a new file |
| 10 | `.tmp/tholdem-verify/RunXunit/bin/Release/net10.0/RunXunit.exe WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll` (after the clean rebuild) | `TOTAL 425 PASSED 425 FAILED 0 SKIPPED 0` |
| 11 | `dotnet run --project .tmp/goal-llm-harness/arms/Arms.csproj -c Release -- --prepare-start --data-dir .tmp/goal-llm-harness/arms/dry-start 0.70 0.70 0.12 1.0` | `start_strategy_id=tholdem-strategy-v1-c9d1a24b` / exit 0 |
| 12 | `dotnet run --project .tmp/goal-llm-harness/arms/Arms.csproj -c Release -- --arm deterministic --seed 1099511927776 --iterations 1 --training-hands 12 --eval-seeds 2 --eval-hands 2 --data-dir .tmp/goal-llm-harness/arms/dry-start --report .tmp/goal-llm-harness/arms/dry-run.json` | exit 0; lines quoted below |
| 13 | `dotnet run --project .tmp/goal-llm-harness/arms/Arms.csproj -c Release -- --verify --data-dir .tmp/goal-llm-harness/arms/dry-start --report .tmp/goal-llm-harness/arms/dry-run.json --out .tmp/goal-llm-harness/arms/dry-verdict.json` | exit 0; lines quoted below |
| 14 | `--prepare-start` repeated against the now-populated dry-start directory | `start_refused=active-champion-exists` / exit 3 |

Rows 1-2 are the frozen baseline (418). The final total is 418 + 7 new tests = 425, with 0 failed
and 0 skipped. Row 10 was run after the row-9 clean rebuild.

### Observed dry-run lines (row 12)

```
iteration=1 champion=tholdem-strategy-v1-c9d1a24b challenger=tholdem-strategy-v2-f7a0e101 promoted=false verified=true outcome=rejected meanBbPer100=0 lcb=0 seeds=2
final_champion=tholdem-strategy-v1-c9d1a24b
transcript_count=0
transcript_outcomes=
prompt_tokens=0
completion_tokens=0
wall_clock_ms=2155
report=.tmp/goal-llm-harness/arms/dry-run.json
report_sha256=a80e7c61ce698a8c16e27c9b15e4fb3989cd6feabc2dfdee5576588dfdaa8437
```

The sidecar was checked after that run: `file_hash=a80e7c61ce698a8c16e27c9b15e4fb3989cd6feabc2dfdee5576588dfdaa8437`,
`sidecar_match=True`.

### Observed verify lines (row 13)

```
verdict=no-promotion
promotions=0
best_lcb=0
best_mean=0
```

The persisted verdict re-derived `recomputedPolicy=false`, `recomputedReason=lcb-below-margin`,
`recomputedVerified=true`, `reportHashMatchesFile=true`, `pointerHashMatchesFile=false` and
`verdict=no-promotion`, which matches the dry run's rejected iteration.

## (c) Files created

All files are UTF-8 without BOM (the first bytes of each C# file are `117,115,105` and the csproj
starts `60,80,114`). No existing test, product source, csproj or `Wip.slnx` was changed.

| File | Purpose |
|---|---|
| `WIP/Wip.Tholdem.ModelHarness/LiveModelConfiguration.cs` | `LiveModelConfiguration` (line 11) with the flag/key gate and base URL validation in `Resolve` (line 40), env-sourced `CreateChatModel` (line 79), and `LlmSecretFile` (line 105) with allowlisted `LoadIntoEnvironment` (line 164). The key value is never stored; `CreateChatModel` passes `env:DEEPSEEK_API_KEY` to the existing adapter. |
| `WIP/Wip.Tholdem.Tests/ModelHarness/LiveModelConfigurationTests.cs` | 5 tests: missing flag (line 17), missing key (line 35), base URL override/default/invalid (line 52), secret-file allowlist and no value leak (line 99), malformed line/duplicate/empty name (line 149). `CountingHttpMessageHandler` counts `SendAsync` and never sends. |
| `WIP/Wip.Tholdem.Tests/ModelHarness/TrainingLoopLlmPolicyTests.cs` | 2 tests: the real `LlmRefinementPolicy` over the scripted model drives 2 iterations with 2 transcripts and matching challenger artifacts (line 18); every persisted evaluation file re-derives the loop's frozen-gate verdict and default criteria (line 100). |
| `.tmp/goal-llm-harness/arms/Arms.csproj` | Scratch host; references `WIP/Wip.Tholdem.ModelHarness.csproj` and `WIP/Wip.Runtime.csproj`. Not added to `Wip.slnx`. |
| `.tmp/goal-llm-harness/arms/Program.cs` | CLI and composition: `PrepareStart` (line 92), `RunArm` (line 133), `Verify` (line 300), `CandidateMatchesProposal` (line 440), canonical run report (line 490) and canonical verdict (line 637). |
| `docs/goal-llm-harness/round-3/implementation.md` | This report. |
| `.tmp/goal-llm-harness/arms/dry-start/`, `dry-run.json`, `dry-run.json.sha256`, `dry-verdict.json` | Dry-run evidence; scratch, not deliverables. |

## (d) Stub, TODO and NotImplementedException confirmation

A case-insensitive content search (`TODO|NotImplementedException|stub|FIXME`) over the five new
source/project files returned:

```
CLEAN WIP/Wip.Tholdem.ModelHarness/LiveModelConfiguration.cs
CLEAN WIP/Wip.Tholdem.Tests/ModelHarness/LiveModelConfigurationTests.cs
CLEAN WIP/Wip.Tholdem.Tests/ModelHarness/TrainingLoopLlmPolicyTests.cs
CLEAN .tmp/goal-llm-harness/arms/Program.cs
CLEAN .tmp/goal-llm-harness/arms/Arms.csproj
```

Every delivered member has a working body. The only doubles are the test-local
`RecordingChatModel` and `CountingHttpMessageHandler`; both exist to prove the policy/refusal
without a network call.

## (e) Anything not passing, and unexecuted paths

Nothing in this round's scope is failing. The final observed suite line is:

```
TOTAL 425 PASSED 425 FAILED 0 SKIPPED 0
```

Deliberately not executed, per instruction: `--arm llm` and any `--secret-file` argument. Therefore:

- The live arm's real DeepSeek call and its persisted live transcript were not observed in this
  round; the orchestrator runs them.
- The live gate refusal path in the runner was not executed end-to-end. Its ordering is visible in
  the code: `LiveModelConfiguration.Resolve()` is called at `Program.cs:190`, returns `2` on a
  refusal at `Program.cs:195`, and the `HttpClient` is only constructed afterwards at
  `Program.cs:198`. The resolver-level zero-send property is proven by
  `LiveModelConfigurationTests.cs:17` and `:35`.
- The llm branch of `--verify` (transcript lookup and `candidateMatchesProposal`) is compiled but
  not executed by the deterministic dry run; the orchestrator's live verify is its first execution.
  The query takes the last record for a correlation id, so a repaired iteration compares the
  proposal that was actually evaluated.

## (f) Exact commands for the orchestrator

Run from the repository root. Create both start pointers first; each command refuses with exit 3 if
its pointer already exists. The secret file supplies only `DEEPSEEK_API_KEY` and
`DEEPSEEK_BASE_URL`; `THOLDEM_LIVE_MODEL` must be set in the child environment.

```powershell
# 1. Fresh pre-registered start for each arm (0.70 / 0.70 / 0.12 / 1.0, iteration 0).
dotnet run --project .tmp/goal-llm-harness/arms/Arms.csproj -c Release -- --prepare-start --data-dir .tmp/goal-llm-harness/arms/llm 0.70 0.70 0.12 1.0
dotnet run --project .tmp/goal-llm-harness/arms/Arms.csproj -c Release -- --prepare-start --data-dir .tmp/goal-llm-harness/arms/deterministic 0.70 0.70 0.12 1.0

# 2. LLM arm (the only live run). Load the operator secret explicitly.
$env:THOLDEM_LIVE_MODEL = '1'
dotnet run --project .tmp/goal-llm-harness/arms/Arms.csproj -c Release -- --arm llm --seed 1099511927776 --iterations 10 --training-hands 2000 --eval-seeds 50 --eval-hands 20 --data-dir .tmp/goal-llm-harness/arms/llm --report .tmp/goal-llm-harness/arms/llm-run.json --secret-file .secrets/deepseek.env --model deepseek-chat --max-tokens 512 --temperature 0.2 --timeout-seconds 90 --max-calls 40

# 3. Deterministic control (same budget and start).
dotnet run --project .tmp/goal-llm-harness/arms/Arms.csproj -c Release -- --arm deterministic --seed 1099511927776 --iterations 10 --training-hands 2000 --eval-seeds 50 --eval-hands 20 --data-dir .tmp/goal-llm-harness/arms/deterministic --report .tmp/goal-llm-harness/arms/deterministic-run.json

# 4. Re-derive every verdict from the persisted report, pointer and evaluation files.
dotnet run --project .tmp/goal-llm-harness/arms/Arms.csproj -c Release -- --verify --data-dir .tmp/goal-llm-harness/arms/llm --report .tmp/goal-llm-harness/arms/llm-run.json --out .tmp/goal-llm-harness/arms/llm-verdict.json
dotnet run --project .tmp/goal-llm-harness/arms/Arms.csproj -c Release -- --verify --data-dir .tmp/goal-llm-harness/arms/deterministic --report .tmp/goal-llm-harness/arms/deterministic-run.json --out .tmp/goal-llm-harness/arms/deterministic-verdict.json
```

Exit codes: `0` for a completed run/verify, `2` for a live-gate or refiner refusal (the code is
printed as `refusal=<code>`), `3` for `--prepare-start` against an existing pointer, `1` for a
usage/IO error. A positive verdict requires a re-derived promoted-and-verified iteration whose
challenger id equals the final pointer's `strategyId`; a no-promotion run is reported exactly as
`verdict=no-promotion` with its best LCB and mean.
