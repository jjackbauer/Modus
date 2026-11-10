# Round 11 adversarial falsification — Falsifier B

- **Repository**: `C:\Users\ricar\dev\Modus`, branch `WIP`
- **Scope**: claims C3 (hidden information), C5 (offline safety), C6 (test and doc integrity)
- **Method**: current-tree source reading, a fresh `dotnet build Wip.slnx --configuration Release`, the
  documented supplementary runner over the rebuilt `WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll`,
  the Release CLI, the running viewer at `http://127.0.0.1:5099`, PowerShell SHA-256/BigInteger
  calculations, and the existing `TurnProbe` scratch binary for the deck print.
- **Not used**: `dotnet test` (documented local VSTest `Win32Exception (5)` limitation), `pwsh`
  (absent; Windows PowerShell was used).
- **Not edited**: no product, test, doc or configuration file. The only documentation file created is
  this report. Scratch produced by the CLI lives under `.tmp/round11/`.

## Verdict table

| id | verdict | evidence | counterexample attempted | reason |
|---|---|---|---|---|
| C3 | Falsified | `WIP/Wip.Tholdem.Agents/IPokerAgent.cs:16-19` hands every agent `PlayerObservation` plus `AgentTurnContext`; `AgentTurnContext.cs:9` is `(ulong TurnSeed, int HandNumber, int Seat, int ActionIndex)` (no session-seed member). `SeedDerivation.cs:44-56` is `SHA-256("tholdem-turn-seed|" + sessionSeed + "|" + hand + "|" + seat + "|" + action + "|" + stream)`, first 8 bytes big-endian. The captured context for recorded seed 42 hand 1 (test output) is `turn seed 3386938122580612636`; SHA-256 enumeration of session seeds 0..100000 recovered `42` in 137 ms (`FOUND=42`). `Derive(42,1,0,0,0)` = `13149409904026330690` (independently computed with PowerShell BigInteger); `SeededDeck.Create(13149409904026330690)` prints `Qs Tc 4s Jd 8h 8s 8d Th 6c`; the recorded `play --seed 42 --hands 1` file has `holeCardsSeat0 ["Qs","4s"]`, `holeCardsSeat1 ["Tc","Jd"]`, `board ["8h","8s","8d","Th","6c"]`, `seed 13149409904026330690` — exact match. The spectator sub-claim holds: `SpectatorSnapshot`/`Wip.Tholdem.Web` has no match under `WIP/Wip.Tholdem.Agents`, the project references only Abstractions/Runtime/Tholdem, and `Wip.Tholdem.Agents.deps.json` contains no Web assembly. | Built a capture of everything an `IPokerAgent` receives at turn 1 of the recorded seed-42 hand: `PlayerObservation` fields plus `(TurnSeed=3386938122580612636, HandNumber=1, Seat=0, ActionIndex=0)`. Enumerated candidate session seeds with the public `DeriveTurnSeed` formula over 0..100000; candidate 42 matched. Then ran the public `Derive` and the deck shuffle and compared the nine dealt cards with `hand-00001.json` from a fresh `play --seed 42 --hands 1`. | The compound claim's third clause fails: for the recorded/default low-entropy session seed 42, the TurnSeed is not an effective one-way commitment. It is a keyed hash of a 6-bit-guessable secret, so an agent can enumerate the seed and reconstruct the opponent's hole cards and the whole board. The first clause has no direct seed/deck/opponent-card member and the spectator sub-claim is true, but the claim is conjunctive and the reconstruction clause is false. A uniformly random 64-bit seed would resist the 100k dictionary; the claim does not state that precondition, and the viewer default and the test fixture both use 42. |
| C5 | Supported | Tholdem sources (excluding `bin/obj`) contain no `HttpClient`, `WebRequest`, `HttpRequestMessage`, `TcpClient`, `Socket`, `Dns`, `SmtpClient` or external URL; the only URL is the loopback constant `http://127.0.0.1:5099` at `WIP/Wip.Tholdem.Web/TholdemWebHost.cs:21`. The web assets contain no `http(s)://`, CDN, `@import` or `url(...)`. A monitored Release `train --iterations 300 --seed 99` run was sampled 31 times over 37.7 s: `TCP_REMOTE_COUNT=0`, `UDP_COUNT=0`; viewer PID 22960 has only the `127.0.0.1:5099` listener. `GET /api/status` returns `{"mode":"spectator",...,"offline":true}`. No wallet/payment/account/betting integration exists in the Tholdem sources (`Street.cs:4` uses the word "betting" only in a poker-rules comment); the sample payment plugin is in `Modus.slnx`, not `Wip.slnx`, and is not part of the Tholdem product. No `dotnet pack`/`dotnet publish`/`nuget push`/`gh release`/`signtool`/deploy step exists in `.github/workflows`, `scripts`, `tools` or the Makefile for Tholdem; `git tag` has only the pre-existing `wip-baseline-2026-11-02`; `docs/tholdem` only disclaims release. `THOLDEM_LIVE_MODEL` is unset and `DEEPSEEK_API_KEY` is empty; `LiveModelGate.IsEnabled` has no product call site, and neither `Program.cs` composes a model. | Searched all Tholdem sources and viewer assets for network APIs and external URLs; ran both the CLI and the viewer while polling `Get-NetTCPConnection -OwningProcess` and `Get-NetUDPEndpoint`; scanned for money/account/betting names; searched every workflow, script, tool and the Makefile for pack/publish/sign/deploy; inspected the running viewer's sockets; checked the gate inputs and product call sites. Also inspected the `.tmp/falsify-c/pack/Wip.Tholdem.0.0.2.nupkg`/`.snupkg` and `.tmp/falsify-c/publish` left by an earlier review: they are scratch under `.tmp`, produced outside this delivery, and are not a delivered package or deployment. | No outbound network call was observed; the only socket is the loopback listener. The built Tholdem product has no real-money/account/external-betting code, and this delivery runs no publish/sign/deploy step and makes no release claim. The transitive `Wip.Runtime` dependency does contain unused `DeepSeek` `HttpClient` provider classes, but the Tholdem hosts never compose or call them; the claim is about calls made by the built product, not about the presence of unreferenced library code. Ordinary packability (`Directory.Build.props` symbols) is explicitly allowed by the claim. |
| C6 | Falsified | `dotnet build Wip.slnx --configuration Release` succeeded (0 errors); `--help`/`--version` print the documented usage/`0.1.0`; `play --seed 777 --hands 2` reproduces the documented `session.json` (`tholdem-f275b4ee69be0557`, handHashes `571049…`/`6672b5…`) and `hand-00001.json` exactly; `train --iterations 1` and `--iterations 3 --seed 7` reproduce the documented challenger ids, ledger row, evaluation document and all three printed iteration lines; `evaluate --seeds 4` reproduces the documented CLI evaluation document exactly; `promote` without/with `--yes` prints the documented refusals and exits 3 with the pointer unchanged; `replay` prints `integrity: unanchored` and exits 5 on a tampered file; missing strategy exits 4; usage exits 2; retention status. Suite: `TOTAL 179 PASSED 179 FAILED 0 SKIPPED 0` (exit 0); 179 `[Fact]` attributes, 0 `Skip=`, 0 `Assert.Skip`, 0 `#if`, 0 `[Trait]`; `docs/plan/test-waivers.json` has `expectedTotals["Wip.Tholdem.Tests"]=179` and 0 Tholdem failure / 0 Tholdem knownAborted entries. However, `docs/tholdem/viewer.md:106-108` says “neither the session seed nor the deck can be reconstructed from it” and `docs/tholdem/engine-rules.md:170-171` says “the session seed and the deck are never exposed to an agent, and the digest cannot be inverted to either”; the C3 TurnSeed dictionary attack reconstructs both from the context for the recorded/default seed 42. Independently, `docs/tholdem/training-promotion-runbook.md:23` says “`StrategyArtifact.Create` persists the challenger”; `StrategyArtifact.Create` (`Learning/StrategyArtifact.cs:28-36`) is a pure factory with no I/O, and the persistence is `TrainingLoop.cs:137` `WriteUtf8(strategyDirectory, challenger.Id + ".json", ...)`. | Ran at least eight documented commands/examples and compared exact JSON/console output (build, help/version, play seed 777, train seed 7 x1/x3, evaluate seeds 4/24, promote, replay, retention, exit codes, viewer endpoints). Checked every Tholdem test source for skip/delete/weakening markers and the waiver JSON for Tholdem entries. Then executed the C3 reconstruction against the test’s own captured context and compared with the recorded hand file; also read `StrategyArtifact.Create` and the `TrainingLoop` write site. | The concrete examples in `docs/tholdem/*.md` are current and the skip/waiver bookkeeping is clean, but the claim is a universal “every factual claim … matches current code behaviour”, and at least two factual claims do not: the observation-boundary “cannot be reconstructed” claim is refuted by the C3 attack, and the runbook’s “`StrategyArtifact.Create` persists” step is false. The committed boundary test that is meant to pin the turn-seed property (`AgentInformationBoundaryTests.TurnSeed_GivenSessionSeed42Hand1_ExpectedDeckAndRecordedCardsDiffer`) is a strawman: it feeds `TurnSeed` into `Derive` as if it were the session seed and asserts the resulting deal differs; it does not attempt the dictionary reconstruction that succeeds. The suite is therefore green while the documented security property is false. |

## C3 — how the reconstruction was done

### What an agent receives

- `WIP/Wip.Tholdem.Agents/IPokerAgent.cs:16-19` defines the only agent entry point:
  `ActAsync(PlayerObservation observation, AgentTurnContext context, CancellationToken)`.
- `WIP/Wip.Tholdem.Agents/AgentTurnContext.cs:9`:
  `public readonly record struct AgentTurnContext(ulong TurnSeed, int HandNumber, int Seat, int ActionIndex);`
- `WIP/Wip.Tholdem/History/PlayerObservation.cs:22-54` exposes hand number, street, button seat,
  acting seat, board, own hole cards, stacks, pot, commitments, amount to call, min/max raise-to,
  legal action kinds, completion and winners. `ToCanonicalJson` (`:90-140`) lists exactly those
  fields; there is no seed, no deck and no opponent hole card member.
- `WIP/Wip.Tholdem.Agents/SeedDerivation.cs:44-56` builds
  `"tholdem-turn-seed|<sessionSeed>|<handNumber>|<seat>|<actionIndex>|<stream>"`, hashes it with
  SHA-256 and reads the first eight big-endian bytes.
- `WIP/Wip.Tholdem.Agents/Simulation/MatchSimulator.cs:68-77` and
  `WIP/Wip.Tholdem.Web/Live/LiveMatch.cs:222` construct the context from the session seed. The
  recorded `play` path goes through `MatchSimulator`.
- The runtime agent (`Runtime/TholdemRuntimeAgent.cs:98-107`) deliberately ignores the context and
  sends only the canonical observation to the model; the generic `IPokerAgent` API still receives
  the TurnSeed, which is what the claim’s “everything an IPokerAgent receives” covers.

### The captured turn

The committed boundary test was run against the rebuilt assembly:

```text
$ dotnet run --project .tmp/tholdem-verify/RunXunit/RunXunit.csproj -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll AgentInformationBoundaryTests

recorded deck prefix : Qs Tc 4s Jd 8h 8s 8d Th 6c
context seed prefix  : 6c 3s 6d As Jd 4d 6s 7d Ah
recorded deal        : seat0=Qs,4s seat1=Tc,Jd board=8h 8s 8d Th 6c
replayed deal        : seat0=6c,6d seat1=3s,As board=Jd 4d 6s 7d Ah
turn seed            : 3386938122580612636
reconstructed seed   : 16723055204109422328
engine hand seed     : 13149409904026330690
TOTAL 4 PASSED 4 FAILED 0 SKIPPED 0
```

The test captures the real context handed to an agent for `MatchSimulator` with session seed 42 and
hand 1. The TurnSeed depends only on `(sessionSeed, handNumber, seat, actionIndex, stream)`, not on
the action or the agent; the recorded `play --seed 42 --hands 1` hand has first actor seat 0 at
action index 0 and uses `stream = 0`, so the recorded match hands the same context.

### The dictionary reconstruction

The material format and SHA-256 are public in the agent assembly. The following PowerShell loop
tries candidate session seeds 0..100000 and compares the first eight big-endian bytes:

```text
$target=[uint64]3386938122580612636
$sha=[System.Security.Cryptography.SHA256]::Create()
for ($c=0; $c -le 100000; $c++) {
  $material="tholdem-turn-seed|$c|1|0|0|0"
  $d=$sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($material))
  $v=[uint64]0; for ($i=0; $i -lt 8; $i++) { $v=($v -shl 8) -bor [uint64]$d[$i] }
  if ($v -eq $target) { $found=$c; break }
}
FOUND=42 ELAPSED_MS=137
```

`Derive(42,1,0,0,0)` was then computed independently with PowerShell `System.Numerics.BigInteger`
and the exact splitmix constants from `SeedDerivation.cs:22-35`:

```text
DERIVE=13149409904026330690
EXPECT=13149409904026330690
```

The existing scratch `TurnProbe` binary (built against the current `Wip.Tholdem`) prints the deck
prefix for that hand seed:

```text
$ .tmp\tholdem-verify\TurnProbe\bin\Release\net10.0\TurnProbe.exe 13149409904026330690
Qs Tc 4s Jd 8h 8s 8d Th 6c
```

The recorded hand from a fresh CLI run:

```text
$ WIP\Wip.Tholdem.Host\bin\Release\net10.0\Wip.Tholdem.Host.exe play --seed 42 --hands 1 --data-dir .tmp\round11\seed42
session: tholdem-c3d4ff060ef47ae4
seed: 42  hands: 1  manifest sha256: ecd113c2c82b128ad66193d21d9e79883bb5f5ad182a4faca3f9383c466638d1

hand-00001.json:
{"handNumber":1,"seed":13149409904026330690,"smallBlind":5,"bigBlind":10,"startingStack":1000,"buttonSeat":0,"holeCardsSeat0":["Qs","4s"],"holeCardsSeat1":["Tc","Jd"],"board":["8h","8s","8d","Th","6c"],...}
```

Deck order `[0], [2]` = seat 0 = `Qs 4s`; `[1], [3]` = seat 1 = `Tc Jd`; `[4..8]` = board =
`8h 8s 8d Th 6c`. The reconstruction reproduces the recorded other-seat hole cards and board
exactly.

### Sub-claims that do hold

- **No direct seed field**: `AgentTurnContext` has `TurnSeed`, `HandNumber`, `Seat`,
  `ActionIndex`; reflection in the suite finds no `SessionSeed` property.
- **Spectator type not reachable**: `grep` for `SpectatorSnapshot|Wip.Tholdem.Web` under
  `WIP/Wip.Tholdem.Agents` returns no matches; the `.csproj` references only `Wip.Abstractions`,
  `Wip.Runtime` and `Wip.Tholdem`; `Wip.Tholdem.Agents.deps.json` has no Web assembly.
- **No direct opponent-card field**: `PlayerObservation` has only `OwnHoleCards`; the runtime
  hand-history tool (`SessionHandView`) also has only `OwnHoleCards`.

The failure is the third, operational clause: the TurnSeed is a hash of a low-entropy session
seed. For the recorded/default seed 42 the dictionary is 43 candidates and takes milliseconds. A
64-bit uniform seed would resist this exact dictionary; the claim is unqualified, and both the
viewer form (`wwwroot/viewer/index.html:31`, value 42) and the test fixture use 42.

## C5 — observed offline evidence

- Source and asset scan over `WIP/Wip.Tholdem*` (excluding `bin/obj`) found no `HttpClient`,
  `WebRequest`, `HttpRequestMessage`, `TcpClient`, `Socket`, `Dns`, `SmtpClient` or external URL.
  The only URL is `TholdemWebHost.cs:21` `http://127.0.0.1:5099`.
- Viewer assets (`wwwroot/viewer/index.html`, `app.js`, `viewer.css`) contain no `http(s)://`,
  `//cdn`, `@import` or `url(...)`.
- `Get-NetTCPConnection`/`Get-NetUDPEndpoint` polling of a Release `train --iterations 300
  --seed 99` process (PID 24632) recorded 31 samples over 37.7 s with `TCP_REMOTE_COUNT=0` and
  `UDP_COUNT=0`; stdout ended at `iteration=300 …` and `final champion: …`.
- Viewer PID 22960 (`.tmp/viewer-run4/Wip.Tholdem.Web.dll`, args `--urls
  http://127.0.0.1:5099 --data-dir C:\Users\ricar\dev\Modus\artifacts\tholdem`) has only
  `127.0.0.1:5099` in `Listen`; `GET /api/status` returned
  `{"mode":"spectator","dataDirectory":"C:\\Users\\ricar\\dev\\Modus\\artifacts\\tholdem","sessions":2,"strategies":4,"liveMatches":0,"offline":true}`.
- Money/account scan: no `Wallet`, `Payment`, `PayPal`, `Stripe`, `Account`, `Deposit`,
  `Withdraw`, `Currency`, `Money`, `Cash`, `Bank`, `Casino`, `RealMoney` or `Betting` in the
  Tholdem sources except the poker-street doc comment in `WIP/Wip.Tholdem/Engine/Street.cs:4`.
- Delivery scan: no `dotnet pack`, `dotnet publish`, `nuget push`, `gh release`, `signtool`,
  `PublishProfile`, deploy or release step in `.github/workflows`, `scripts`, `tools` or the
  Makefile for Tholdem. `git tag --list` has only `wip-baseline-2026-11-02`; `docs/tholdem`
  disclaims release/publish/sign/deploy.
- Gate: `THOLDEM_LIVE_MODEL` is unset and `DEEPSEEK_API_KEY` is empty; `LiveModelGate.IsEnabled`
  (`Runtime/LiveModelGate.cs:13-31`) has no product call site; `WIP/Wip.Tholdem.Host/Program.cs`
  and `WIP/Wip.Tholdem.Web/Program.cs` compose no model, and `LiveAgentCatalog` only creates the
  three deterministic baselines.
- The transitive `WIP/Wip.Runtime/Runtime/DeepSeekChatModel.cs` and `DeepSeekModelProvider.cs` do
  contain `HttpClient` code, but `Wip.Tholdem.Host.deps.json` includes the `Wip.Runtime` library
  only; no Tholdem path constructs those classes and no call was observed. The claim is about
  calls made by the built product with the gate disabled, not about the presence of unused library
  code.
- `.tmp/falsify-c/pack/Wip.Tholdem.0.0.2.nupkg`/`.snupkg` and `.tmp/falsify-c/publish` are
  scratch from an earlier review under `.tmp`; they are not a delivered package and no delivery
  operation produced or published them. Ordinary .NET packability is not the claim.

## C6 — commands/examples and the mismatches

### Executed and compared (exact outputs)

1. `dotnet build Wip.slnx --configuration Release` — `Build succeeded.` 0 errors, 6 warnings
   (all pre-existing non-Tholdem package/solution warnings); exit 0. This is the documented
   quick-start build.
2. `WIP\Wip.Tholdem.Host\bin\Release\net10.0\Wip.Tholdem.Host.exe --help` and `--version` — the
   exact documented usage text and `0.1.0`, exit 0.
3. `play --seed 777 --hands 2` — printed `session: tholdem-f275b4ee69be0557`, manifest
   `32ff5488…`; `session.json` matched the documented example byte-for-byte
   (`{"schema":"tholdem.session.v1","sessionId":"tholdem-f275b4ee69be0557","seed":777,"hands":2,…,"handHashes":["571049…","6672b5…"]}`),
   and `hand-00001.json` matched the documented example byte-for-byte
   (`"seed":9754104818518126180`, `["4c","Js"]`, `["Tc","Kd"]`, empty board, one fold, winners
   `[1]`, pot 15, stacks `[995,1005]`).
4. `train --iterations 1 --seed 7` and `train --iterations 3 --seed 7` — printed exactly the
   documented lines (`challenger=tholdem-strategy-v2-b155b98f`, `meanBbPer100=0.00 lcb=0.00
   seeds=24` at iteration 1; `4.38/1.22` and `0.00/-0.83` at iterations 2/3; final champion
   `tholdem-strategy-v1-f100e793`). The persisted `strategies/tholdem-strategy-v1-f100e793.json`,
   `ledger.json`, training-loop evaluation report and `active-champion.json` matched the
   documented examples exactly. Two fresh `train --iterations 3 --seed 7` directories had 9 files
   each and 0 differing SHA-256 hashes.
5. `evaluate --challenger tholdem-strategy-v2-b155b98f --seeds 4` — wrote
   `{"schema":"tholdem.cli-evaluation.v1","criteria":{"marginBbPer100":5,"minSeeds":24},"report":{…,"seeds":4,…,"challengerWins":4,"incumbentWins":4,"ties":0,…}}`,
   exactly the documented CLI evaluation example; `--seeds 24` printed the documented
   `decision: lcb-below-margin` / `verifier: verified (rejection-justified)` lines.
6. `promote` without `--yes` printed `promotion requires explicit confirmation; re-run with --yes`
   / `promotion refused: lcb-below-margin` / `verifier: rejection-justified` and exited 3; with
   `--yes` printed the refused lines and exited 3; the pointer stayed
   `{"strategyId":"tholdem-strategy-v1-f100e793","iteration":0}` — the documented behavior.
7. `replay` on an anchored hand succeeded; on a copy without a sibling manifest printed
   `integrity: unanchored`; on a file with one extra trailing space printed a hash mismatch and
   exited 5. `evaluate --challenger no-such-strategy` exited 4. `play` without `--seed` exited 2.
   `retention --status` printed `sessions: 1`, `hands: 2`, `bytes: 1043`; `--json` printed
   `{"schema":"tholdem.retention.v1",…}`; `retention --purge --older-than-days 0` without `--yes`
   printed the preview and exited 3.
8. Viewer at 5099: `GET /` 302 `Location: /viewer/index.html`; `/viewer/` 200; `/api/status` exact
   `offline:true` body; `/api/sessions` and `/api/sessions/{id}`; `/api/sessions/{id}/hands/1`
   returns the recorded hand; `/viewer/table?...` 200 `text/html`; `/api/strategies` and
   `/api/strategies/{id}`; `/api/ledger`; `/api/champion` 200; `POST /api/live` with both baseline
   names 201 and a snapshot with both seats' hole cards; missing and unknown agent names 400
   `{"error":"unknown-agent","seat":…}`; pause/resume 200 `{"status":…}`; `DELETE` 204; unknown
   live id 404.

### Test and waiver integrity

- Rebuilt suite: `TOTAL 179 PASSED 179 FAILED 0 SKIPPED 0`, exit 0.
- 179 `[Fact]` attributes across 50 test source files; 0 `[Theory]`; 0 `Skip=`; 0
  `Assert.Skip`; 0 `#if`; 0 `[Trait]`; no empty/trivial assertions found in a scan for
  `Assert.True(true)`, `Assert.NotNull(null)`, swallowed `catch` or `Ignore`.
- `docs/plan/test-waivers.json`: `expectedTotals["Wip.Tholdem.Tests"] = 179`; zero entries in
  `failures` whose `project`/`test` name contains Tholdem; zero entries in `knownAborted` whose
  project contains Tholdem.
- `tools/run-wip-tests.ps1` includes `WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj` at line 38.
- The claim’s “skipped/deleted/weakened” bookkeeping is therefore clean on the evidence available.
  The Tholdem tree is untracked, so a historical diff is unavailable; no skip/deletion marker was
  found.

### Concrete mismatches (the falsification)

1. **Observation-boundary docs vs observed behavior.**
   `docs/tholdem/viewer.md:106-108`: “The context has no session-seed field, and neither the
   session seed nor the deck can be reconstructed from it.”
   `docs/tholdem/engine-rules.md:170-171`: “the session seed and the deck are never exposed to an
   agent, and the digest cannot be inverted to either.”
   The C3 dictionary reconstruction recovers the session seed (42) and reconstructs the exact
   other-seat hole cards and board from the same TurnSeed. The claim that they “cannot be
   reconstructed” does not match the observed behavior for the recorded/default seed.
2. **Runbook step 4 is not what the code does.**
   `docs/tholdem/training-promotion-runbook.md:23`: “`StrategyArtifact.Create` persists the
   challenger;”.
   `WIP/Wip.Tholdem.Agents/Learning/StrategyArtifact.cs:28-36`: `StrategyArtifact.Create` computes
   a version, hash and id and returns a `StrategyArtifact` record; it performs no file or stream
   I/O. The actual persistence is `WIP/Wip.Tholdem.Agents/Learning/TrainingLoop.cs:137`:
   `WriteUtf8(strategyDirectory, challenger.Id + ".json", challenger.ToCanonicalJson());`.
   The numbered iteration description therefore names the wrong operation for step 4.
3. **Supporting example-layout mismatch.**
   `docs/tholdem/strategy-artifact-schema.md:117-118` says `play` (and `MatchSimulator.Run` with
   a history directory) writes `<data>/sessions/<sessionId>/session.json` and
   `hand-NNNNN.json`. `MatchSimulator.Run` writes the two files directly into
   `options.HistoryDirectory` (`MatchSimulator.cs:39-40`, `137-141`, `159-162`) and never creates
   `sessions/<sessionId>`; `PlayCommand.cs:26-48` is what stages `.inprogress` and moves it to
   `sessions/<sessionId>`.

The test that is meant to pin the turn-seed boundary,
`AgentInformationBoundaryTests.TurnSeed_GivenSessionSeed42Hand1_ExpectedDeckAndRecordedCardsDiffer`,
derives `SeedDerivation.Derive(context.TurnSeed, context.HandNumber, 0, 0, 0)` and asserts the
resulting deal differs from the recorded deal. That is not the attack a cheating agent would run:
it treats the opaque TurnSeed as if it were the session seed. The dictionary attack above uses the
actual session seed space and succeeds, so the suite can be green while the documented property is
false.

## Summary

- **C3 Falsified**: the spectator-type sub-claim holds and there is no direct seed field, but the
  third clause fails for the recorded/default low-entropy seed 42: TurnSeed enumeration recovers
  the session seed and reproduces the opponent's hole cards and board.
- **C5 Supported**: no outbound network call observed in the built product, no money/account/
  betting integration, and no delivery publish/sign/deploy or release claim; the only package-like
  files are prior-review scratch under `.tmp`.
- **C6 Falsified**: the documented commands and concrete examples reproduce exactly and the
  skip/waiver bookkeeping is clean, but the universal “every factual claim matches code” clause is
  false — the observation-boundary “cannot be reconstructed” claim is refuted by the C3 attack,
  and the runbook’s `StrategyArtifact.Create` persistence step does not match the code.
