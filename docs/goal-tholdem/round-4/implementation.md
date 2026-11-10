# Round 4 — `Wip.Tholdem.Web`: Bones-style spectator viewer with real-HTTP tests

- **Date**: 2026-11-06
- **Branch**: `WIP`
- **HEAD at verification**: `409b6f9bfd0a727a32cf8c7977f2f2163730b8f0` (other sessions' Bones changes are present in the working tree; this round did not touch any of those files)
- **Scope delivered**: `Wip.Tholdem.Web` (`Sdk.Web`, net10.0) — a loopback-only, offline ASP.NET Core viewer with a seeded live match feed, a read-only recorded-session/replay browser, and read-only strategy/ledger views over the frozen data directory; 10 committed tests that drive the real Kestrel surface; solution/architecture/waiver integration.
- **Not created**: `Wip.Tholdem.Host`. No file under `WIP/Wip.Tholdem/**` or `WIP/Wip.Tholdem.Agents/**` (source) was changed. No `WIP/Wip.Bones*`, `WIP/Wip.Shell*`, `WIP/Wip.Runtime*` file, and no `tools/run-wip-tests.ps1`, was changed by this round.

## Files created

| Path | Purpose |
|---|---|
| `WIP/Wip.Tholdem.Web/Wip.Tholdem.Web.csproj` | `Microsoft.NET.Sdk.Web`, net10.0, ImplicitUsings/Nullable, ProjectReferences to `Wip.Tholdem` + `Wip.Tholdem.Agents`; `wwwroot/viewer/**` copied beside the output assembly |
| `WIP/Wip.Tholdem.Web/Program.cs` | `TholdemWebHost.Create(args).Run(); public partial class Program;` |
| `WIP/Wip.Tholdem.Web/TholdemViewerOptions.cs` | `sealed record TholdemViewerOptions(string DataDirectory, int DefaultDelayMs = 250, int MaxLiveHands = 500)` |
| `WIP/Wip.Tholdem.Web/TholdemWebHost.cs` | `AddTholdemViewerServices`, `MapLiveRoutes`, `MapReplayRoutes`, `MapStrategyRoutes`, `MapStaticViewer`, `Create`, `JsonOptions` (camelCase + `JsonStringEnumConverter(JsonNamingPolicy.CamelCase)`), `--data-dir` / `--urls` handling, viewer-asset resolution |
| `WIP/Wip.Tholdem.Web/ViewerStatus.cs` | `GET /api/status` DTO (`mode, dataDirectory, sessions, strategies, liveMatches, offline`) |
| `WIP/Wip.Tholdem.Web/Live/SpectatorSnapshot.cs` | `SpectatorSnapshot`, `SpectatorAction`, `StartLiveMatchRequest`, `LiveMatchStarted` |
| `WIP/Wip.Tholdem.Web/Live/LiveAgentCatalog.cs` | the three contracted agent names; unknown name returns false (HTTP 400) |
| `WIP/Wip.Tholdem.Web/Live/LiveMatch.cs` | paced live worker using the simulator's exact hand-seed/button/turn-context rules; pause/resume/stop; 200-entry action tail; hand hashes |
| `WIP/Wip.Tholdem.Web/Live/LiveMatchRegistry.cs` | singleton registry (`Start`, `TryGet`, `Stop`), deterministic 16-hex id, disposal stops every match |
| `WIP/Wip.Tholdem.Web/Recording/SessionCatalog.cs` | read-only scan of `<data>/sessions/*/session.json` + `hand-*.json`; tolerant parsing with `skipped` |
| `WIP/Wip.Tholdem.Web/Recording/ReplayTableRenderer.cs` | server-rendered HTML replay table (hand number, hole cards, board, pot, winners, showdown, street-named action rows) |
| `WIP/Wip.Tholdem.Web/Strategies/StrategyCatalog.cs` | read-only strategies/ledger/champion pass-through with `skipped` counts |
| `WIP/Wip.Tholdem.Web/wwwroot/viewer/index.html` | static shell with all 22 contracted stable ids, relative `viewer.css` / `app.js` references |
| `WIP/Wip.Tholdem.Web/wwwroot/viewer/viewer.css` | dark slate palette (`#0f172a` body, `#0b1220` sidebar, `#334155` borders, `#e2e8f0` text, 22rem sidebar), rounded card badges, highlighted active seat, prominent pot |
| `WIP/Wip.Tholdem.Web/wwwroot/viewer/app.js` | polls `/api/live/{id}`, renders card badges, keeps the log scrolled to the newest action, lists sessions and loads `/viewer/table?...` into the replay panel, loads strategies/ledger; same-origin paths only |
| `WIP/Wip.Tholdem.Tests/ViewerTestSupport.cs` | in-process Kestrel harness (binds `http://127.0.0.1:0` via `TholdemWebHost.Create`, reads the bound address from `IServerAddressesFeature`, disposes in `finally`), JSON/polling helpers, scratch dirs under the compiled test output |
| `WIP/Wip.Tholdem.Tests/TholdemWebViewerTests.cs` | the 10 new facts (static viewer, live feed, hash cross-check, determinism, controls, replay, strategies/ledger, offline assets, spectator boundary, status) |
| `docs/goal-tholdem/round-4/implementation.md` | this report |

## Files modified

| Path | Change |
|---|---|
| `WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj` | added `<ProjectReference Include="..\Wip.Tholdem.Web\Wip.Tholdem.Web.csproj" />` and `<FrameworkReference Include="Microsoft.AspNetCore.App" />` |
| `Wip.slnx` | added `<Project Path="WIP/Wip.Tholdem.Web/Wip.Tholdem.Web.csproj" />` inside the existing `<Folder Name="/WIP/Tholdem/">` |
| `tests/Wip.Architecture.Tests/ArchitectureReferenceTests.cs` | `Assert.Equal(46, graph.Count)` → `47` |
| `tests/Wip.Architecture.Tests/ArchitectureRules.cs` | added `tholdem-web-no-bones-or-hosts` (`Wip.Tholdem.Web` must not reach `Wip.Bones`, `Wip.Bones.*`, `Wip.Shell`, `Wip.Shell.*`, `Wip.ShellHost`, `Wip.Tholdem.Host`) after the Agents rule |
| `docs/plan/test-waivers.json` | `expectedTotals["Wip.Tholdem.Tests"]` 76 → 86 |

## Implementation notes

### Host and options

- `TholdemWebHost.Create` reads `--data-dir <path>` (also `--data-dir=<path>`), defaults to `<contentRoot>/artifacts/tholdem`, creates the directory, registers the options + `LiveMatchRegistry` singletons, and binds `http://127.0.0.1:5099` only when no `--urls`/`--urls=` argument and no `ASPNETCORE_URLS` are present.
- `MapStaticViewer` uses `UseDefaultFiles` + `UseStaticFiles` with a physical `wwwroot/viewer` provider under `/viewer`, then `MapGet("/", () => Results.Redirect("/viewer/index.html"))`.
- The viewer directory is resolved in a fixed order: `contentRoot/wwwroot/viewer`, process base directory, the directory of `Wip.Tholdem.Web.dll`, then the repository-relative `WIP/Wip.Tholdem.Web/wwwroot/viewer` found by walking up to `Wip.slnx`. In-process Kestrel tests have a different content root than `dotnet run`, so the fixed order keeps both entry points working without a `--content-root` argument. The project also copies `wwwroot/viewer/**` to its output (and therefore into the referencing test output).

### Live match feed

- `POST /api/live` validates `hands` 1..`MaxLiveHands`, `delayMs` 0..5000 (default `DefaultDelayMs`), and both agent names against `random-legal` / `tight-aggressive` / `pot-odds-equity`; anything else is 400. The 201 body is exactly `{ id, seed, hands, seat0Agent, seat1Agent, delayMs }` with `id` a 16-character lowercase hex string.
- Ids are `SHA-256(seed \n hands \n delay \n seat0 \n seat1 \n registry-sequence)` truncated to 16 hex characters: no clock, `System.Random` or GUID. Two identical requests in one registry get different ids (sequence), and the same request sequence produces the same id run-to-run.
- The worker reproduces `MatchSimulator` exactly for hand `n`: `handSeed = SeedDerivation.Derive(Seed, n, 0, 0, 0UL)`, engine `handNumber = n`, `buttonSeat = (n - 1) % 2`, fresh starting stack/blinds from the same `SimulationOptions` defaults (1000/5/10), and `AgentTurnContext(Seed, n, seat, actionsThatSeatHasTakenThisHand)`. The agent is given only `PlayerObservation.ForSeat`.
- `delayMs` is used only by `Task.Delay` between published states (0 uses `Task.Yield`); it never participates in a decision. `HandRecord.From(finalState).Hash` produces one lowercase SHA-256 per completed hand.
- The snapshot holds `id, status, handNumber, street, board, pot, stacks, holeCardsSeat0/1, buttonSeat, isComplete, showdownReached, winners, handHashes, actions`; `actions` is the tail of at most 200 `{ handNumber, street, seat, kind, amount, potAfter }` entries. Both seats' hole cards are read through the public `PlayerObservation.ForSeat(state, seat).OwnHoleCards` projection — no internals access and no change to the domain project.
- Delete sets `stopped` and cancels the worker; pause sets `paused` and the worker only advances after resume; the worker publishes `complete` after the final hand unless it was stopped. A paused snapshot is frozen (the paced worker waits in 5 ms slices).

### Replay, strategies, ledger

- `GET /api/sessions` scans `<data>/sessions/*/session.json`, skips malformed manifests (reported as `skipped`), computes `manifestHash` as the lowercase SHA-256 of the manifest bytes, `handCount` from the `hand-*.json` files, and sorts by `sessionId`.
- `GET /api/sessions/{id}` matches the directory name or the manifest `sessionId` and returns `schema, sessionId, seed, startingStack, smallBlind, bigBlind, agents, handHashes, manifestHash, handCount` plus `hands: [{ handNumber, board, winners, showdown, potAwarded, finalStacks }]`. Note the deliberate naming: the manifest's numeric `hands` becomes `handCount`, because the contracted `hands` key is the summary array (a literal "manifest plus hands" would collide).
- `GET /viewer/table?session=…&hand=…` returns `text/html` with `data-hand-number`, both seats' cards, board cards, `Pot awarded: N`, `Showdown: yes|no`, `Winners: Seat N…`, and `<tr class="action-row" data-street data-seat data-kind data-amount data-pot-after>` rows whose street cells use `Preflop`/`Flop`/`Turn`/`River`. Missing session, missing hand or `hand < 1` is 404.
- Strategy files are parsed as JSON objects (non-objects/malformed JSON increment `skipped`) and returned as `{ id, file, payload }` sorted by id. `ledger.json` non-object array elements increment `skipped`; a missing ledger is `{ entries: [], skipped: 0 }`. `active-champion.json` is passed through or 404. `GET /api/status` reports `mode: "spectator"`, the configured absolute data directory, valid session/strategy counts, the registry match count, and `offline: true`.
- Nothing under the data directory is written by any endpoint.

### Viewer assets

- `index.html` is a static shell with all stable ids (`status`, `live-form`, `seed`, `hands`, `delay`, `seat0-agent`, `seat1-agent`, `start-live`, `pause-live`, `resume-live`, `stop-live`, `live-table`, `board`, `pot`, `action-log`, `sessions`, `replay-panel`, `replay-hand`, `replay-scrubber`, `strategies`, `strategy-detail`, `ledger`), two seat panels (`data-seat="0|1"` with name/stack/hole-card slots), the board row, pot, live action log, session list, replay scrubber, strategy list/detail and ledger table.
- `app.js` only uses same-origin root-relative paths (`/api/...`, `/viewer/table?...`); it polls only while the match is `running` or `paused`, stops after `complete`/`stopped`, rebuilds the action log and pins it to the newest entry, highlights the seat that acted last, renders `As`/`Td`-style text badges with red hearts/diamonds, and loads server-rendered replay HTML with `textContent`-safe data handling.
- No `http://`, `https://`, `//cdn` or `@import url(http` appears in any viewer file; the only two asset references in `index.html` are the relative `viewer.css` and `app.js`.

## Deliberate implementation decisions

1. **Live determinism test strength.** The first committed version compared only `handHashes` across seeds. A constant live hand seed still lets each match's hashes differ because `AgentTurnContext` embeds the session seed, so that assertion alone did not kill the required mutant. The test now also compares the final hand's four hole cards (in seat order) between seed 42 and seed 43 and requires them to differ. This is the "deal fingerprint" that a constant `0UL` derivation cannot fake; the fact count is unchanged (10 new facts).
2. **Board growth observation.** A `delayMs: 0` match finishes before a second HTTP GET can observe intermediate streets, so the growth clause is asserted on a second identical live match with `delayMs: 40` while the required delay-0 match still covers the 201 body, completion, six hashes, hole cards and action-log content. `delayMs` only paces progress, as the packet requires.
3. **Hole-card access.** `TableState.HoleCardsSeat0/1` are internal to `Wip.Tholdem`, and that project is out of scope, so the spectator snapshot reads both seats through the public `PlayerObservation.ForSeat(...).OwnHoleCards` projection. No InternalsVisibleTo, reflection or domain edit was added.
4. **Enum casing.** `JsonStringEnumConverter(JsonNamingPolicy.CamelCase)` (mirroring `BonesWebHost`) serializes the snapshot `street` and each action `street` as `preflop`/`flop`/`turn`/`river`; the table renderer displays the human `Preflop`/`Flop`/… names. The committed tests assert the camelCase JSON form and the human table form.
5. **Static asset discovery.** The viewer directory search order in decision "Host and options" avoids depending on the test process's content root; both the test process and `dotnet run` serve the same physical files.
6. **Test scratch location.** Scratch data directories live under the compiled test assembly directory (`viewer-test-temp/<prefix>-<guid>`) and are deleted in `finally`; the real-server verification uses `.tmp/goal-round4/...`. `%TEMP%` is never used. The only GUID in the new code is the test-only temp-directory suffix, which the dispatch allows.

## Test count

**86 test cases**: 76 pre-existing + 10 new `[Fact]` methods, 0 `[Theory]`, 0 skips.

| Suite | Facts |
|---|---|
| Existing Tholdem engine/agents/history suites (rounds 1–3) | 76 |
| `TholdemWebViewerTests.Viewer_GivenStaticHost_ExpectedRedirectShellStableIdsAndAssets` | 1 |
| `TholdemWebViewerTests.LiveMatch_GivenSeed42SixHands_ExpectedSpectatorCompletionHashesAndBoardGrowth` | 1 |
| `TholdemWebViewerTests.LiveMatchHashes_GivenSameSeedAgentsAndHands_ExpectedMatchSimulatorHistoryBytes` | 1 |
| `TholdemWebViewerTests.LiveMatchHashes_GivenSameInputs_ExpectedIdenticalAcrossRunsAndDifferentSeedDiverges` | 1 |
| `TholdemWebViewerTests.LiveMatchControls_GivenDeletePauseResumeAndInvalidRequests_ExpectedDocumentedStatuses` | 1 |
| `TholdemWebViewerTests.RecordedSessions_GivenSimulatedHistory_ExpectedListingDetailReplayAndMissingHand404` | 1 |
| `TholdemWebViewerTests.StrategyAndLedgerEndpoints_GivenEmptyThenPopulatedDataDirectory_ExpectedSortedPassthroughAndChampion` | 1 |
| `TholdemWebViewerTests.ViewerAssets_GivenOfflineViewerDirectory_ExpectedNoExternalReferencesAndRelativeIndexAssets` | 1 |
| `TholdemWebViewerTests.AgentSources_GivenSpectatorBoundaryScan_ExpectedNoWebOrLiveIdentifiers` | 1 |
| `TholdemWebViewerTests.Status_GivenConfiguredDataDirectory_ExpectedOfflineSpectatorReport` | 1 |
| **Total** | **86** |

`docs/plan/test-waivers.json` parses and `expectedTotals["Wip.Tholdem.Tests"] == 86`.

## Test-first evidence (red)

Tests were written against a signature-only host whose route handlers temporarily threw `NotImplementedException` and whose static middleware was not yet wired. The red run command:

```text
dotnet run --project .tmp/tholdem-verify/RunXunit/RunXunit.csproj -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
```

First attempt (scratch-runner limitation, not a test result): the supplementary runner had no ASP.NET Core shared framework in its runtime config, so the ASP.NET-dependent facts failed to load:

```text
FAIL TholdemWebViewerTests.LiveMatchControls_GivenDeletePauseResumeAndInvalidRequests_ExpectedDocumentedStatuses: FileNotFoundException: Could not load file or assembly 'Microsoft.AspNetCore, Version=10.0.0.0, Culture=neutral, PublicKeyToken=adb9793829ddae60'.
TOTAL 86 PASSED 77 FAILED 9 SKIPPED 0
```

`Wip.Tholdem.Tests` correctly has `<FrameworkReference Include="Microsoft.AspNetCore.App" />` (the real VSTest host resolves it); only the supplementary runner needed the same `<FrameworkReference>` added to its gitignored `.tmp` project. After that, the observed red run (complete; exit code `1`):

```text
FAIL TholdemWebViewerTests.LiveMatchControls_GivenDeletePauseResumeAndInvalidRequests_ExpectedDocumentedStatuses: HttpRequestException: An error occurred while sending the request.
FAIL TholdemWebViewerTests.LiveMatchHashes_GivenSameInputs_ExpectedIdenticalAcrossRunsAndDifferentSeedDiverges: HttpRequestException: An error occurred while sending the request.
FAIL TholdemWebViewerTests.LiveMatchHashes_GivenSameSeedAgentsAndHands_ExpectedMatchSimulatorHistoryBytes: HttpRequestException: An error occurred while sending the request.
FAIL TholdemWebViewerTests.LiveMatch_GivenSeed42SixHands_ExpectedSpectatorCompletionHashesAndBoardGrowth: HttpRequestException: An error occurred while sending the request.
FAIL TholdemWebViewerTests.RecordedSessions_GivenSimulatedHistory_ExpectedListingDetailReplayAndMissingHand404: HttpRequestException: An error occurred while sending the request.
FAIL TholdemWebViewerTests.Status_GivenConfiguredDataDirectory_ExpectedOfflineSpectatorReport: HttpRequestException: An error occurred while sending the request.
FAIL TholdemWebViewerTests.StrategyAndLedgerEndpoints_GivenEmptyThenPopulatedDataDirectory_ExpectedSortedPassthroughAndChampion: HttpRequestException: An error occurred while sending the request.
FAIL TholdemWebViewerTests.ViewerAssets_GivenOfflineViewerDirectory_ExpectedNoExternalReferencesAndRelativeIndexAssets: EqualException: Assert.Equal() Failure: Values differ
Expected: 3
Actual:   0
FAIL TholdemWebViewerTests.Viewer_GivenStaticHost_ExpectedRedirectShellStableIdsAndAssets: EqualException: Assert.Equal() Failure: Values differ
Expected: OK
Actual:   NotFound
TOTAL 86 PASSED 77 FAILED 9 SKIPPED 0
```

9 of the 10 new facts failed (the temporary handlers aborted the connection through Kestrel's default error path, `/viewer/index.html` was 404 and the viewer directory did not yet exist). The 10th (`AgentSources_GivenSpectatorBoundaryScan_…`) reads only agent sources and passed during red; it remains a real assertion that no agent file mentions `SpectatorSnapshot`, `TholdemWebHost` or `LiveMatch`. Every temporary `NotImplementedException` was removed before the green phase; a final scan of `WIP/Wip.Tholdem.Web/**/*.cs` finds `0` occurrences of `NotImplementedException`, `TODO`, `System.Random` or `Guid.NewGuid`.

## Mutation experiment

Required mutant: the live hand-seed derivation in `WIP/Wip.Tholdem.Web/Live/LiveMatch.cs` line 140 was replaced with a constant.

- Original file SHA-256: `D603061E2C26C9B9609E06775606339685ABA253BF819DD996F7B780E0169645` (7267 bytes)
- Mutant SHA-256: `20ABB745D5CD7B92D30E0F9C5B7DE027F1609C0F3CE09595BE6DBD3629B67EC7`
- Mutant line: `var handSeed = 0UL;` (was `var handSeed = SeedDerivation.Derive(Seed, hand, 0, 0, 0UL);`)
- Forced rebuild: `dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release --no-incremental` → `0 Warning(s) 0 Error(s)`

First mutant run (before the determinism test was strengthened) observed `TOTAL 86 PASSED 84 FAILED 2`:

```text
FAIL TholdemWebViewerTests.LiveMatchHashes_GivenSameSeedAgentsAndHands_ExpectedMatchSimulatorHistoryBytes: EqualException: Assert.Equal() Failure: Strings differ
FAIL TholdemWebViewerTests.LiveMatch_GivenSeed42SixHands_ExpectedSpectatorCompletionHashesAndBoardGrowth: ContainsException: Assert.Contains() Failure: Filter not matched in collection
TOTAL 86 PASSED 84 FAILED 2 SKIPPED 0
```

The determinism fact still passed because agent choices derive from the *session* seed, so hashes differ even when every deal is identical. The fact was strengthened with the final-deal hole-card fingerprint described above (still 10 new facts), the mutant was rebuilt and re-run. Observed (exit code `1`):

```text
FAIL TholdemWebViewerTests.LiveMatchHashes_GivenSameInputs_ExpectedIdenticalAcrossRunsAndDifferentSeedDiverges: NotEqualException: Assert.NotEqual() Failure: Collections are equal
FAIL TholdemWebViewerTests.LiveMatchHashes_GivenSameSeedAgentsAndHands_ExpectedMatchSimulatorHistoryBytes: EqualException: Assert.Equal() Failure: Strings differ
FAIL TholdemWebViewerTests.LiveMatch_GivenSeed42SixHands_ExpectedSpectatorCompletionHashesAndBoardGrowth: ContainsException: Assert.Contains() Failure: Filter not matched in collection
TOTAL 86 PASSED 83 FAILED 3 SKIPPED 0
```

Both required tests fail under the mutant: the live-vs-simulator byte-hash cross-check and the live determinism/deal test (plus the board-growth assertion because the constant deal never reaches the river in the paced run).

- Restore: copied the byte-identical pre-mutation backup back.
  - `RESTORED_SHA256=D603061E2C26C9B9609E06775606339685ABA253BF819DD996F7B780E0169645`
  - `RESTORED_MATCHES_ORIGINAL=True`
  - `MUTANT_LINE_ABSENT=True`
  - restored line: `var handSeed = SeedDerivation.Derive(Seed, hand, 0, 0, 0UL);`
- Forced rebuild: `0 Warning(s) 0 Error(s)`.
- Restored green run: `TOTAL 86 PASSED 86 FAILED 0 SKIPPED 0`, exit code `0`.

No mutated file remains in the repository; the production file hash was re-checked at the end of the round as `D603061E2C26C9B9609E06775606339685ABA253BF819DD996F7B780E0169645`.

## Verification (final observed runs)

### 1. Full solution build

Command:

```text
dotnet build Wip.slnx --configuration Release
```

Final observed output (exit code `0`):

```text
    126 Warning(s)
    0 Error(s)

Time Elapsed 00:01:40.33
```

All 126 warnings are pre-existing warnings in other WIP projects (for example xUnit analyzer warnings in `Wip.Bones.Web.Tests` and nullable warnings in `Wip.Shell.E2E.Tests`); the Tholdem projects build with `0 Warning(s), 0 Error(s)`.

### 2. Tholdem suite

Command:

```text
dotnet run --project .tmp/tholdem-verify/RunXunit/RunXunit.csproj -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
```

Observed: `TOTAL 86 PASSED 86 FAILED 0 SKIPPED 0`, exit code `0`.

### 3. Architecture suite

Command:

```text
dotnet run --project .tmp/tholdem-verify/RunXunit/RunXunit.csproj -c Release -- tests/Wip.Architecture.Tests/bin/Release/net10.0/Wip.Architecture.Tests.dll
```

Observed: `TOTAL 4 PASSED 4 FAILED 0 SKIPPED 0`, exit code `0`. The project-graph assertion is `Assert.Equal(47, graph.Count)` and the `tholdem-web-no-bones-or-hosts` rule is present.

### 4. Solution coverage

Command:

```text
powershell -NoProfile -ExecutionPolicy Bypass -File tools/verify-solution-coverage.ps1
```

Observed output (exit code `0`):

```text
solution=Wip.slnx
wip_project_count=47
solution_wip_project_count=47
missing_from_solution=0
solution_paths_not_on_disk=0
solution_coverage=ok
```

### 5. HarnessSync check

Command:

```text
dotnet run --project tools/HarnessSync -- --check
```

Observed: no stdout, exit code `0`.

### 6. Real entry point and HTTP surface

Started with:

```text
dotnet run --project WIP/Wip.Tholdem.Web -- --urls http://127.0.0.1:5188 --data-dir C:\Users\ricar\dev\Modus\.tmp\goal-round4\live-data-final
```

then driven with `Invoke-WebRequest`; the process was terminated with `taskkill /PID <pid> /T /F` and the final line proves no process remained. Observed:

```text
GET /viewer/index.html -> 200 text/html bytes=4558
GET /api/status -> 200 body={"mode":"spectator","dataDirectory":"C:\\Users\\ricar\\dev\\Modus\\.tmp\\goal-round4\\live-data-final","sessions":0,"strategies":0,"liveMatches":0,"offline":true}
POST /api/live -> 201 body={"id":"ad8353a0a207e73f","seed":42,"hands":6,"delayMs":0,"seat0Agent":"random-legal","seat1Agent":"tight-aggressive"}
GET /api/live/ad8353a0a207e73f -> 200 status=complete handNumber=6 hashes=6 board=3
server_process_stopped=True
```

The id is byte-for-byte identical to the earlier run of the same first request, which is additional evidence that no clock, random source or GUID participates in match identity.

### 7. Source hygiene

- 15 files under `WIP/Wip.Tholdem.Web` (source + viewer assets): `BAD_ENCODING=0` (no BOM, no UTF-16).
- 22 `.cs` files under `WIP/Wip.Tholdem.Tests` (all rounds): `BAD_BOM=0`.
- `WIP/Wip.Tholdem.Web/**/*.cs`: `0` occurrences of `NotImplementedException`, `TODO`, `System.Random` or `Guid.NewGuid`.
- The only `Guid.NewGuid` in the new test files is the test-only temp-directory suffix in `ViewerTestSupport.cs`.
- No agent source file mentions `SpectatorSnapshot`, `TholdemWebHost` or `LiveMatch` (asserted by a committed fact).

## Anything that could not be made to pass

Nothing required by the dispatch is failing. The following are deliberate, disclosed deviations or environment notes rather than failures:

1. **Board growth is observed on a second paced match, not the delay-0 match.** With `delayMs: 0` the six-hand match completes before a second HTTP request can observe intermediate streets, so the growth clause (`0 → 3 → 4 → 5` for one hand) is collected from an otherwise identical match with `delayMs: 40`. The delay-0 match still asserts the 201 body, completion, six `handHashes`, both seats' hole cards and the action-log fields. No test is skipped or weakened; the growth assertion remains concrete and literal.
2. **`GET /api/sessions/{id}` field naming.** The manifest's numeric `hands` and the contracted `hands` summary array cannot both use the same key. The response exposes the summaries as `hands` (as contracted) and the manifest count as `handCount`; all other manifest fields (`schema`, `sessionId`, `seed`, `startingStack`, `smallBlind`, `bigBlind`, `agents`, `handHashes`, `manifestHash`) are present.
3. **Scratch runner change outside the deliverable.** `.tmp/tholdem-verify/RunXunit/RunXunit.csproj` (gitignored, not a tracked repository file) gained `<FrameworkReference Include="Microsoft.AspNetCore.App" />` so the supplementary runner can load the ASP.NET-dependent test assembly; without it the runner reports `FileNotFoundException` for `Microsoft.AspNetCore`. The committed test project's own `<FrameworkReference>` is the real configuration and works with VSTest.
4. **`dotnet test` was not run** (the local VSTest testhost aborts with `Win32Exception (5)`; the dispatch forbids it). The compiled assembly was executed with the supplementary runner instead, and CI remains the authoritative gate.
5. **Pre-existing working-tree changes.** `git status` shows unrelated modified/untracked Bones/Shell/Runtime files from other sessions (including `tools/run-wip-tests.ps1`); they predate this round and were not touched. The full solution build is green with them present.

No stub, `TODO`, `NotImplementedException`, `System.Random`, clock or GUID exists in the delivered server code; no test was skipped, weakened or deleted; no file outside the stated scope was edited by this round apart from the gitignored supplementary runner noted above.
