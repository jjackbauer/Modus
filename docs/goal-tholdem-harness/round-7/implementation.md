# Round 7 — Bounded ablation runner (R-11)

## Outcome

- **R-11 — additive ablation path.** `HarnessAblationRunner.Run` executes the six fixed
  configurations (`none`, `signatures`, `memory`, `lineage`, `skills`, `all`) in the same seed,
  iteration count, training-hand count and evaluation shape. Each arm gets a freshly recreated
  `<DataDirectory>/ablation/<config>/data` directory and calls the unchanged `TrainingLoop.Run`
  (`HarnessAblationRunner.cs:71-96`; `TrainingLoop.cs:54-60`). The six arms and their flags are the
  fixed table at `HarnessAblationConfiguration.cs:33-67`.
- **One canonical report.** The runner emits `<DataDirectory>/ablation/report.json` as
  `tholdem.ablation.v1` with a fixed top-level and row key order, invariant numbers, and no clock,
  path, GUID or culture-dependent value (`HarnessAblationReport.cs:27-94`). The header carries the
  run's `seed` and `iterations`; each row carries the required ten fields plus a fixed `note`.
- **No pointer write.** The runner writes only `<root>/ablation/**`. `active-champion.json` and
  `meta/active-proposer.json` at the caller root are never opened; a test pre-creates both and
  asserts their exact bytes and the exact root entry set after a real run
  (`HarnessAblationTests.cs:37-53`).
- **Bounded and refusing.** `Iterations` 1..10, `TrainingHands` 1..2000, `EvaluationSeeds` 1..100,
  `EvaluationHandsPerMatch` 1..50 (`HarnessAblationRunner.cs:29-43`). Every bound is checked before
  any directory exists, and the ten-minute `Stopwatch` budget is checked before each configuration
  (`:104-142`, `:154-163`). Both refusals throw `HarnessAblationException` (an
  `InvalidOperationException`) with a stable code (`HarnessAblationOptions.cs:30-44`).
- **Status — 6 new tests pass.** The unfiltered supplementary runner reports **375/375** (369
  pre-existing + 6 new, 0 failed, 0 skipped). The pre-change baseline was **369/369**. No existing
  product or test file was edited, deleted, skipped or weakened, and no `.csproj`/`Wip.slnx` file was
  touched. The forced rebuild has 0 errors; its 16 warnings are all pre-existing and none mentions a
  new ablation file.

## Files

| File | Change |
|---|---|
| `WIP/Wip.Tholdem.Agents/Learning/Refinement/HarnessAblationOptions.cs` | new: the bounded options record and the stable `HarnessAblationException` codes |
| `WIP/Wip.Tholdem.Agents/Learning/Refinement/HarnessAblationConfiguration.cs` | new: the fixed six-configuration table and its notes |
| `WIP/Wip.Tholdem.Agents/Learning/Refinement/HarnessAblationReport.cs` | new: report row/report records and the canonical writer |
| `WIP/Wip.Tholdem.Agents/Learning/Refinement/HarnessAblationRunner.cs` | new: validation, scratch looping, summarisation and report emission |
| `WIP/Wip.Tholdem.Tests/Learning/HarnessAblationTests.cs` | new: 6 xUnit runtime tests |
| `docs/goal-tholdem-harness/round-7/implementation.md` | this document |

`TrainingLoop.cs`, `TrainingOptions.cs`, `TrainingRun.cs`, `TrainingIteration.cs`,
`HarnessMemoryStore.cs`, `HarnessBootstrapMode.cs`, the five frozen gate components,
`FrozenComponents`, `MetaLoop`, `MetaArtifact`, `ProposerGenome`, `StrategyGenome`,
`StrategyArtifact`, `StrategyAgent`, `SignatureDrivenRefinementPolicy`, `RefinementGuard`, the
engine/history/simulator/host/web/runtime/Bones/Shell projects, every existing test and every
project file were left untouched. The runner is purely additive.

## Test-first evidence

### 1. Red with the surface present and the runner stubbed

The test file was written first. The options/config/report types were added, and `Run` was left as a
runtime stub (`throw new NotImplementedException("red: the ablation runner is not implemented yet.")`),
so the red run is a runtime failure, not a compile failure. Command:

```powershell
.tmp/tholdem-verify/RunXunit/bin/Release/net10.0/RunXunit.exe `
  WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll HarnessAblation
```

Observed tail of the red run:

```text
Expected: typeof(Wip.Tholdem.Agents.Learning.Refinement.HarnessAblationException)
Actual:   typeof(System.NotImplementedException)
FAIL HarnessAblationTests.Run_GivenMemoryAndLineageConfigs_ExpectedSameChallengersAsSignatures: NotImplementedException: red: the ablation runner is not implemented yet.
FAIL HarnessAblationTests.Run_GivenOptionAboveIterationBound_ExpectedStableRefusal: ThrowsException: Assert.Throws() Failure: Exception type was not an exact match
Expected: typeof(Wip.Tholdem.Agents.Learning.Refinement.HarnessAblationException)
Actual:   typeof(System.NotImplementedException)
FAIL HarnessAblationTests.Run_GivenOtherOutOfBoundOptions_ExpectedStableRefusal: ThrowsException: Assert.Throws() Failure: Exception type was not an exact match
Expected: typeof(Wip.Tholdem.Agents.Learning.Refinement.HarnessAblationException)
Actual:   typeof(System.NotImplementedException)
FAIL HarnessAblationTests.Run_GivenSameOptionsTwice_ExpectedIdenticalReports: NotImplementedException: red: the ablation runner is not implemented yet.
FAIL HarnessAblationTests.Run_GivenSixConfigurations_ExpectedCanonicalReportAndNoPointerWrite: NotImplementedException: red: the ablation runner is not implemented yet.
TOTAL 6 PASSED 0 FAILED 6 SKIPPED 0
```

The captured tail begins with the `Expected`/`Actual` pair of
`Run_GivenExhaustedWallClockBudget_ExpectedStableRefusal`, whose own `FAIL` line scrolled above the
tail; the total line counts all six failures.

### 2. Pre-change baseline

Before any edit, the unfiltered supplementary runner reported:

```text
TOTAL 369 PASSED 369 FAILED 0 SKIPPED 0
```

### 3. Final green

```powershell
dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release --nologo -v q
.tmp/tholdem-verify/RunXunit/bin/Release/net10.0/RunXunit.exe `
  WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll HarnessAblation
.tmp/tholdem-verify/RunXunit/bin/Release/net10.0/RunXunit.exe `
  WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
```

Observed:

```text
BUILD_EXIT=0 (0 errors; forced rebuild reports 16 warnings, all pre-existing and none in a new ablation file)
TOTAL 6 PASSED 6 FAILED 0 SKIPPED 0
TOTAL 375 PASSED 375 FAILED 0 SKIPPED 0
```

The logs are preserved at `.tmp/goal-refinement/round7-focused-final.log`,
`.tmp/goal-refinement/round7-full-final.log` and
`.tmp/goal-refinement/round7-build-final.log`. `dotnet test` remains unusable in this session
(Win32Exception 5 on the VSTest testhost handle), so the supplied supplementary runner executed the
same compiled test assembly.

### 4. Test list

| Test (`HarnessAblationTests.cs`) | Behaviour proven |
|---|---|
| `Run_GivenSixConfigurations_ExpectedCanonicalReportAndNoPointerWrite` (`:32`) | six real runs; exact pointer bytes; exact root entries; BOM-free, newline-free report; exact schema/header/key order; six rows in fixed order; scratch pointer existence; no `meta/active-proposer.json` in a scratch directory |
| `Run_GivenSameOptionsTwice_ExpectedIdenticalReports` (`:142`) | two fresh roots, same options: byte-identical report files and identical field tuples |
| `Run_GivenOptionAboveIterationBound_ExpectedStableRefusal` (`:171`) | `Iterations = 11` refuses with `ablation-option-out-of-range`, no `ablation` directory, no report, empty root |
| `Run_GivenOtherOutOfBoundOptions_ExpectedStableRefusal` (`:193`) | zero/boundary-exceeding hands, seeds and hands-per-match plus blank data directory and an overlength/negative wall-clock cap all refuse before any write |
| `Run_GivenExhaustedWallClockBudget_ExpectedStableRefusal` (`:237`) | a zero wall-clock budget refuses with `ablation-wall-clock-exceeded` before the first configuration; no scratch and no report |
| `Run_GivenMemoryAndLineageConfigs_ExpectedSameChallengersAsSignatures` (`:257`) | real ledgers prove `memory` = `lineage` = `signatures` challengers while `none` differs; matching memory content hashes; write-only/read-only notes |

## Canonical report schema

File: `<DataDirectory>/ablation/report.json`, UTF-8 without BOM, no trailing newline
(`HarnessAblationRunner.cs:98-100`). Exact shape and fixed key order
(`HarnessAblationReport.cs:38-93`):

```json
{"schema":"tholdem.ablation.v1","seed":<ulong>,"iterations":<int>,"configurations":[{"config":"<name>","finalChampionId":"<strategy-id>","iterations":<int>,"verifiedPromotions":<int>,"meanBbPer100":<number|null>,"zeroEffectIterations":<int>,"inertProposals":<int>,"memoryEntries":<int>,"memoryContentHash":"<sha256>"|null,"note":"<fixed text>"}]}
```

| Field | Derivation |
|---|---|
| `schema` | constant `tholdem.ablation.v1` (`HarnessAblationReport.cs:32`) |
| `seed` | the option seed, emitted invariantly (`:40-41`) |
| `iterations` | the requested iteration count (`:42-43`) |
| `config` | the fixed configuration name (`HarnessAblationConfiguration.cs:20-30`) |
| `finalChampionId` | `run.FinalChampionId` (`HarnessAblationRunner.cs:194-197`) |
| `iterations` (row) | `run.Iterations.Count` (`:197`) |
| `verifiedPromotions` | iterations with `Verified && Decision.Promoted` (`:174-175`, `:198`) |
| `meanBbPer100` | mean of the non-null per-iteration `Report.MeanBbPer100`; `null` when there are no reports (`:170-182`, `:199`) |
| `zeroEffectIterations` | non-null reports with `abs(MeanBbPer100) < 0.05` and `abs(LowerBoundBbPer100) < 0.05` (`:27`, `:176-178`, `:200`) |
| `inertProposals` | iterations with `Report == null` (`:179`, `:201`) |
| `memoryEntries` | `HarnessMemoryStore.Load().Entries.Count` when `harness/memory.json` exists, else 0 (`:184-192`, `:202`) |
| `memoryContentHash` | the loaded snapshot's content hash, else `null` (`:191`, `:203`) |
| `note` | the fixed per-configuration statement, including the memory write-only and lineage read-only findings (`HarnessAblationConfiguration.cs:41-66`; `HarnessAblationRunner.cs:204`) |

Every numeric value goes through `CanonicalJsonWriter.Number`, so doubles use
`"R"` and `InvariantCulture` and `0.0` is `0` (`Simulation/CanonicalJsonWriter.cs:70-84`). The
report contains no clock, elapsed time, data path, GUID or culture-dependent text.

`MemoryEntries` is the number of canonical memory entries, not the sum of their `count` fields; the
3-iteration probe below wrote two entries while the per-entry counts summed to three.

## Configuration table

| Order | `config` | `HarnessRefinement` | `SkillRefinement` | Expected / observed role |
|---|---|---|---|---|
| 1 | `none` | `false` | `false` | legacy loop; no memory file (`memoryEntries = 0`, `memoryContentHash = null`) |
| 2 | `signatures` | `true` | `false` | signature-driven numeric proposal; memory written |
| 3 | `memory` | `true` | `false` | memory is **write-only**: the store is never passed into the policy, so challengers equal `signatures` |
| 4 | `lineage` | `true` | `false` | lineage is **read-only**: the measured loop never constructs `StrategyLineageReader`, so challengers equal `signatures` |
| 5 | `skills` | `true` | `true` | signature-driven, with a bluff-skill edit only when the strongest usable signal is `aggression-by-street` (`SignatureDrivenRefinementPolicy.cs:48-57`) |
| 6 | `all` | `true` | `true` | same flags as `skills`; memory and lineage remain observational |

The memory claim is source-verifiable: `TrainingLoop` creates the store only in refinement mode
(`TrainingLoop.cs:117`) and only calls `RecordPattern`/`RecordRejection` after a completed evaluation
(`:304-314`); it never passes the snapshot to the proposer. The lineage claim is source-verifiable:
the only product call to `StrategyLineageReader.Read` is in `HarnessBootstrap.cs:156`, and
`TrainingLoop` contains no lineage call.

## Real six-configuration ablation (D7)

Command and options:

```powershell
dotnet run --project .tmp/goal-refinement/round7-ablation-probe -c Release -- `
  .tmp/goal-refinement/round7-ablation-root-gate 3 120 24 10
```

This is a real run with seed `20261111`, 3 iterations, 120 training hands, 24 evaluation seeds and
10 evaluation hands per match (the default gate's `MinSeeds = 24` is met), and the default
ten-minute cap. The probe pre-created both sentinel pointers at the caller root before invoking the
runner.

Report artifact: `.tmp/goal-refinement/round7-ablation-root-gate/ablation/report.json`,
**2259 bytes, SHA-256 `29aecdc833c3d7f30c1ebe8d60660055bf5131ce7e34cfbff898c30bcba29175`**,
no BOM, no CR and no LF. Captured output:
`.tmp/goal-refinement/round7-ablation-gate-output.txt`.

Observed rows:

| `config` | `meanBbPer100` | `zeroEffectIterations` | `verifiedPromotions` | `memoryEntries` | `memoryContentHash` | challengers per iteration |
|---|---|---|---|---|---|---|
| `none` | 0.06944444444444445 | 0 | 0 | 0 | `null` | `0ceaa8fd`, `7f16f59a`, `ea96d95d` |
| `signatures` | -25.555555555555557 | 0 | 0 | 2 | `6c338995…df46` | `4f4ea1bf`, `a829c1b8`, `12f5b9e4` |
| `memory` | -25.555555555555557 | 0 | 0 | 2 | `6c338995…df46` | `4f4ea1bf`, `a829c1b8`, `12f5b9e4` |
| `lineage` | -25.555555555555557 | 0 | 0 | 2 | `6c338995…df46` | `4f4ea1bf`, `a829c1b8`, `12f5b9e4` |
| `skills` | -25.555555555555557 | 0 | 0 | 2 | `6c338995…df46` | `4f4ea1bf`, `a829c1b8`, `12f5b9e4` |
| `all` | -25.555555555555557 | 0 | 0 | 2 | `6c338995…df46` | `4f4ea1bf`, `a829c1b8`, `12f5b9e4` |

The final champion is `tholdem-strategy-v1-f100e793` for all six arms. No arm reached a verified
promotion; the gate was not weakened or bypassed (the report records the unchanged verdict outcome).

Full emitted report text:

```json
{"schema":"tholdem.ablation.v1","seed":20261111,"iterations":3,"configurations":[{"config":"none","finalChampionId":"tholdem-strategy-v1-f100e793","iterations":3,"verifiedPromotions":0,"meanBbPer100":0.06944444444444445,"zeroEffectIterations":0,"inertProposals":0,"memoryEntries":0,"memoryContentHash":null,"note":"legacy mode; harness refinement and skill refinement are off"},{"config":"signatures","finalChampionId":"tholdem-strategy-v1-f100e793","iterations":3,"verifiedPromotions":0,"meanBbPer100":-25.555555555555557,"zeroEffectIterations":0,"inertProposals":0,"memoryEntries":2,"memoryContentHash":"6c338995498f2dce75fba8823f37325419d316aaa56dde1add13d7432250df46","note":"harness refinement on; signature-driven proposals only"},{"config":"memory","finalChampionId":"tholdem-strategy-v1-f100e793","iterations":3,"verifiedPromotions":0,"meanBbPer100":-25.555555555555557,"zeroEffectIterations":0,"inertProposals":0,"memoryEntries":2,"memoryContentHash":"6c338995498f2dce75fba8823f37325419d316aaa56dde1add13d7432250df46","note":"memory is write-only; it never reaches the proposal, so challengers match signatures"},{"config":"lineage","finalChampionId":"tholdem-strategy-v1-f100e793","iterations":3,"verifiedPromotions":0,"meanBbPer100":-25.555555555555557,"zeroEffectIterations":0,"inertProposals":0,"memoryEntries":2,"memoryContentHash":"6c338995498f2dce75fba8823f37325419d316aaa56dde1add13d7432250df46","note":"lineage is read-only; the trainer never consults it, so challengers match signatures"},{"config":"skills","finalChampionId":"tholdem-strategy-v1-f100e793","iterations":3,"verifiedPromotions":0,"meanBbPer100":-25.555555555555557,"zeroEffectIterations":0,"inertProposals":0,"memoryEntries":2,"memoryContentHash":"6c338995498f2dce75fba8823f37325419d316aaa56dde1add13d7432250df46","note":"harness refinement on with skill edits; the policy may edit the bluff skill"},{"config":"all","finalChampionId":"tholdem-strategy-v1-f100e793","iterations":3,"verifiedPromotions":0,"meanBbPer100":-25.555555555555557,"zeroEffectIterations":0,"inertProposals":0,"memoryEntries":2,"memoryContentHash":"6c338995498f2dce75fba8823f37325419d316aaa56dde1add13d7432250df46","note":"harness refinement with skill edits; memory and lineage remain observational"}]}
```

### Pointer-byte evidence

Before and after the run, the probe hashed and printed the caller-root sentinels:

```text
CHAMPION_BEFORE=9feccc6a59284e9446a559f4fd5e43319e29f06d64e123a06194987c31b0aa6e
CHAMPION_AFTER=9feccc6a59284e9446a559f4fd5e43319e29f06d64e123a06194987c31b0aa6e
PROPOSER_BEFORE=210784c83d93bd9a22e2cd8f812e9ccf182386d63cd7416b08d922645ff38a9b
PROPOSER_AFTER=210784c83d93bd9a22e2cd8f812e9ccf182386d63cd7416b08d922645ff38a9b
CHAMPION_BYTES={"strategyId":"round-7-sentinel-champion","iteration":42}
PROPOSER_BYTES={"proposerId":"round-7-sentinel-proposer","generation":5}
```

The independent re-hash after the run confirms the same values. The automated test also asserts the
exact byte arrays (`HarnessAblationTests.cs:45-47`) and that the caller root contains exactly
`ablation`, `active-champion.json` and `meta` (`:48-53`). The loop's own pointer writes stay inside
the per-config scratch data directories, where the test finds `active-champion.json` and
`ledger.json` and no `meta/active-proposer.json` (`:125-132`).

## Memory/lineage no-difference finding

The fixed flags for `memory` and `lineage` are identical to `signatures`
(`HarnessAblationConfiguration.cs:42-56`), because the measured loop does not use either feature as
a proposal input. Both probes confirm the consequence instead of manufacturing a difference:

- Gate-shaped 3×120 run: `memory` and `lineage` reproduce `signatures`' three challenger ids exactly
  and share `meanBbPer100 = -25.555555555555557` and memory hash
  `6c338995…df46`; `none` differs on all three challengers and on the mean.
- A second 3×120 run with 8×8 evaluation reproduced the same equality and emitted a different,
  also-deterministic report with SHA-256
  `493a4a54a2e4cfe3d6859e90c54efd61bc33f0fd2e8e5703f9559035a74a0bee` at
  `.tmp/goal-refinement/round7-ablation-root-large/ablation/report.json` (captured at
  `.tmp/goal-refinement/round7-ablation-large-output.txt`).
- The pinned 1-iteration/24-hand test additionally proves the comparison is not vacuous: `none`
  challenger `0ceaa8fd` differs from `signatures` challenger `d24dcf6d`, while `memory` and `lineage`
  both produce `d24dcf6d` (`HarnessAblationTests.cs:264-281`).

`skills`/`all` were also data-dependent on the gate-shaped run: no iteration selected the
`aggression-by-street` skill edit, so they matched `signatures`. On the pinned 24-hand/1-iteration
test shape the strongest signal is `aggression-by-street-low`, so the skill edit is selected and the
`skills`/`all` challenger differs from `signatures` (`HarnessAblationTests.cs:280-281`; existing
R-10 evidence `HarnessSkillsTests.cs:488-523`). The report's note says the policy *may* edit the
skill, which is accurate.

## Determinism evidence

- `Run_GivenSameOptionsTwice_ExpectedIdenticalReports` runs two complete ablations in two fresh
  roots and compares the report files byte-for-byte, plus six field tuples
  (`HarnessAblationTests.cs:142-161`).
- The first real probe (2 iterations, 24 training hands, 2×2 evaluation) was executed twice and both
  runs produced report SHA-256
  `39d5fca6125092f7f108a99408a4c7ce43b69744ef833934e99c593691e0a24a`, length 2151, with identical
  champion/proposer hashes; the captured output is
  `.tmp/goal-refinement/round7-ablation-probe-output.txt`.
- No elapsed time, path or generated identifier enters the report; all numbers are invariant.

## Bounds and refusals

| Option | Bound | Refusal code | Test |
|---|---|---|---|
| `Iterations` | 1..10 | `ablation-option-out-of-range` | iteration-above and zero-bound cases |
| `TrainingHands` | 1..2000 | `ablation-option-out-of-range` | other-bounds test |
| `EvaluationSeeds` | 1..100 | `ablation-option-out-of-range` | other-bounds test |
| `EvaluationHandsPerMatch` | 1..50 | `ablation-option-out-of-range` | other-bounds test |
| `DataDirectory` | non-blank | `ablation-option-out-of-range` | other-bounds test |
| `WallClockCap` | `0`..10 minutes | `ablation-option-out-of-range` (outside) / `ablation-wall-clock-exceeded` (exhausted) | other-bounds and zero-budget tests |

Validation happens before `Path.Combine`/`Directory.CreateDirectory`
(`HarnessAblationRunner.cs:62-68`, `:104-142`), so every refused option leaves the caller root
empty; the tests assert `Directory.GetFileSystemEntries(root)` is empty. The budget check runs before
each of the six configurations (`:71-73`, `:154-163`), so a zero budget refuses before configuration
one starts.

## Deviations (all deliberate, none weakening an invariant)

1. **`WallClockCap` is an init-only option, shortenable but not lengthenable.** The brief specifies a
   ten-minute cap without naming a parameter. To exercise the refusal path deterministically a test
   needs a shortened budget; the property defaults to `TimeSpan.FromMinutes(10)`
   (`HarnessAblationOptions.cs:22`) and values above it are refused (`HarnessAblationRunner.cs:136-141`).
   Elapsed time is never reported.
2. **Each report row carries an extra fixed `note`.** R-11's configuration text requires the report
   to state that memory is write-only and lineage read-only. A per-row fixed `note` is the direct
   implementation (`HarnessAblationConfiguration.cs:41-66`); it is the only field beyond the
   required per-config list and is included in the fixed key order.
3. **Two extra tests.** `Run_GivenOtherOutOfBoundOptions_ExpectedStableRefusal` and
   `Run_GivenExhaustedWallClockBudget_ExpectedStableRefusal` cover the remaining bounds and the
   budget rather than leaving them unexercised. They are additive.
4. **The no-reports test was omitted.** The required test
   `Run_GivenAConfigWithNoReports_ExpectedNullMeanAndZeroPromotions` needs a configuration that
   completes zero evaluations. The six fixed configs use the default proposer; the only guard
   (`skill-inert`) is installed only for `SkillRefinement` and the runner exposes no proposer seam.
   A seed that makes every skill proposal inert would require changing product behaviour or adding a
   test-only injection point, both outside this round's constraints. The `null` mean path is
   implemented (`HarnessAblationRunner.cs:180-182`) but is the one unexercised branch; it is recorded
   here rather than faked.
5. **Supplementary runner.** `dotnet test` aborts with Win32Exception (5) on this machine; the
   supplied `.tmp/tholdem-verify/RunXunit` executes the same compiled assembly, as in prior rounds.

## Anything not passing

Nothing fails. All 375 tests pass (369 pre-existing + 6 new) with zero failures and zero skips, and
the build has zero errors. The only unverified branch is the no-reports `null` mean path described
in deviation 4. The only environment limitation remains the known `dotnet test` Win32Exception (5),
which is why the supplementary runner output is cited throughout.
