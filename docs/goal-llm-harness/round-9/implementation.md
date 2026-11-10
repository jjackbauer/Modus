# Round 9 — Executable final-evidence numbers

**Date**: 2026-11-09
**Scope**: one new test file, `WIP/Wip.Tholdem.Tests/ModelHarness/FinalEvidenceTests.cs`,
and this report. No product file, existing test, csproj, `Wip.slnx`, scratch host, or artifact was
edited. The test reads recorded bytes only; it makes no model call, opens no network socket, and
does not read `.secrets/deepseek.env`.

## 1. What was added

One test file, one test:

- `WIP/Wip.Tholdem.Tests/ModelHarness/FinalEvidenceTests.cs` — class at line 13, test
  `FinalEvidence_GivenRecordedRuns_ExpectedEndToEndState` at line 53.
- The repository root comes from `AgentTestSupport.RepositoryRoot()` (line 55); every artifact path
  is composed from it.
- A missing artifact fails through `RequireFile` (lines 324-329), whose message names the operator
  run for that artifact.

The test pins the six integrated surfaces the final report cites:

| # | Surface | Test lines |
|---:|---|---|
| 1 | LLM arm report + sidecar | 58-97 |
| 2 | LLM verdict | 99-123 |
| 3 | Deterministic arm report + sidecar | 125-158 |
| 4 | Both champion pointers | 160-181 |
| 5 | Live Ponder session | 183-285 |
| 6 | Five frozen round-0 hashes | 287-321 |

## 2. Test-first execution and the observed red evidence

The artifact values were extracted independently before the test was written (read-only
`fs`/`node` extraction, no mutation). The first draft then followed the earlier round-8 index
reading, `requestMessages[1]`; that index is stale once the session memory window fills. The build
succeeded and the focused run failed on a real assertion, not on a compile error:

~~~text
FAIL FinalEvidenceTests.FinalEvidence_GivenRecordedRuns_ExpectedEndToEndState: TrueException: Last-message handNumber mismatches (20): call 2: observation hand 1 != turn hand 3; call 3: observation hand 1 != turn hand 4; call 4: observation hand 3 != turn hand 4; ... call 33: observation hand 15 != turn hand 19
TOTAL 1 PASSED 0 FAILED 1 SKIPPED 0
~~~

The objective names the **last** request message as the current observation. The first draft was
corrected to `requestMessages[requestMessages.GetArrayLength() - 1]` (line 240). The same focused
run then reported `TOTAL 1 PASSED 1 FAILED 0 SKIPPED 0`.

**No supplied literal was wrong, so no literal was changed.** The stale-index run was the only red
before green. Each literal in the test was checked against the recorded artifact before writing and
matched on the corrected run: schema, arm, model kind, seed, all six budget fields, start/final ids,
promotions, transcript counts, token totals, verdict values, criteria, per-iteration flags,
deterministic maxima, pointer hash and exact pointer bytes, Ponder counts/tally/streets/holes, and
the five frozen hashes. The test was not weakened, skipped, or made conditional to pass.

## 3. Every command and its observed TOTAL

`<runner>` = `.tmp/tholdem-verify/RunXunit/bin/Release/net10.0/RunXunit.exe`;
`<dll>` = `WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll`.

| # | Command | Exit | Observed result |
|---:|---|---:|---|
| 1 | `dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release` (first draft) | 0 | `Build succeeded.` / `12 Warning(s)` / `0 Error(s)`; no warning names `FinalEvidenceTests.cs` |
| 2 | `<runner> <dll> FinalEvidenceTests` (first draft, index 1) | 1 | `TOTAL 1 PASSED 0 FAILED 1 SKIPPED 0`; 20 handNumber mismatches |
| 3 | Correct index to last request message; `dotnet build ... -c Release` | 0 | `Build succeeded.` / `12 Warning(s)` / `0 Error(s)` |
| 4 | `<runner> <dll> FinalEvidenceTests` (final) | 0 | `TOTAL 1 PASSED 1 FAILED 0 SKIPPED 0` |
| 5 | `<runner> <dll>` (final full suite) | 1 | `TOTAL 444 PASSED 443 FAILED 1 SKIPPED 0`; only failure is the mandated Ponder tool-call gate |
| 6 | `<runner> <dll> ModelHarness` (final) | 1 | `TOTAL 50 PASSED 49 FAILED 1 SKIPPED 0`; same single failure |

Baseline was `TOTAL 443 PASSED 442 FAILED 1 SKIPPED 0`; 443 + 1 new test = 444, with 443 passing
and exactly the same single red gate.

The full-suite failure is unchanged and is not caused by the new test:

~~~text
FAIL PonderEvidenceTests.Ponder_GivenLiveModel_ExpectedAtLeastOneToolCall: TrueException: The recorded live session reports summary.toolCalls=0: the live model made no tool call in the 20-hand Ponder match.
TOTAL 444 PASSED 443 FAILED 1 SKIPPED 0
~~~

## 4. What the test asserts, with concrete literals

### 4.1 LLM arm report (lines 58-97)

- File SHA-256 is `32596a3812758e07213fa3b5f1ae8178263442cb38c8db96b87e75fa1e75fa2d`
  (line 64) and equals the sidecar's trimmed value (line 65).
- `schema` `tholdem.modelharness.run.v1` (line 69), `arm` `llm` (line 70),
  `modelKind` `live` (line 71), `seed` `1099511927776` (line 72).
- Budget (lines 74-80): iterations 10, trainingHands 2000, evaluationSeeds 50,
  evaluationHandsPerMatch 20, marginBbPer100 5, minSeeds 24.
- `startStrategyId` and `finalChampionId` are both `tholdem-strategy-v1-c9d1a24b`
  (lines 82-83); `promotions` 0 (line 84); `transcriptCount` 10 (line 85);
  `transcriptOutcomes.ok` 10 (line 86); promptTokens 6213 (line 87); completionTokens 800
  (line 88).
- Exactly 10 iterations (line 91), every `promoted` false (lines 92-97).

### 4.2 LLM verdict (lines 99-123)

- `verdict` `no-promotion` (line 104), `promotions` 0 (line 105),
  `bestLcb` `1.6233195816931527` (line 106), `bestMean` `50.03` (line 107),
  `pointerSha256` `ba24279777d1c52c3219fef7e9184df0e15313d2a6cc28cfd2c96868358ad805`
  (line 108).
- Criteria margin 5 and minSeeds 24 (lines 110-112).
- Exactly 10 iterations (line 115); every iteration has `recomputedPolicy` false,
  `recomputedReason` `lcb-below-margin`, `recomputedVerified` true,
  `reportHashMatchesFile` true, and `candidateMatchesProposal` true (lines 116-123).

### 4.3 Deterministic arm report (lines 125-158)

- Sidecar SHA-256 `bf1b0875336fcc4b4c10f590f3e761f546c543a5f1dcc5b12365d35be1bf08ff`
  equals the report file's hash (lines 131-132).
- `arm` `deterministic` (line 137), `modelKind` `none` (line 138), `promotions` 0
  (line 141), `transcriptCount` 0 (line 142), `finalChampionId` equals
  `startStrategyId` and both are `tholdem-strategy-v1-c9d1a24b` (lines 139-140).
- Exactly 10 iterations (line 145), every `promoted` false (lines 146-151).
- Best (maximum) `lowerBoundBbPer100` is `2.396597390544052` (lines 153-155) and best
  `meanBbPer100` is `7.7` (lines 156-158) across the iterations.

### 4.4 Champion pointers (lines 160-181)

Both `.tmp/goal-llm-harness/arms/arm-llm/active-champion.json` and
`.tmp/goal-llm-harness/arms/arm-deterministic/active-champion.json` are read. Each has SHA-256
`ba24279777d1c52c3219fef7e9184df0e15313d2a6cc28cfd2c96868358ad805` (line 179) and exact content
`{"strategyId":"tholdem-strategy-v1-c9d1a24b","iteration":0}` (line 180).

### 4.5 Live Ponder session (lines 183-285)

- `modelKind` `live` (line 194), `hands` 20 (line 195),
  `summary.toolCalls` 0 (line 196), `servedModels` contains `deepseek-flash` (line 203).
- 33 turns and 33 calls (lines 205-208).
- Exact action tally: Fold 6, Check 18, Call 7, Bet 1, Raise 1 (lines 210-220), with the exact key
  set `Bet, Call, Check, Fold, Raise` (lines 213-215).
- Every turn `actionSource` is `model` (lines 222-225).
- The current observation is the **last** request message (line 240); it is a `user` message
  (line 241) carrying the observation marker (lines 243-247).
- Last-message `handNumber` is compared with the paired turn's `hand` (lines 251-257); the
  accumulated mismatch count is asserted to be 0 (lines 272-274). The corrected index produced
  0 mismatches; the first draft's stale index produced the 20 observed above.
- Streets at the last request message: exactly `{0,1,2,3}` with counts 14, 7, 6, 6
  (lines 275-279).
- Distinct `ownHoleCards` pairs: 13 (line 280), each observation contributing exactly two cards
  (line 268).
- Tool catalogue: the four names are exactly `equity-calculator`, `pot-odds`,
  `hand-history-query`, `opponent-statistics` and the count is 4 (lines 282-285), read from
  `TholdemToolCatalog.CreateDefinitions()`; no tool name changed.

### 4.6 Frozen round-0 baseline (lines 287-321)

- The five table rows of `docs/goal-llm-harness/round-0/frozen-baseline.txt` are parsed from the
  Markdown table (lines 296-311); exactly 5 distinct rows are required (line 313).
- Each row's current file SHA-256 is computed from the repository root and asserted equal to the
  recorded value (lines 314-321). The five recorded hashes are matched by the current files:
  `EvaluationCriteria.cs` `9ff873f0...`, `PromotionPolicy.cs` `2bb7a800...`,
  `PromotionVerifier.cs` `72252cdd...`, `MatchEvaluator.cs` `56c85e2d...`, and
  `SeedDerivation.cs` `ec4e7845...`.

## 5. What is not passing

- The same single red gate remains: `PonderEvidenceTests.Ponder_GivenLiveModel_ExpectedAtLeastOneToolCall`
  fails because the recorded live session reports `summary.toolCalls=0`. That test was not edited,
  skipped, deleted, or weakened.
- Nothing else is failing: the new test is green and the full suite is 444 total with only that gate
  red.
- No artifact was modified. The test reads each artifact with `File.ReadAllBytes` or
  `File.ReadAllText` only; the running suite does not rewrite them.

## 6. File identity

`WIP/Wip.Tholdem.Tests/ModelHarness/FinalEvidenceTests.cs`: 330 lines, 16443 bytes, UTF-8 without
BOM (first three bytes `75 73 69`, no `EF BB BF`), final byte `0A`, SHA-256
`81aee6aecafa589adec29bac34c06bad860c651e03f67da7a3c229643f6e4d94`.
