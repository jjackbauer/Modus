# Round 10 — close the `AgentTurnContext` session-seed leak

- **Repository**: `C:\Users\ricar\dev\Modus`, branch `WIP`. The Tholdem tree is untracked:
  `git status --porcelain -- WIP/Wip.Tholdem.Agents WIP/Wip.Tholdem.Tests WIP/Wip.Tholdem.Web docs/tholdem`
  prints `??` for each directory and `git log --all -- <same paths>` prints nothing. The suite
  described here is the suite in this working tree, not yet committed.
- **Defect closed**: every `IPokerAgent` received the session seed in
  `AgentTurnContext.SessionSeed`; deriving `SeedDerivation.Derive(42, 1, 0, 0, 0)` and shuffling
  `SeededDeck.Create(...)` reproduced the recorded hole cards and board exactly.
- **Outcome**: the context now carries only an opaque `TurnSeed` (one-way SHA-256), no agent source
  contains `SessionSeed`, the new tests fail if the leak is reintroduced, and all verification gates
  below are green. `dotnet test` was **not** run (documented local VSTest limitation); the
  supplementary `RunXunit` runner over the compiled test assembly is the executed evidence.

## 1. Required design, as implemented

### 1.1 `AgentTurnContext`

`WIP/Wip.Tholdem.Agents/AgentTurnContext.cs:9`:

```csharp
public readonly record struct AgentTurnContext(ulong TurnSeed, int HandNumber, int Seat, int ActionIndex);
```

The doc comment (`:3-8`) states that `TurnSeed` is an opaque one-way SHA-256 derivation of the
session-scoped input and turn identity, that it cannot be inverted to recover the session seed or the
deck, and that it never exposes hidden cards. There is no `SessionSeed` member.

### 1.2 `SeedDerivation.DeriveTurnSeed`

`WIP/Wip.Tholdem.Agents/SeedDerivation.cs:44-56` adds:

```csharp
public static ulong DeriveTurnSeed(
    ulong sessionSeed,
    int handNumber,
    int seat,
    int actionIndex,
    ulong stream)
{
    var material = string.Create(
        CultureInfo.InvariantCulture,
        $"{TurnSeedDomain}|{sessionSeed}|{handNumber}|{seat}|{actionIndex}|{stream}");
    var digest = SHA256.HashData(Utf8.GetBytes(material));
    return BinaryPrimitives.ReadUInt64BigEndian(digest);
}
```

`TurnSeedDomain = "tholdem-turn-seed"` (`:18`) and the UTF-8 encoding emits no BOM. `Derive`
(`:22-38`) is byte-for-byte unchanged; `SeedDerivationTests.Derive_GivenPinnedAcesContext_ExpectedExactSplitmix64Literal`
still pins `Derive(42, 1, 0, 0, 0) == 13149409904026330690`, and the recorded hands still replay.
The SDK's SHA-256 is a one-way function, so the derivation is not invertible.

### 1.3 Construction sites

`WIP/Wip.Tholdem.Agents/Simulation/MatchSimulator.cs:68-77` and
`WIP/Wip.Tholdem.Web/Live/LiveMatch.cs:222-226` both build:

```csharp
new AgentTurnContext(
    SeedDerivation.DeriveTurnSeed(sessionSeed, hand, seat, actionIndex, 0UL),
    hand,
    seat,
    actionIndex)
```

(`options.Seed` / `Seed` respectively; `actionCounts[seat]` is the per-seat action index.)

### 1.4 Agent randomness

Every baseline and `StrategyAgent` now derives its random stream from `context.TurnSeed`:

- `Baselines/RandomLegalAgent.cs:34` and `:46`
- `Baselines/TightAggressiveAgent.cs:86`
- `Baselines/PotOddsEquityAgent.cs:50`
- `Learning/StrategyAgent.cs:97`

`IPokerAgent.cs:6-11` documents that `TurnSeed` is opaque. No agent reads a session-seed member
because none exists.

### 1.5 Tests

`WIP/Wip.Tholdem.Tests/AgentInformationBoundaryTests.cs`:

- `AgentSources_GivenAgentFiles_ExpectedNoHiddenInformationIdentifiers` (`:56-91`) now scans
  `IPokerAgent.cs`, `AgentTurnContext.cs`, `Baselines/*.cs` and `Learning/StrategyAgent.cs`
  (`AgentTestSupport.AgentSourceFiles`, `:141-156`) for the unbounded string `SessionSeed` and fails
  on any occurrence. The pre-existing word-boundary identifier regex remains; because the required
  doc comment necessarily uses the words "session seed" and "deck", the regex is applied to the
  source with XML documentation comment lines removed (the unbounded `SessionSeed` check still covers
  every byte of every file, comments included). Ordinary comments and strings remain in the regex.
- `AgentTurnContext_GivenReflection_ExpectedNoSessionSeedMember` (`:94-109`) reflects over
  `AgentTurnContext` and fails if any public property name contains `SessionSeed`; it also asserts a
  `TurnSeed` property exists.
- `TurnSeed_GivenSessionSeed42Hand1_ExpectedDeckAndRecordedCardsDiffer` (`:111-150`) runs
  `MatchSimulator` for session seed 42, hand 1 with a `ContextCapturingAgent`, takes the real context
  handed to the agent, derives `SeedDerivation.Derive(context.TurnSeed, context.HandNumber, 0, 0, 0)`
  and asserts (a) the engine deck prefix equals the recorded
  `Qs Tc 4s Jd 8h 8s 8d Th 6c`, (b) `context.TurnSeed != SeedDerivation.Derive(42,1,0,0,0)`, and
  (c) the cards derived from the context differ from the recorded cards. It prints both deals.
- The determinism fact now builds its context with `DeriveTurnSeed` (`:37-43`).

`WIP/Wip.Tholdem.Tests/Runtime/RuntimeAgentTestSupport.cs:270-282` now builds the context with
`SeedDerivation.DeriveTurnSeed(SessionSeed, observation.HandNumber, observation.ActingSeat, actionIndex, 0)`.
`TightAggressiveAgentTests` and `PotOddsEquityAgentTests` use the same numeric literals as before;
the literals are now valid `TurnSeed` values and their assertions are unchanged.

`docs/plan/test-waivers.json:21` records the new total: `"Wip.Tholdem.Tests": 179` (177 + 2 new
facts); it still has no Tholdem failure entry.

## 2. Test-first red output (before the fix)

The pre-fix test run used the same three assertions. `DeriveTurnSeed` did not exist yet, so the
cheat-path fact built the current-shape context directly (`new AgentTurnContext(42, 1, 0, 0)`) and
read `context.SessionSeed`, mirroring how `MatchSimulator` built it.

```text
$ dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release   # succeeded
$ dotnet run --project .tmp/tholdem-verify/RunXunit/RunXunit.csproj -c Release -- \
    WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll AgentInformationBoundaryTests

recorded deck prefix : Qs Tc 4s Jd 8h 8s 8d Th 6c
context seed prefix  : Qs Tc 4s Jd 8h 8s 8d Th 6c
turn seed            : 42
reconstructed seed   : 13149409904026330690
engine hand seed     : 13149409904026330690
FAIL AgentInformationBoundaryTests.AgentSources_GivenAgentFiles_ExpectedNoHiddenInformationIdentifiers: TrueException: Agent sources reference hidden information: AgentTurnContext.cs:SessionSeed, PotOddsEquityAgent.cs:SessionSeed, RandomLegalAgent.cs:SessionSeed, TightAggressiveAgent.cs:SessionSeed, StrategyAgent.cs:SessionSeed
FAIL AgentInformationBoundaryTests.AgentTurnContext_GivenReflection_ExpectedNoSessionSeedMember: TrueException: AgentTurnContext must not expose the session seed; found: SessionSeed
FAIL AgentInformationBoundaryTests.TurnSeed_GivenSessionSeed42Hand1_ExpectedDeckAndRecordedCardsDiffer: NotEqualException: Assert.NotEqual() Failure: Strings are equal
Expected: Not "Qs Tc 4s Jd 8h 8s 8d Th 6c"
Actual:       "Qs Tc 4s Jd 8h 8s 8d Th 6c"
TOTAL 4 PASSED 1 FAILED 3 SKIPPED 0
```

Raw log: `.tmp/round10/red-boundary.log`. The source scan and reflection facts are red exactly
because `SessionSeed` was present; the cheat-path fact is red because the reconstructed deck equals
the recorded deck.

## 3. Cheat path: before and after

### 3.1 After the fix (final test shape, green run)

```text
$ dotnet run --project .tmp/tholdem-verify/RunXunit/RunXunit.csproj -c Release -- \
    WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll AgentInformationBoundaryTests

recorded deck prefix : Qs Tc 4s Jd 8h 8s 8d Th 6c
context seed prefix  : 6c 3s 6d As Jd 4d 6s 7d Ah
recorded deal        : seat0=Qs,4s seat1=Tc,Jd board=8h 8s 8d Th 6c
replayed deal        : seat0=6c,6d seat1=3s,As board=Jd 4d 6s 7d Ah
turn seed            : 3386938122580612636
reconstructed seed   : 16723055204109422328
engine hand seed     : 13149409904026330690
TOTAL 4 PASSED 4 FAILED 0 SKIPPED 0
```

Raw log: `.tmp/round10/green-boundary-final.log`. The recorded deal is the actual CLI recording for
session seed 42 hand 1 (`.tmp/tholdem-cli/sessions/tholdem-06795fbeacfe528b/hand-00001.json`);
`TurnSeed` now produces a completely different deal prefix, so the context cannot recover the
recorded hole cards or board.

The turn seed was also computed independently of the product code (PowerShell SHA-256 over the
contract string, first eight bytes big-endian):

```text
material=tholdem-turn-seed|42|1|0|0|0
sha256=2f00d24718a5561ca9ca096399621bc525c96346895d629988c6893c0121ebd6
turn_seed=3386938122580612636
```

(Raw log: `.tmp/round10/independent-turn-seed.log`; it matches the test's printed `turn seed`.)

### 3.2 Before the fix (red) and under the mutation

```text
recorded deck prefix : Qs Tc 4s Jd 8h 8s 8d Th 6c
context seed prefix  : Qs Tc 4s Jd 8h 8s 8d Th 6c
recorded deal        : seat0=Qs,4s seat1=Tc,Jd board=8h 8s 8d Th 6c
replayed deal        : seat0=Qs,4s seat1=Tc,Jd board=8h 8s 8d Th 6c
turn seed            : 42
reconstructed seed   : 13149409904026330690
engine hand seed     : 13149409904026330690
```

## 4. Mutation: reintroduce the leak, observe both new tests fail

Backups were taken with SHA-256 before mutating:

```text
WIP/Wip.Tholdem.Agents/AgentTurnContext.cs 271271fb3815a1292a4e51a16f2509715bf2d5d03f1701dfeeaadc0ecca73dc5
WIP/Wip.Tholdem.Agents/Simulation/MatchSimulator.cs 3f89e74eac542738b87ed5072771ea2ab1aa20f7f76a9860ffb8a1449aa4e74f
```

Temporary mutation (both parts, so the reflection fact, the source scan and the cheat-path fact are
exercised):

1. `AgentTurnContext` regained `public ulong SessionSeed => TurnSeed;`.
2. `MatchSimulator` passed `options.Seed` as the first `AgentTurnContext` argument (the pre-fix leak).

```text
$ dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release   # succeeded
$ dotnet run --project .tmp/tholdem-verify/RunXunit/RunXunit.csproj -c Release -- \
    WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll AgentInformationBoundaryTests

recorded deck prefix : Qs Tc 4s Jd 8h 8s 8d Th 6c
context seed prefix  : Qs Tc 4s Jd 8h 8s 8d Th 6c
recorded deal        : seat0=Qs,4s seat1=Tc,Jd board=8h 8s 8d Th 6c
replayed deal        : seat0=Qs,4s seat1=Tc,Jd board=8h 8s 8d Th 6c
turn seed            : 42
reconstructed seed   : 13149409904026330690
engine hand seed     : 13149409904026330690
FAIL AgentInformationBoundaryTests.AgentSources_GivenAgentFiles_ExpectedNoHiddenInformationIdentifiers: TrueException: Agent sources reference hidden information: AgentTurnContext.cs:SessionSeed, AgentTurnContext.cs:seed
FAIL AgentInformationBoundaryTests.AgentTurnContext_GivenReflection_ExpectedNoSessionSeedMember: TrueException: AgentTurnContext must not expose the session seed; found: SessionSeed
FAIL AgentInformationBoundaryTests.TurnSeed_GivenSessionSeed42Hand1_ExpectedDeckAndRecordedCardsDiffer: NotEqualException: Assert.NotEqual() Failure: Strings are equal
Expected: Not "Qs Tc 4s Jd 8h 8s 8d Th 6c"
Actual:       "Qs Tc 4s Jd 8h 8s 8d Th 6c"
TOTAL 4 PASSED 1 FAILED 3 SKIPPED 0
```

Raw log: `.tmp/round10/mutation-red-final.log`.

Restore was byte-verified, then forced (`--no-incremental`) rebuild and re-run:

```text
WIP/Wip.Tholdem.Agents/AgentTurnContext.cs restored_sha256=271271fb3815a1292a4e51a16f2509715bf2d5d03f1701dfeeaadc0ecca73dc5 expected=271271fb3815a1292a4e51a16f2509715bf2d5d03f1701dfeeaadc0ecca73dc5 match=True
WIP/Wip.Tholdem.Agents/Simulation/MatchSimulator.cs restored_sha256=3f89e74eac542738b87ed5072771ea2ab1aa20f7f76a9860ffb8a1449aa4e74f expected=3f89e74eac542738b87ed5072771ea2ab1aa20f7f76a9860ffb8a1449aa4e74f match=True
HASH_VERIFIED=True

$ dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release --no-incremental
    2 Warning(s)
    0 Error(s)

$ dotnet run --project .tmp/tholdem-verify/RunXunit/RunXunit.csproj -c Release -- \
    WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll AgentInformationBoundaryTests

recorded deck prefix : Qs Tc 4s Jd 8h 8s 8d Th 6c
context seed prefix  : 6c 3s 6d As Jd 4d 6s 7d Ah
recorded deal        : seat0=Qs,4s seat1=Tc,Jd board=8h 8s 8d Th 6c
replayed deal        : seat0=6c,6d seat1=3s,As board=Jd 4d 6s 7d Ah
turn seed            : 3386938122580612636
reconstructed seed   : 16723055204109422328
engine hand seed     : 13149409904026330690
TOTAL 4 PASSED 4 FAILED 0 SKIPPED 0
```

Raw logs: `.tmp/round10/restore-verify-final.log`, `.tmp/round10/restore-build-final.log`,
`.tmp/round10/restore-boundary-final.log`. The two warnings are the pre-existing solution-wide
`Wip.Runtime` CA2024/CS8425 set; there are no warnings in the Tholdem projects.

## 5. Seed-derived expectations that legitimately changed

Changing the agents' randomness base changes every simulated decision, so five fixture expectations
moved. None was weakened: each replacement keeps every original assertion and only changes the
deterministic fixture that exercises it (or replaces a hard-coded hand number with a predicate over
the same recorded fields).

| Test | Old fixture | New fixture | Why |
|---|---|---|---|
| `Host/TrainCommandTests.cs` `Run_GivenTwoRunsIntoOneDirectory_...` | second run `--seed 8` | `--seed 9` | Under the new stream both seed 7 and seed 8 propose challenger `tholdem-strategy-v2-b155b98f`; `UpsertLedger` replaces the identical key, so the ledger stayed at 1 entry. Seed 9 proposes `tholdem-strategy-v2-0ceaa8fd`, so the two entries and the distinct-challenger assertion hold. |
| `Learning/LearningWebIntegrationTests.cs` `StrategyAndLedgerEndpoints_...` | promoted run `Seed = 909091` | `Seed = 909093` | Seeds 909091/909092 propose the iteration-1 challenger `tholdem-strategy-v2-0ceaa8fd` again; the ledger upsert kept 2 entries. Seed 909093 proposes `tholdem-strategy-v2-b155b98f`, so the ledger grows 2 → 3 as asserted. |
| `Learning/StrategyAgentTests.cs` `ActAsync_GivenFortySimulatedHands_ExpectedOnlyLegalActions` | `Seed = 20261031` | `Seed = 20261032` | The old stream produced `[Call, Bet, Check, Raise]` and never a fold in 40 hands. The new seed produces 114 turns covering `Fold`, `Check`, `Call` and a bet/raise, so the existing distinct-kind assertions pass without relaxation. |
| `TholdemViewerRemediationTests.cs` `ReplaySettlement_GivenFoldThatAwardsThePot_...` | seed 777, hand 2 | seed 1, first record with a final fold at `potAfter = 0` and `PotAwarded > 0` | Seed 777 no longer deals such a hand (boards `5,5,0,0,5`). Seed 1 hand 1 ends with a fold, `potAfter 0`, `potAwarded 455`. The test now selects the hand by the same predicate it asserts instead of hard-coding hand 2. |
| `TholdemViewerTableTests.cs` `ReplayTable_GivenHandWithFourCardBoard_...` and `SessionDetail_...` | fixture seed 777 | fixture seed 1 | Seed 777 no longer produces a four-card board (boards `5,5,0,0,5`); seed 1 over five hands produces boards `4,5,5,0,4` (hands 1 and 5), keeping the four-card-board fixture. |

Constructor-only updates (no expectation changed): the boundary determinism fact and
`RuntimeAgentTestSupport.Context` now pass `DeriveTurnSeed(...)` as the first argument; the
`TightAggressiveAgentTests`/`PotOddsEquityAgentTests` literals `9001/9002/9101/9102` are now
`TurnSeed` values and all their assertions pass unchanged.

Two verification probes produced the replacement fixtures (scratch, not part of the product):

```text
A seed777 boards=5,5,0,0,5
B four-card seed=1 hands=[1,5] boards=4,5,5,0,4
C fold-settlement seed=1 hand=1 potAwarded=455 actions=6
D strategy seed=20261032 turns=114 kinds=[Call,Check,Raise,Bet,Fold]
E seed7 challenger=tholdem-strategy-v2-b155b98f outcome=rejected
E seed8 challenger=tholdem-strategy-v2-b155b98f sameAsSeed7=True
E seed9 challenger=tholdem-strategy-v2-0ceaa8fd sameAsSeed7=False
F first ids=...v2-0ceaa8fd,...v2-e1ba56f7
F promotedSeed=909091 challenger=...v2-0ceaa8fd newKey=False ledger=2
F promotedSeed=909093 challenger=...v2-b155b98f newKey=True ledger=3
```

Raw log: `.tmp/round10/seed-search.log`.

## 6. Documentation changes (only the files in scope)

| File | Change |
|---|---|
| `docs/tholdem/engine-rules.md` §Observation boundary (`:154-...`) | Replaced "`PlayerObservation` is the only projection an agent receives" with the turn-context fact: `PlayerObservation` plus `AgentTurnContext` (hand number, acting seat, per-seat action index, opaque `TurnSeed`). Documents the exact SHA-256 formula, invariant formatting, stream 0 at both construction sites, "no session-seed field", "the session seed and the deck are never exposed", the unchanged `Derive` hand-seed formula, and the new boundary tests. |
| `docs/tholdem/viewer.md` §Spectator view vs agent surfaces (`:94-104`) | Replaced "Agents receive only `PlayerObservation` … no seed" with the same two-projection fact and the one-way `TurnSeed` derivation. §Live (`:51-62`) and the endpoint table (`:84`) now state that `seat0Agent`/`seat1Agent` are required and a missing/unknown name is a 400 `unknown-agent` (matches `TholdemWebHost.cs:66-74`). Opening release sentence made precise. |
| `docs/tholdem/known-limits.md` (`:3-6`, `:40-46`, `:67-71`) | "No release, publish or deploy is claimed — no package … was produced" replaced with: no publish/signing/deployment is performed or automated; the projects are ordinary packable .NET artifacts; any package produced during review was scratch evidence and was not delivered. All Tholdem "committed suite/tests" phrasing replaced with "the suite in this working tree, not yet committed". |
| `docs/tholdem/README.md` (`:8-10`) | Same precise release wording. |
| `docs/plan/test-waivers.json` (`:21`) | `"Wip.Tholdem.Tests": 177` → `179` (two new facts). JSON parses; no Tholdem failure entries. |

## 7. Verification (exact observed output)

### 7.1 Test project build

```text
$ dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release
  Determining projects to restore...
  All projects are up-to-date for restore.
  Wip.Abstractions -> C:\Users\ricar\dev\Modus\WIP\Wip.Abstractions\bin\Release\net10.0\Wip.Abstractions.dll
  Wip.Tholdem -> C:\Users\ricar\dev\Modus\WIP\Wip.Tholdem\bin\Release\net10.0\Wip.Tholdem.dll
  Wip.Agent.Basic -> C:\Users\ricar\dev\Modus\WIP\Wip.Agent.Basic\bin\Release\net10.0\Wip.Agent.Basic.dll
  Wip.Builder -> C:\Users\ricar\dev\Modus\WIP\Wip.Builder\bin\Release\net10.0\Wip.Builder.dll
  Wip.Workspaces.Git -> C:\Users\ricar\dev\Modus\WIP\Wip.Workspaces.Git\bin\Release\net10.0\Wip.Workspaces.Git.dll
  Wip.Validation.DotNet -> C:\Users\ricar\dev\Modus\WIP\Wip.Validation.DotNet\bin\Release\net10.0\Wip.Validation.DotNet.dll
  Wip.Runtime -> C:\Users\ricar\dev\Modus\WIP\Wip.Runtime\bin\Release\net10.0\Wip.Runtime.dll
  Wip.Tholdem.Agents -> C:\Users\ricar\dev\Modus\WIP\Wip.Tholdem.Agents\bin\Release\net10.0\Wip.Tholdem.Agents.dll
  Wip.Tholdem.Host -> C:\Users\ricar\dev\Modus\WIP\Wip.Tholdem.Host\bin\Release\net10.0\Wip.Tholdem.Host.dll
  Wip.Tholdem.Web -> C:\Users\ricar\dev\Modus\WIP\Wip.Tholdem.Web\bin\Release\net10.0\Wip.Tholdem.Web.dll
  Wip.Tholdem.Tests -> C:\Users\ricar\dev\Modus\WIP\Wip.Tholdem.Tests\bin\Release\net10.0\Wip.Tholdem.Tests.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:19.05
```

Exit code 0. Raw log: `.tmp/round10/final-test-build.log`.

### 7.2 Supplementary runner (full Tholdem suite)

```text
$ dotnet run --project .tmp/tholdem-verify/RunXunit/RunXunit.csproj -c Release -- \
    WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
TOTAL 179 PASSED 179 FAILED 0 SKIPPED 0
```

Exit code 0. The full raw log (including Kestrel console noise from the viewer tests) is
`.tmp/round10/final-suite-full.log`; the summary is `.tmp/round10/final-suite-summary.log`.

### 7.3 Solution build

```text
$ dotnet build Wip.slnx --configuration Release
    124 Warning(s)
    0 Error(s)

Time Elapsed 00:01:09.64
```

Exit code 0. The 124 warnings are the pre-existing solution-wide set (Bones/Runtime); no Tholdem
warning is among them. Raw log: `.tmp/round10/final-solution-build.log`.

### 7.4 Architecture runner

```text
$ dotnet build tests/Wip.Architecture.Tests -c Release
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:07.47

$ dotnet run --project .tmp/tholdem-verify/RunXunit/RunXunit.csproj -c Release -- \
    tests/Wip.Architecture.Tests/bin/Release/net10.0/Wip.Architecture.Tests.dll
TOTAL 4 PASSED 4 FAILED 0 SKIPPED 0
```

The passing `ProjectGraph_GivenWipSolution_ExpectedAllWipProjects` asserts `Assert.Equal(48, graph.Count)`
(`tests/Wip.Architecture.Tests/ArchitectureReferenceTests.cs:16`), i.e. 48 projects. Exit code 0.
Raw log: `.tmp/round10/final-architecture.log`.

### 7.5 Solution coverage

```text
$ powershell -NoProfile -ExecutionPolicy Bypass -File tools/verify-solution-coverage.ps1
solution=Wip.slnx
wip_project_count=48
solution_wip_project_count=48
missing_from_solution=0
solution_paths_not_on_disk=0
solution_coverage=ok
```

Exit code 0. Raw log: `.tmp/round10/final-coverage.log`.

### 7.6 Harness sync

```text
$ dotnet run --project tools/HarnessSync -- --check
```

No stdout; exit code 0. This also validates that the edited/new text files are UTF-8 without BOM.

### 7.7 Docs and agent-source scan

```text
--- exact identifier SessionSeed in WIP/Wip.Tholdem.Agents/**/*.cs (excluding bin/obj), case-sensitive ---
SessionSeed matches: 0
--- stale observation-boundary phrases in docs/tholdem/*.md ---
matches: 0
--- committed-suite phrases in docs/tholdem/*.md ---
matches: 0
--- release/publish/deploy claims in the four in-scope docs ---
viewer.md:6: telemetry. **No publish, signing or deployment is performed or automated by this delivery**; it is
known-limits.md:5: **No publish, signing or deployment is performed or automated by this delivery.** The projects are
README.md:8: **No publish, signing or deployment is performed or automated by this delivery.** The projects are
--- stale runtime-agent comment (out of scope, reported) ---
WIP/Wip.Tholdem.Agents/Runtime/TholdemRuntimeAgent.cs:100: // The turn context carries the session seed; the decision deliberately never reads it. The
```

Raw log: `.tmp/round10/docs-scan.log`. A case-insensitive scan also matches the parameter name
`sessionSeed` inside `SeedDerivation.Derive`/`DeriveTurnSeed`; that is the derivation function used
by the host, not an agent-visible context member, and it is unchanged by design.

### 7.8 CLI checks of the out-of-scope documented examples

`docs/tholdem/strategy-artifact-schema.md` and `docs/tholdem/training-promotion-runbook.md` contain
seed-derived examples. They were not edited (outside the permitted file list), so they are recorded
here as findings:

```text
$ dotnet run --project WIP/Wip.Tholdem.Host -c Release -- play --seed 777 --hands 2 \
    --data-dir .tmp/round10/doc-777

session: tholdem-f275b4ee69be0557
seed: 777  hands: 2  manifest sha256: 32ff5488f5349eab004d58c5226d91e240bc151f89e749fc05b06ca84413975e
...
session.json:
{"schema":"tholdem.session.v1","sessionId":"tholdem-f275b4ee69be0557","seed":777,"hands":2,...,"handHashes":["571049a9883fcc96d5ea0b85771030d63f66fe0169306d6a16f6bb39b7a8019b","6672b578c1daee099ce71b59b1f61ccba7b7a20a394201b9801d11e93dbd3ca4"]}

hand-00001.json:
{"handNumber":1,"seed":9754104818518126180,...,"actions":[{"street":0,"seat":0,"kind":"Fold",...}],...}
```

The hand-1 example (deck + fold) is unchanged; the second `handHashes` entry changed from the
documented `ebc35644…` to `6672b578…`. Also:

```text
$ dotnet run --project WIP/Wip.Tholdem.Host -c Release -- train --iterations 1 --seed 7 \
    --data-dir .tmp/round10/doc-train7
iteration=1 champion=tholdem-strategy-v1-f100e793 challenger=tholdem-strategy-v2-b155b98f promoted=false verified=true outcome=rejected meanBbPer100=0.00 lcb=0.00 seeds=24
final champion: tholdem-strategy-v1-f100e793
```

The runbook's documented line (`meanBbPer100=-19.38 lcb=-59.50`, challenger `…-0ceaa8fd`) no longer
matches. Raw logs: `.tmp/round10/doc-check-play777.log`, `.tmp/round10/doc-check-train7.log`.

## 8. Files changed

Product:

- `WIP/Wip.Tholdem.Agents/AgentTurnContext.cs`
- `WIP/Wip.Tholdem.Agents/SeedDerivation.cs`
- `WIP/Wip.Tholdem.Agents/IPokerAgent.cs` (doc comment)
- `WIP/Wip.Tholdem.Agents/Baselines/RandomLegalAgent.cs`
- `WIP/Wip.Tholdem.Agents/Baselines/TightAggressiveAgent.cs`
- `WIP/Wip.Tholdem.Agents/Baselines/PotOddsEquityAgent.cs`
- `WIP/Wip.Tholdem.Agents/Learning/StrategyAgent.cs`
- `WIP/Wip.Tholdem.Agents/Simulation/MatchSimulator.cs`
- `WIP/Wip.Tholdem.Web/Live/LiveMatch.cs` (context construction only)

Tests:

- `WIP/Wip.Tholdem.Tests/AgentInformationBoundaryTests.cs`
- `WIP/Wip.Tholdem.Tests/AgentTestSupport.cs`
- `WIP/Wip.Tholdem.Tests/Runtime/RuntimeAgentTestSupport.cs`
- `WIP/Wip.Tholdem.Tests/Host/TrainCommandTests.cs`
- `WIP/Wip.Tholdem.Tests/Learning/LearningWebIntegrationTests.cs`
- `WIP/Wip.Tholdem.Tests/Learning/StrategyAgentTests.cs`
- `WIP/Wip.Tholdem.Tests/TholdemViewerRemediationTests.cs`
- `WIP/Wip.Tholdem.Tests/TholdemViewerTableTests.cs`

Docs/config:

- `docs/tholdem/engine-rules.md`, `docs/tholdem/viewer.md`, `docs/tholdem/known-limits.md`,
  `docs/tholdem/README.md`, `docs/plan/test-waivers.json`, this file.

No engine, history, runtime-agent behaviour, learning gate/verifier, CLI, `Wip.slnx`,
architecture-test or `.github` file was changed. The WIP project count remains 48 (coverage output).

## 9. What I could not fix (outside the permitted scope)

1. **`WIP/Wip.Tholdem.Agents/Runtime/TholdemRuntimeAgent.cs:100`** still says
   `// The turn context carries the session seed; the decision deliberately never reads it.` That
   sentence is now false. The runtime agent is explicitly out of scope ("the runtime agent's
   behaviour"), so the comment was left untouched; it should become e.g. "The turn context carries an
   opaque per-turn seed; the decision deliberately never reads it."
2. **`docs/tholdem/strategy-artifact-schema.md`** (out of scope) has stale seed-derived examples:
   `:125` `handHashes[1]` is now `6672b578c1daee099ce71b59b1f61ccba7b7a20a394201b9801d11e93dbd3ca4`,
   and the `train --seed 7` ledger/evaluation examples at `:61`/`:84` now differ (see 7.8). Line 6
   still says "**No release is claimed**", which is not an absolute "no path exists" claim but could
   be aligned with the new precise wording.
3. **`docs/tholdem/training-promotion-runbook.md`** (out of scope): the printed example at `:32-33`
   and the quoted values at `:43` no longer match the seed-7 run (`meanBbPer100=0.00 lcb=0.00`,
   challenger `tholdem-strategy-v2-b155b98f`). The procedure and gate description remain correct;
   only the observed numbers changed with the new RNG stream.

No test was weakened or deleted: the two new facts are additive, and every changed fixture keeps the
original assertions (listed in §5). `dotnet test` was not run, per the documented local limitation;
the supplementary runner over the same compiled test assembly is the executed evidence.

## Correction note (2026-11-06)

This note corrects §3.1 without rewriting the original text above. The sentence "`TurnSeed` now
produces a completely different deal prefix" was imprecise about what is fed to `SeededDeck.Create`.

(a) The cheat-path example as written used `SeedDerivation.Derive(TurnSeed, …)` rather than a direct
`SeededDeck.Create(TurnSeed)`. The test captures the real `AgentTurnContext`, computes
`SeedDerivation.Derive(context.TurnSeed, context.HandNumber, 0, 0, 0)`
(= `16723055204109422328`), and constructs the deck from that derived seed. The printed
`context seed prefix` / `replayed deal` lines (`6c 3s 6d As Jd 4d 6s 7d Ah`) therefore do not
describe a deck created directly from `TurnSeed`.

(b) A direct `SeededDeck.Create(TurnSeed)` with the captured `TurnSeed` `3386938122580612636` yields
`Qh Kc Ts Tc 2h 2c Ks 4d 3h`.

(c) Both constructions differ from the recorded seed-42 hand-1 deal
`Qs Tc 4s Jd 8h 8s 8d Th 6c`, so the test's assertion and the security property still hold; only
the report's wording was inaccurate.
