# Round 8 — Executable correction of the live Ponder behaviour claim

**Date**: 2026-11-09
**Scope**: one new test file, `WIP/Wip.Tholdem.Tests/ModelHarness/PonderBehaviorTests.cs`, and this report.
No product file, existing test, csproj, `Wip.slnx`, scratch host or session artifact was edited.

## 1. The claim and the artifact facts

The earlier narrative claimed the live model folded on every turn. The artifact says otherwise.
`.tmp/goal-llm-harness/ponder/live/ponder-session.json` records schema
`tholdem.ponder.session.v1`, `modelKind` `live`, 20 hands, 33 turns, 33 calls,
`summary.toolCalls` 0 and `servedModels` `["deepseek-flash"]`.

Read from the artifact before the test was written and re-derived after review (read-only script,
no mutation):

~~~text
turn tally                            {"Fold":6,"Check":18,"Call":7,"Bet":1,"Raise":1}
requestMessages[1] vs paired turn     20 handNumber mismatches   (stale session memory)
last request message vs paired turn   0 handNumber mismatches    (current observation)
streets at requestMessages[1]         14 / 7 / 6 / 6
streets at last request message       14 / 7 / 6 / 6
distinct ownHoleCards, requestMessages[1]   11
distinct ownHoleCards, last message         13
~~~

Both indexes happen to give the same street distribution, but only the last request message is
aligned with its turn: 20 of 33 calls carry a stale `handNumber` at `requestMessages[1]`, because the
memory window retains prior user/assistant messages. The current observation is the last message;
its distinct hole-pair count is **13**, not the stale index's 11.

## 2. Test-first execution and the observed red evidence

The counts were extracted first, then the test was written with provisional literals so the runner,
not the author, decided them. The first delivery parsed the wrong index (`requestMessages[1]`), as
the original instruction named it; after review the test was corrected to the last request message.
All observed failures, in order:

### 2.1 Over-strict message count

The first draft asserted exactly two request messages per call. Observed:

~~~text
FAIL PonderBehaviorTests.Ponder_GivenRecordedLiveSession_ExpectedVariedLegalActionMix: EqualException: Assert.Equal() Failure: Values differ
Expected: 2
Actual:   4
TOTAL 1 PASSED 0 FAILED 1 SKIPPED 0
~~~

Recorded request lengths are 2 (call 1), 4 (call 2) and 6 (calls 3-33). The over-strict assertion was
replaced with `length >= 2`; no required assertion was weakened.

### 2.2 Wrong river count on the first (stale-index) delivery

River literal provisionally 5:

~~~text
Expected: 5
Actual:   6
TOTAL 1 PASSED 0 FAILED 1 SKIPPED 0
~~~

### 2.3 Wrong hole-pair count on the first (stale-index) delivery

Distinct-pair literal provisionally 12:

~~~text
Expected: 12
Actual:   11
TOTAL 1 PASSED 0 FAILED 1 SKIPPED 0
~~~

### 2.4 Corrected index makes the stale 11 observable

After switching the test to the last request message, the stale literal 11 was left in place for one
run:

~~~text
FAIL PonderBehaviorTests.Ponder_GivenRecordedLiveSession_ExpectedVariedLegalActionMix: EqualException: Assert.Equal() Failure: Values differ
Expected: 11
Actual:   13
TOTAL 1 PASSED 0 FAILED 1 SKIPPED 0
~~~

The river literal 6 stayed correct because both indexes give 14/7/6/6. The hole-pair literal was
then corrected to 13, the current observation's true count, and the class ran green:
`TOTAL 1 PASSED 1 FAILED 0 SKIPPED 0`. The corrected literals match the pre-test extraction in
Section 1.

## 3. Every command and its observed result

`<runner>` = `.tmp/tholdem-verify/RunXunit/bin/Release/net10.0/RunXunit.exe`;
`<dll>` = `WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll`.

| # | Command | Exit | Observed result |
|---:|---|---:|---|
| 1 | `dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release` (baseline) | 0 | `Build succeeded.` / `0 Warning(s)` / `0 Error(s)` |
| 2 | `<runner> <dll>` (baseline) | 1 | `TOTAL 442 PASSED 441 FAILED 1 SKIPPED 0`; only failure = the mandated gate |
| 3 | First draft (stale index) written; build | 0 | `Build succeeded.` |
| 4 | `<runner> <dll> PonderBehaviorTests` (first draft) | 1 | `TOTAL 1 PASSED 0 FAILED 1 SKIPPED 0`; `Expected: 2 / Actual: 4` |
| 5 | Replace exact message count with `>= 2`; build | 0 | `Build succeeded.` / `0 Error(s)` |
| 6 | `<runner> <dll> PonderBehaviorTests` (river 5) | 1 | `TOTAL 1 PASSED 0 FAILED 1 SKIPPED 0`; `Expected: 5 / Actual: 6` |
| 7 | Set river 6 and hole pairs 12; build | 0 | `Build succeeded.` / `0 Error(s)` |
| 8 | `<runner> <dll> PonderBehaviorTests` (holes 12) | 1 | `TOTAL 1 PASSED 0 FAILED 1 SKIPPED 0`; `Expected: 12 / Actual: 11` |
| 9 | Set hole pairs 11; build | 0 | `Build succeeded.` / `12 Warning(s)` / `0 Error(s)` |
| 10 | `<runner> <dll> PonderBehaviorTests` (stale-index green) | 0 | `TOTAL 1 PASSED 1 FAILED 0 SKIPPED 0` |
| 11 | `<runner> <dll>` (first delivery, full) | 1 | `TOTAL 443 PASSED 442 FAILED 1 SKIPPED 0`; only failure = the mandated gate |
| 12 | `<runner> <dll> ModelHarness` (first delivery) | 1 | `TOTAL 49 PASSED 48 FAILED 1 SKIPPED 0`; only failure = the mandated gate |
| 13 | Correct index to last request message, add hand-alignment assert; build | 0 | `Build succeeded.` / `12 Warning(s)` / `0 Error(s)` |
| 14 | `<runner> <dll> PonderBehaviorTests` (stale literal 11) | 1 | `TOTAL 1 PASSED 0 FAILED 1 SKIPPED 0`; `Expected: 11 / Actual: 13` |
| 15 | Correct hole pairs to 13; build | 0 | `Build succeeded.` / `12 Warning(s)` / `0 Error(s)`; no warning names the new file |
| 16 | `<runner> <dll> PonderBehaviorTests` (final) | 0 | `TOTAL 1 PASSED 1 FAILED 0 SKIPPED 0` |
| 17 | `<runner> <dll>` (final full suite) | 1 | `TOTAL 443 PASSED 442 FAILED 1 SKIPPED 0`; only failure = the mandated gate |
| 18 | `<runner> <dll> ModelHarness` (final) | 1 | `TOTAL 49 PASSED 48 FAILED 1 SKIPPED 0`; only failure = the mandated gate |
| 19 | `dotnet build ... -c Release -t:Rebuild` (full warning census) | 0 | `Build succeeded.` / `16 Warning(s)` / `0 Error(s)`; none in `PonderBehaviorTests.cs` (the 16 are pre-existing: TrainingLoopTests, LearningWebIntegrationTests, ThreatControlTests, AgentExecutor, DeepSeekChatModel, TrainCommand) |

Baseline 442 + 1 new test = 443; 442 pass and exactly the same single failure as the baseline.

## 4. What the new test asserts

`WIP/Wip.Tholdem.Tests/ModelHarness/PonderBehaviorTests.cs` — one test,
`Ponder_GivenRecordedLiveSession_ExpectedVariedLegalActionMix` at line 22.

- Path resolution and operator message: `AgentTestSupport.RepositoryRoot()` (line 25) plus
  `.tmp/goal-llm-harness/ponder/live/ponder-session.json` (line 30); a missing artifact fails with
  the operator run (lines 31-34), matching the wording style of `PonderEvidenceTests.cs:33-36`.
- Canonical JSON: `File.ReadAllBytes` then `JsonDocument.Parse` (lines 36-38) — parse success is
  the canonicality assertion.
- Artifact identity literals: `modelKind` `"live"` (line 40), `hands` 20 (line 41),
  `summary.toolCalls` 0 (line 42, recorded as a fact, not a requirement),
  `servedModels` contains `"deepseek-flash"` (line 49).
- Turns: count 33 (line 52); grouped ordinally by `actionKind` (lines 54-56) with the exact key set
  `Bet, Call, Check, Fold, Raise` (lines 57-59) and exact counts Fold=6 (line 60), Check=18
  (line 61), Call=7 (line 62), Bet=1 (line 63), Raise=1 (line 64).
- Per turn: `actionKind` is in that turn's `legalActionKinds` case-insensitively
  (lines 74-77), and `actionSource` is `"model"` (line 78).
- Calls: count 33 (line 82); calls are paired to turns by position, with `sequence == index + 1`
  asserted (lines 86-91).
- The current observation is the **last** request message: length `>= 2` (line 94),
  `requestMessages[0]` is `system` (line 95), the last message is `user` (lines 96-97), and the
  task text is read from that last message (lines 99-105). The marker is the runtime observation
  contract produced by `TholdemRuntimeAgent.BuildTask`
  (`WIP/Wip.Tholdem.Agents/Runtime/TholdemRuntimeAgent.cs:166-180`) over
  `PlayerObservation.ToCanonicalJson` (`WIP/Wip.Tholdem/History/PlayerObservation.cs:136-197`).
- Alignment proof: every call's parsed observation `handNumber` equals its paired turn's `hand`
  (lines 106-108). Observed: 0 mismatches at the last message (20 at the abandoned index).
- Streets: key set exactly `{0,1,2,3}` (line 122) with counts 14 (line 123), 7 (line 124),
  6 (line 125) and 6 (line 126) — at least one call on each street, and the stable observed
  distribution.
- Distinct `ownHoleCards` pairs: each observation contributes its two-card joined pair
  (lines 112-119); the distinct count is 13 (line 127).
- Tool catalogue: the four names are exactly
  `equity-calculator, pot-odds, hand-history-query, opponent-statistics` with count 4
  (lines 129-132), read from `TholdemToolCatalog.CreateDefinitions()`; no tool name changed.

The test makes no model call and opens no network socket; it only parses the recorded bytes.

## 5. The one expected failure (unchanged)

~~~text
FAIL PonderEvidenceTests.Ponder_GivenLiveModel_ExpectedAtLeastOneToolCall: TrueException: The recorded live session reports summary.toolCalls=0: the live model made no tool call in the 20-hand Ponder match.
TOTAL 443 PASSED 442 FAILED 1 SKIPPED 0
~~~

`PonderEvidenceTests.cs` was not edited, skipped, deleted or weakened (mtime 2026-11-09 13:26:28,
SHA-256 `3090DF964880E0B55749D26B00222B62EA6E2859D0800ADD1A3B37347808C51A`). It remains the
measured D6 gate.

## 6. Scope, identity and notes

- The only file added is `PonderBehaviorTests.cs`: 6307 bytes, UTF-8 without BOM (first bytes
  `75 73 69`, no `EF BB BF`), final byte `0A`, SHA-256
  `63c4627f6e9c1ebc539aefdbf53751a51dc893b25adb40c974b3691e23a31fbc`. This is the corrected
  second delivery; the first delivery's stale-index version is superseded and not on disk.
- The session artifact was read only: mtime 2026-11-09 13:13:09, SHA-256
  `881604AFF8E043A372AEB9727679911D70E7848B74021D65301DABA00AABE0A9`, unchanged (only
  `File.ReadAllBytes` is used).
- No product source or existing test was modified. A modification-time scan of `WIP/**/*.cs`
  excluding `bin`/`obj` prints only files from earlier rounds plus this round's new test; no
  product file and no existing test appears.
- No network call, no live model call, no secret file opened; the test contains no model wiring.
- Correction record: the first delivery parsed `requestMessages[1]` as the observation. That index
  is stale session memory once the window fills (20/33 hand mismatches) and gave 11 distinct
  hole-card pairs. The final test parses the last request message, whose `handNumber` matches its
  turn on all 33 calls and whose distinct hole-card pair count is 13. The street distribution is
  14/7/6/6 at both indexes; no other count claim is made here.
- All 12 incremental warnings and 16 full-rebuild warnings are pre-existing and none names the new
  test file. No test is skipped.
