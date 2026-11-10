# Round 12 — remove every seed from the agent-facing surface

- **Repository**: `C:\Users\ricar\dev\Modus`, branch `WIP`.
- **Scope**: only the files listed in the round-12 task. `Wip.slnx`, the architecture tests, the
  engine/history projects, the learning gate/verifier, the CLI and `.github/` were not touched.
- **Verdict**: implemented. `AgentTurnContext` is now `(int HandNumber, int Seat, int ActionIndex)`;
  `SeedDerivation.DeriveTurnSeed` is deleted; every agent derives its randomness from
  `SeedDerivation.DeriveAgentSeed(observation.ToCanonicalJson(), hand, seat, actionIndex, stream)`;
  the round-11 dictionary attack has no target value in anything an agent receives.
- **Tests**: `TOTAL 182 PASSED 182 FAILED 0 SKIPPED 0` (was 179; +4 new facts, −1 replaced cheat-path
  fact). The suite was written red first, then fixed, then mutated.
- **Verification limits**: local `dotnet test` was not run (VSTest is unusable in this session; see
  [known-limits.md](../../tholdem/known-limits.md)); the supplementary runner over the compiled test
  assembly was used instead. CI was not observed. No browser/UI behaviour changed, so the viewer was
  exercised only through the in-process Kestrel tests.

## 1. Root cause

Round 10 replaced `AgentTurnContext.SessionSeed` with `TurnSeed =
SHA-256("tholdem-turn-seed|" + sessionSeed + "|" + hand + "|" + seat + "|" + action + "|" + stream)[0..8]`.
The digest is one-way, but the documented session seeds are tiny (`42`, `7`, `777`), so an agent could
enumerate candidate seeds and compare the digest with the value it was given. The round-11 falsifiers
recovered `42`, `7` and `777` this way and rebuilt the recorded decks.

The fix must therefore remove the **value**, not just its name: no seed, no digest of a seed, and no
other secret-derived value may reach an agent.

## 2. Test-first: red output against the pre-fix code

Command (same command before and after the fix):

```powershell
dotnet run --project .tmp/tholdem-verify/RunXunit/RunXunit.csproj -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll AgentInformationBoundaryTests
```

Observed against the pre-fix build (log: `WIP/Wip.Tholdem.Tests/bin/Release/net10.0/round12-evidence/red-boundary.log`):

```text
turn context         : AgentTurnContext { TurnSeed = 3386938122580612636, HandNumber = 1, Seat = 0, ActionIndex = 0 }
agent-visible values : 11
candidate seeds      : 0..100000
recovered seeds      : 42 (turn seed 3386938122580612636)
FAIL AgentInformationBoundaryTests.AgentInputs_GivenRecordedSeed42Hand1Enumeration_ExpectedNoCandidateTurnSeedAndDifferentDeck: TrueException: The enumeration recovered session seed(s) from the agent's inputs: 42
FAIL AgentInformationBoundaryTests.AgentSources_GivenExecutableCode_ExpectedNoSeedIdentifier: TrueException: Agent executable sources still reference a seed: AgentTurnContext.cs:Seed, AgentTurnContext.cs:TurnSeed, PotOddsEquityAgent.cs:TurnSeed, RandomLegalAgent.cs:TurnSeed, TightAggressiveAgent.cs:TurnSeed, StrategyAgent.cs:TurnSeed
FAIL AgentInformationBoundaryTests.AgentTurnContext_GivenReflection_ExpectedNoSeedValuedMember: TrueException: AgentTurnContext must not expose a seed-valued property; found: TurnSeed:UInt64
FAIL AgentInformationBoundaryTests.DeriveAgentSeed_GivenObservationAndTurnCoordinates_ExpectedStableTwiceAndChangedForDifferentObservation: TrueException: SeedDerivation.DeriveAgentSeed(string,int,int,int,ulong) is missing; determinism cannot be observed.
TOTAL 7 PASSED 3 FAILED 4 SKIPPED 0
```

The enumeration test is a literal replay of the falsifier procedure, implemented locally so it keeps
working after the API is deleted: for `candidate = 0..100000` it computes
`SHA-256("tholdem-turn-seed|candidate|hand|seat|action|stream")[0..8]` and compares that value with
**everything the agent can read**: every numeric token in `observation.ToCanonicalJson()`, every
numeric token in `context.ToString()`, and every numeric public property of the context. Before the
fix the context's own `TurnSeed` was one of those values, so candidate `42` matched.

The red log uses the original test name, `...ExpectedNoCandidateTurnSeedAndDifferentDeck`. The test was
later renamed to `...ExpectedNoCandidateSeedMatchAndDifferentDeck` so the five in-scope product docs
contain no `TurnSeed` token at all; the assertions are unchanged.

## 3. Attack before and after

### Before (pre-fix compiled assembly)

The printed context is the real context `MatchSimulator` handed to seat 0 for session seed 42, hand 1,
action 0. Enumerating candidate session seeds with the old formula finds exactly the value the context
carried:

```text
turn context         : AgentTurnContext { TurnSeed = 3386938122580612636, HandNumber = 1, Seat = 0, ActionIndex = 0 }
agent-visible values : 11
candidate seeds      : 0..100000
recovered seeds      : 42 (turn seed 3386938122580612636)
```

`SeedDerivation.Derive(42, 1, 0, 0, 0)` is the unchanged engine hand seed
`13149409904026330690`, whose first nine deck cards are `Qs Tc 4s Jd 8h 8s 8d Th 6c` — exactly the
recorded deal. This is the round-11 counterexample, reproduced by the new test.

### After (final build)

Same command, same test, post-fix output (log: `green-run-3.log` / `restored-run.log`):

```text
turn context         : AgentTurnContext { HandNumber = 1, Seat = 0, ActionIndex = 0 }
agent-visible values : 10
candidate seeds      : 0..100000
recovered seeds      : none
recorded deal        : Qs Tc 4s Jd 8h 8s 8d Th 6c
agent-seed deal      : Js Qd Kd 2d 2c 4s 8d 4h 3d
agent seed           : 7725157224249495112
```

There is no target: none of the 100001 candidate digests equals any value the agent receives. The deck
built from the agent's own derived seed (`SeededDeck.Create(7725157224249495112)`) is
`Js Qd Kd 2d 2c 4s 8d 4h 3d`, which differs from both the recorded deal and
`Derive(42, 1, 0, 0, 0)`. The test asserts all of that, plus that the recorded deal is still exactly
`Qs Tc 4s Jd 8h 8s 8d Th 6c` (the engine derivation is unchanged).

## 4. Implementation

### 4.1 Agent-facing types

- `WIP/Wip.Tholdem.Agents/AgentTurnContext.cs`:
  `public readonly record struct AgentTurnContext(int HandNumber, int Seat, int ActionIndex);`
  The XML doc states that these are public turn coordinates and that the context carries no seed of
  any width, no digest and no secret-derived value.
- `WIP/Wip.Tholdem.Agents/IPokerAgent.cs`: doc comment now says the context carries only hand number,
  acting seat and per-seat action index, with no seed, so an agent's inputs cannot be inverted to the
  session seed or the deck. The `ActAsync(PlayerObservation, AgentTurnContext, CancellationToken)`
  signature is unchanged.

### 4.2 Derivation

`WIP/Wip.Tholdem.Agents/SeedDerivation.cs`:

- `Derive(ulong sessionSeed, int handNumber, int seat, int actionIndex, ulong stream)` is **byte-for-byte
  unchanged**; the pinned test `SeedDerivationTests.Derive_GivenPinnedAcesContext_ExpectedExactSplitmix64Literal`
  still observes `13149409904026330690 == 0xB67C16A39F7E2642`, and the recorded seed-42 hand-1 deal is
  unchanged.
- `DeriveTurnSeed` and its `tholdem-turn-seed` domain constant are deleted.
- New:
  `public static ulong DeriveAgentSeed(string canonicalObservation, int handNumber, int seat, int actionIndex, ulong stream)`
  returns the first eight big-endian bytes of
  `SHA-256(UTF8("tholdem-agent-seed|" + canonicalObservation + "|" + handNumber + "|" + seat + "|" +
  actionIndex + "|" + stream))`, formatted with `CultureInfo.InvariantCulture`. The parameter is the
  agent's own input (its hole cards plus public state), so the value is reproducible and is not a
  function of the session seed or the deck.

### 4.3 Consumers

- `Baselines/RandomLegalAgent.cs`: kind stream `0` and size stream `1`.
- `Baselines/TightAggressiveAgent.cs`: bluff stream `2`.
- `Baselines/PotOddsEquityAgent.cs`: equity stream `0x50`.
- `Learning/StrategyAgent.cs`: bluff stream `2`.
- `Simulation/MatchSimulator.cs` and `WIP/Wip.Tholdem.Web/Live/LiveMatch.cs` construct
  `new AgentTurnContext(hand, seat, actionIndex)`.
- `Runtime/TholdemRuntimeAgent.cs` still deliberately does not use the context for decisions; its
  comment now says the context carries only public coordinates and no seed exists on the agent
  surface. The runtime agent never reads a context member.

## 5. Tests

New/changed facts in `WIP/Wip.Tholdem.Tests/AgentInformationBoundaryTests.cs`:

1. `AgentTurnContext_GivenReflection_ExpectedNoSeedValuedMember` — by reflection, no `ulong`/`long`/
   `string` property whose name contains `Seed`, and the public property shape is exactly
   `ActionIndex:Int32, HandNumber:Int32, Seat:Int32`.
2. `AgentSources_GivenExecutableCode_ExpectedNoSeedIdentifier` — after stripping comments, the source
   of `AgentTurnContext.cs` and `IPokerAgent.cs` contains no `Seed` substring, and every agent source
   (`Baselines/*`, `IPokerAgent.cs`, `AgentTurnContext.cs`, `StrategyAgent.cs`) contains no
   `TurnSeed`/`DeriveTurnSeed`/`SessionSeed` token.
3. `TholdemRuntimeAgent_GivenSource_ExpectedNeverReadsASeedFromContext` — the runtime agent source has
   no `TurnSeed`/`SessionSeed`/`DeriveAgentSeed`, and no `context.<...Seed...>` access.
4. `AgentInputs_GivenRecordedSeed42Hand1Enumeration_ExpectedNoCandidateSeedMatchAndDifferentDeck` —
   the attack replay described above, plus the agent-seed deck difference.
5. `DeriveAgentSeed_GivenObservationAndTurnCoordinates_ExpectedStableTwiceAndChangedForDifferentObservation`
   — same observation + coordinates yields the same agent seed twice and the same action twice; a
   changed observation (one flop board card replaced) yields a different seed.

The old cheat-path fact `TurnSeed_GivenSessionSeed42Hand1_ExpectedDeckAndRecordedCardsDiffer` is
replaced by fact 4 (its subject, the `TurnSeed` value, no longer exists).
`ActAsync_GivenOneHundredObservations_ExpectedDeterministicLegalActions` and the baseline-specific tests
now construct the three-field context; their assertions are unchanged.

### 5.1 Fixtures updated because agent randomness changed

Only fixture **inputs** changed. Every assertion line was kept as-is; nothing was weakened or deleted.

| Test | Fixture | Change | Why |
|---|---|---|---|
| `TholdemViewerTableTests.ReplayTable_GivenHandWithFourCardBoard_…` and `SessionDetail_GivenRecordedHands_…` | `CreateRecordedSession` session seed | `1` → `50` (5 hands, random-legal vs random-legal) | With the new randomness the old seed-1 session has no exactly-four-card board. Seed 50 deals boards `[0,4,3,0,0]`, so the four-card-board assertion and the board≥3 assertion both hold. |
| `TholdemWebViewerTests.LiveMatch_GivenSeed42SixHands_…` | paced live match seed | `42` → `1` (still 6 hands, 40 ms, same agents) | Seed-42 hand 6 is a preflop all-in under the new randomness, so the observed board jumps 0→5 and the `0,3,4,5` growth predicate has no witness. Seed 1 has a six-hand random-legal vs tight-aggressive hand played on all four streets. The delay-0 seed-42 match and its asserted seed/agent values are unchanged. |
| `Learning.TrainingLoopTests.Run_GivenSameOptionsIntoTwoDirectories_…` | "different training seed" | `314160` → `314161` | Seeds 314159 and 314160 now propose the same genome ids (the analysis quantises to the same proposal), so the "a different seed must change at least one artifact id" assertion had no discriminating fixture. Seed 314161 yields `b155b98f,7f16f59a` vs `0ceaa8fd,e1ba56f7`. |
| `Learning.LearningWebIntegrationTests.StrategyAndLedgerEndpoints_…` | promoted-run seed | `909093` → `909092` | Seed 909093's iteration-1 challenger id now equals the first run's iteration-1 challenger (`b155b98f`), so the ledger `UpsertLedger` replaces the entry instead of appending and the expected entry count is not reached. Seed 909092 promotes challenger `4ac1f97b` and appends, so `run.Iterations.Count + 1` holds. |

### 5.2 Documentation examples regenerated

All JSON examples in the five in-scope docs were compared byte-for-byte with output from the rebuilt
CLI. Updated values:

| Document | Example | Old | New |
|---|---|---|---|
| `strategy-artifact-schema.md` | `ledger.json` | `"winRate":0.4791666666666667` | `"winRate":0.5` |
| `strategy-artifact-schema.md` | training evaluation report | `challengerWins:23, incumbentWins:23, ties:2` | `24, 24, 0` |
| `strategy-artifact-schema.md` | CLI evaluation (`--seeds 4`) | `challengerWins:4, incumbentWins:4, ties:0` | `2, 2, 4` |
| `strategy-artifact-schema.md` | seed-777 `session.json` | `handHashes[1] = 6672b578…` | `92aa3db0e586fd3b9b31bb1d45777d507aa8f931b7b912aca1c14964ace547f3` |
| `training-promotion-runbook.md` | iteration 2 / 3 lines | `mean=4.38 lcb=1.22` / `mean=0.00 lcb=-0.83` | `mean=1.67 lcb=-0.76` / `mean=-1.25 lcb=-3.40` |

Unchanged examples verified equal after the fix: seed-777 `sessionId` `tholdem-f275b4ee69be0557`,
hand-1 hash `571049a9…`, `hand-00001.json`, the version-1 strategy artifact, `active-champion.json`,
and the manifest hash for `play --seed 777 --hands 2` (`ab3095f7…`). Agent choices changed (so
`hand-02` and the manifest hash changed); the deals did not.

## 6. Verification (observed output)

All commands were run from the repository root in the harness PowerShell host (Windows PowerShell
5.1.26100.4202). `dotnet test` was not run.

### 6.1 Test project build and supplementary runner

```powershell
dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release
dotnet run --project .tmp/tholdem-verify/RunXunit/RunXunit.csproj -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
```

```text
Build succeeded.
    0 Warning(s)
    0 Error(s)
...
TOTAL 182 PASSED 182 FAILED 0 SKIPPED 0
```

### 6.2 Full solution build

```powershell
dotnet build Wip.slnx --configuration Release
```

```text
    124 Warning(s)
    0 Error(s)
exit: 0
```

(The 124 warnings are the pre-existing solution warnings; the Tholdem test project builds with 0
warnings.)

### 6.3 Architecture runner and project count

`tests/Wip.Architecture.Tests/ArchitectureReferenceTests.cs:16` asserts `Assert.Equal(48, graph.Count)`.

```powershell
dotnet run --project .tmp/tholdem-verify/RunXunit/RunXunit.csproj -c Release -- tests/Wip.Architecture.Tests/bin/Release/net10.0/Wip.Architecture.Tests.dll
```

```text
TOTAL 4 PASSED 4 FAILED 0 SKIPPED 0
exit: 0
```

### 6.4 Solution coverage

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/verify-solution-coverage.ps1
```

```text
solution=Wip.slnx
wip_project_count=48
solution_wip_project_count=48
missing_from_solution=0
solution_paths_not_on_disk=0
solution_coverage=ok
exit: 0
```

### 6.5 HarnessSync

```powershell
dotnet run --project tools/HarnessSync -- --check
```

```text
exit: 0
```

(no output)

### 6.6 Seed-surface scan

Source scan over `WIP/Wip.Tholdem.Agents` (excluding `bin`/`obj`) for
`DeriveTurnSeed|TurnSeed|TurnSeedDomain`:

```text
forbidden-token source matches in WIP/Wip.Tholdem.Agents: 0
```

Executable code of `AgentTurnContext.cs` after comment stripping:

```csharp
namespace Wip.Tholdem.Agents;

public readonly record struct AgentTurnContext(int HandNumber, int Seat, int ActionIndex);
```

Reflection over the compiled `Wip.Tholdem.Agents.dll` (scratch probe `.tmp/round12-reflect`, exit 0):

```text
AgentTurnContext properties: ActionIndex:Int32, HandNumber:Int32, Seat:Int32
seed-valued context members: 0
SeedDerivation public methods: Derive, DeriveAgentSeed
DeriveTurnSeed present: False
DeriveAgentSeed present: True
```

Note: `sessionSeed` still appears as the parameter name of the unchanged engine derivation
`SeedDerivation.Derive(ulong sessionSeed, …)`; it is not part of any agent-facing type and the scan
above targets the turn-seed identifiers and the context shape.

### 6.7 Documentation examples

All seven JSON examples in `strategy-artifact-schema.md` (the version-2 artifact has no example in
that document) compared byte-for-byte with the final CLI output:

```text
session   in doc: True
hand1     in doc: True
v1        in doc: True
ledger    in doc: True
report    in doc: True
cli       in doc: True
pointer   in doc: True
```

Final CLI output used for the comparison:

```text
session: tholdem-f275b4ee69be0557
seed: 777  hands: 2  manifest sha256: ab3095f7472f815bbeaa9a7cdbcba6eee0acd802241dc10f1e4185807fffb8ee
...
iteration=1 champion=tholdem-strategy-v1-f100e793 challenger=tholdem-strategy-v2-b155b98f promoted=false verified=true outcome=rejected meanBbPer100=0.00 lcb=0.00 seeds=24
iteration=2 champion=tholdem-strategy-v1-f100e793 challenger=tholdem-strategy-v2-e1ba56f7 promoted=false verified=true outcome=rejected meanBbPer100=1.67 lcb=-0.76 seeds=24
iteration=3 champion=tholdem-strategy-v1-f100e793 challenger=tholdem-strategy-v2-ea96d95d promoted=false verified=true outcome=rejected meanBbPer100=-1.25 lcb=-3.40 seeds=24
final champion: tholdem-strategy-v1-f100e793
...
decision: lcb-below-margin
verifier: verified (rejection-justified)
```

### 6.8 Waiver register

`docs/plan/test-waivers.json` `expectedTotals["Wip.Tholdem.Tests"]`: `179` → `182` (4 new facts,
1 replaced). No failure/knownAborted entry was added.

## 7. Mutation: re-add a `TurnSeed` property

Original file hash (`Get-FileHash -Algorithm SHA256`):

```text
ORIGINAL SHA256: 1E4EB31448DC96D9E526C2731C24787BA23F228F42BAB3DA6235B44619CF2F07
```

Temporary mutation, appended to the record struct:

```csharp
public readonly record struct AgentTurnContext(int HandNumber, int Seat, int ActionIndex)
{
    // MUTATION (round 12): reintroduced seed-valued member to prove the boundary test bites.
    public ulong TurnSeed { get; init; }
}
```

```text
MUTANT SHA256: F3952F8BE622E5815B155515DD9489505614475F9B6868E1BC9455652D6FE431
```

Rebuild and boundary run:

```text
build exit: 0
turn context         : AgentTurnContext { HandNumber = 1, Seat = 0, ActionIndex = 0, TurnSeed = 0 }
recovered seeds      : none
FAIL AgentInformationBoundaryTests.AgentSources_GivenAgentFiles_ExpectedNoHiddenInformationIdentifiers: TrueException: Agent sources reference hidden information: AgentTurnContext.cs:seed
FAIL AgentInformationBoundaryTests.AgentSources_GivenExecutableCode_ExpectedNoSeedIdentifier: TrueException: Agent executable sources still reference a seed: AgentTurnContext.cs:Seed, AgentTurnContext.cs:TurnSeed
FAIL AgentInformationBoundaryTests.AgentTurnContext_GivenReflection_ExpectedNoSeedValuedMember: TrueException: AgentTurnContext must not expose a seed-valued property; found: TurnSeed:UInt64
TOTAL 7 PASSED 4 FAILED 3 SKIPPED 0
```

(The enumeration test still reports no target because the mutated property defaults to `0`; the
reflection and source boundary tests are the ones that catch a re-added seed member.)

Restore from the byte backup, hash check, forced rebuild and rerun:

```text
RESTORED SHA256: 1E4EB31448DC96D9E526C2731C24787BA23F228F42BAB3DA6235B44619CF2F07
rebuild exit: 0
turn context         : AgentTurnContext { HandNumber = 1, Seat = 0, ActionIndex = 0 }
recovered seeds      : none
recorded deal        : Qs Tc 4s Jd 8h 8s 8d Th 6c
agent-seed deal      : Js Qd Kd 2d 2c 4s 8d 4h 3d
TOTAL 7 PASSED 7 FAILED 0 SKIPPED 0
```

## 8. Files changed

Product (agent-facing surface):

- `WIP/Wip.Tholdem.Agents/AgentTurnContext.cs`
- `WIP/Wip.Tholdem.Agents/SeedDerivation.cs`
- `WIP/Wip.Tholdem.Agents/IPokerAgent.cs`
- `WIP/Wip.Tholdem.Agents/Baselines/RandomLegalAgent.cs`
- `WIP/Wip.Tholdem.Agents/Baselines/TightAggressiveAgent.cs`
- `WIP/Wip.Tholdem.Agents/Baselines/PotOddsEquityAgent.cs`
- `WIP/Wip.Tholdem.Agents/Learning/StrategyAgent.cs`
- `WIP/Wip.Tholdem.Agents/Simulation/MatchSimulator.cs`
- `WIP/Wip.Tholdem.Agents/Runtime/TholdemRuntimeAgent.cs` (comment only; never reads the context)
- `WIP/Wip.Tholdem.Web/Live/LiveMatch.cs` (context construction only)

Tests: `AgentInformationBoundaryTests.cs`, `TightAggressiveAgentTests.cs`,
`PotOddsEquityAgentTests.cs`, `Runtime/RuntimeAgentTestSupport.cs`, `TholdemWebViewerTests.cs`,
`TholdemViewerTableTests.cs`, `Learning/TrainingLoopTests.cs`, `Learning/LearningWebIntegrationTests.cs`.

Docs: `docs/tholdem/{engine-rules,viewer,known-limits,strategy-artifact-schema,training-promotion-runbook}.md`,
`docs/plan/test-waivers.json`, this file.

No engine, history, learning-gate/verifier, CLI, `Wip.slnx`, architecture-test or `.github` file was
modified.

## 9. Not fixed / limits of this round

- **Historical records still name the deleted API.** `docs/goal-tholdem/round-{9,10,11}/*`,
  `docs/goal-tholdem/{journal,evidence}.md` and other frozen round reports describe the old
  `SessionSeed`/`TurnSeed` designs. They are historical evidence and are outside the round-12 SCOPE,
  so they were not rewritten. The five in-scope product docs contain no `TurnSeed`/`DeriveTurnSeed`
  reference.
- **CI not observed.** No GitHub run was triggered or inspected; the local equivalents listed in
  section 6 are the evidence.
- **`dotnet test` not used.** Local VSTest is unusable in this session; the supplementary runner over
  the compiled test assembly is the test evidence, as in the previous rounds.
- **Scratch location exception.** Round-12 logs live under
  `WIP/Wip.Tholdem.Tests/bin/Release/net10.0/round12-evidence/` (git-ignored). A scratch `*.csproj`
  inside `WIP/**/bin` is enumerated by `tools/verify-solution-coverage.ps1` and changed its project
  count to 49, so the small reflection probe was placed in `.tmp/round12-reflect` (git-ignored,
  outside `WIP/`) instead; that directory was the only scratch outside the test output tree.
- **Fixture seeds are behaviour-specific.** The four reseeded test fixtures (section 5.1) were chosen
  against the current observation-derived randomness. A future change to agent behaviour or to the
  observation JSON format can invalidate those fixture inputs again (the assertions themselves are
  not weakened).
