# Round 13 - falsifier A (C3, C6)

- **Repository**: `C:\Users\ricar\dev\Modus`, branch `WIP`, HEAD `f1a26bb`.
- **Date**: 2026-11-07.
- **Tree under test**: the round-13 implementation in `docs/goal-tholdem/round-13/implementation.md`, read from the current working tree. Prior rounds were not used as evidence; every result below was re-derived.
- **Build used**: `WIP\Wip.Tholdem.Host\bin\Release\net10.0\Wip.Tholdem.Host.exe` and the Release DLLs beside it; the test assembly was run with `.tmp/tholdem-verify/RunXunit`. `dotnet build Wip.slnx --configuration Release` was run and exited 0 (6 warnings, 0 errors).
- **Local limits honoured**: `dotnet test` was not used (VSTest unusable here); the supplementary runner over the compiled test assembly was used; the harness PowerShell host was used for all commands; no scratch under `%TEMP%` (all scratch under `.tmp/falsify13` and `artifacts\tholdem-run`); the viewer at `http://127.0.0.1:5099` was queried, not restarted.

## Verdict summary

| id | verdict | evidence | counterexample attempted | reason |
|---|---|---|---|---|
| C3 | Supported | (a) A live probe captured the exact inputs of the seat-0 agent in `play`/simulator seed 42 hand 1: observation JSON `{"handNumber":1,...,"ownHoleCards":["Qs","4s"],...}` with no seed, deck, undealt cards or opponent cards; context `AgentTurnContext { HandNumber = 1, Seat = 0, ActionIndex = 0 }`; reflection lists exactly the 17 documented `PlayerObservation` members and the three context ints. `Wip.Tholdem.Agents.dll` references no `Wip.Tholdem.Web`, and `GetType("Wip.Tholdem.Web.Live.SpectatorSnapshot")` returns null. (b) A scratch consumer that references only `Wip.Tholdem.Agents` and uses `Wip.Tholdem.Web.Live.SpectatorSnapshot` fails to compile: `error CS0234: The type or namespace name 'Web' does not exist in the namespace 'Wip.Tholdem'`. The project graph is Agents -> {Abstractions, Runtime, Tholdem}; Web -> {Tholdem, Agents}. (c) CLI: `play --seed 42 --hands 1`, `play --seed=42`, `play --seed 1099511627775`, `train --iterations 1 --seed 7`, `evaluate --challenger nope --seeds 1 --seed 42` all exit 2 with `tholdem: seed <n> is below the minimum 1099511627776; use --allow-weak-seed for reproducible demos`; no session/ledger/evaluation was created. With `--allow-weak-seed`: play/train/evaluate exit 0 and write their artifacts; seed `1099511627776` without the flag exits 0. HTTP on 5099: seed 42, 1099511627775 and string `"42"` without the field all return 400 `{"error":"weak-seed","minimum":1099511627776}`; `allowWeakSeed:true` seed 42 returns 201; `allowWeakSeed:false` returns 400; seed 1099511627776 (number or string) without the field returns 201. (d) `docs/tholdem/known-limits.md` section `Threat model` states (a)-(d) precisely; it states that a weak seed lets an observer enumerate and recover the deck (including the round-13 11-match/first-42/deck reproduction) and that opting out is outside the secrecy guarantee. A grep of `docs/tholdem` finds no absolute "non-invertible" wording except the meta-sentence saying it was replaced. | Captured the actual agent inputs during a real match and searched them for seed/deck/opponent cards; tried to compile `SpectatorSnapshot` from an Agents-only consumer; tried every weak-seed spelling/boundary through CLI and the live HTTP endpoint, with and without opt-in; inspected the agent-facing tools (`HandHistoryTool`, `OpponentStatsTool`, `EquityTool`, `SessionHandStore`) and the Agents assembly metadata; searched the docs for absolute claims. The only residual is the documented weak-seed enumeration (disclosed in (b)/(d) and gated at the named entry points), plus out-of-scope library APIs and source comments noted below. | The four conjuncts hold on the current tree. The round-12 inversion counterexample is no longer a counterexample to C3: (a) is direct-exposure scoped, and (b)/(d) explicitly state and gate the enumeration risk. The spectator type is unreachable from Agents both by project graph and by an observed compile error. |
| C6 | Supported | Supplementary runner twice (before and after the solution build): `TOTAL 194 PASSED 194 FAILED 0 SKIPPED 0`, exit 0. Source scan: 51 Tholdem test files, 194 `[Fact]`, 0 `[Theory]`/`[InlineData]`, no `Skip =`, no `#if`, no `Compile Remove`/excluded file; all 194 facts executed. `docs/plan/test-waivers.json`: `expectedTotals["Wip.Tholdem.Tests"] = 194`; no `Wip.Tholdem.Tests` entry in `failures` or `knownAborted`. Documented commands run and compared: `--version` -> `0.1.0`; `--help` -> usage block with `--allow-weak-seed` and seed policy; `train --iterations 3 --seed 7 --allow-weak-seed --data-dir artifacts\tholdem-run` -> the exact three documented iteration lines and final champion; `evaluate --challenger tholdem-strategy-v2-b155b98f --seeds 24 --data-dir artifacts\tholdem-run` -> `mean bb/100: 0.00  LCB: 0.00  margin: 5.00`, `decision: lcb-below-margin`, `verifier: verified (rejection-justified)`; `promote --yes` -> `promotion refused: lcb-below-margin` / `verifier: rejection-justified`, exit 3; `promote` without `--yes` -> `promotion requires explicit confirmation; re-run with --yes`, exit 3; `play --seed 777 --hands 2 --allow-weak-seed` -> `session: tholdem-f275b4ee69be0557`, `manifest sha256: ab3095f7472f815bbeaa9a7cdbcba6eee0acd802241dc10f1e4185807fffb8ee`; `evaluate --seeds 4` -> documented CLI evaluation; `train --iterations 1 --seed 7 --allow-weak-seed` -> documented v1 artifact/ledger/report/pointer. Seven fenced JSON examples in `strategy-artifact-schema.md` are byte-equal to the regenerated files: v1 333, ledger 273, training report 279, CLI evaluation 370, pointer 59, session 335, hand 321 bytes. `replay` anchored/unanchored/correct 64-hex/wrong 64-hex -> exit 0 / 0 with `integrity: unanchored` / 0 / 5 with the hash-mismatch message; `retention --status` -> `sessions: 1`, `hands: 2`, `bytes: 1043`, oldest session. `dotnet build Wip.slnx --configuration Release` -> build succeeded, 0 errors. Threat-model enumeration reproduced with `SeedSearchProbe Qs 4s 1 20000`: `candidates=20001 matches=11`, first `42`, `deck=Qs Tc 4s Jd 8h 8s 8d Th 6c`, `opponent_hole=Tc,Jd`, `board=8h,8s,8d,Th,6c`. | Ran the full suite and scanned for skipped/deleted/weakened tests (Skip, #if, Theory/InlineData, Compile Remove, waiver entries). Ran the documented commands and all seven JSON examples verbatim and compared output/bytes. Tried to find a documented command that fails (help, build, train, evaluate, promote, play, replay, retention, JSON, `--min-seeds`). Checked the seed-guard messages and the threat-model enumeration. Also checked the direct library APIs (`MatchSimulator.Run`, `TrainingLoop.Run`, `LiveMatchRegistry.Start`) and the source XML comments for scope. | No skipped, deleted or weakened Tholdem test was found; the waiver file has no Tholdem failure/abort waiver; every concrete documented command/example and the threat-model statements reproduce. The only numeric difference is the enumeration's elapsed time (observed 629 ms vs the document's round-13 388 ms); the documented match count, first seed, deck, opponent cards and board are exact, and timing is not a deterministic behavioural claim. |

## C3 - hidden information

### C3(a): the agent-facing surface

The probe referenced only `Wip.Tholdem.Agents` and ran `MatchSimulator.Run` with seed 42, hand 1, capturing every `ActAsync` argument. The first seat-0 turn was:

```text
observation={"handNumber":1,"street":0,"buttonSeat":0,"actingSeat":0,"board":[],"ownHoleCards":["Qs","4s"],"ownStack":995,"opponentStack":990,"pot":15,"ownStreetCommitment":5,"opponentStreetCommitment":10,"amountToCall":5,"minRaiseTo":20,"maxRaiseTo":1000,"legalActionKinds":["Fold","Call","Raise","AllIn"],"isComplete":false,"winners":[]}
context=AgentTurnContext { HandNumber = 1, Seat = 0, ActionIndex = 0 }
contains-seed-lower=False
contains-deck=False
contains-holeCardsSeat1=False
contains-opponent-Tc-Jd=False
obs-props=HandNumber:Int32,Street:Street,ButtonSeat:Int32,ActingSeat:Int32,Board:IReadOnlyList`1,OwnHoleCards:IReadOnlyList`1,OwnStack:Int64,OpponentStack:Int64,Pot:Int64,OwnStreetCommitment:Int64,OpponentStreetCommitment:Int64,AmountToCall:Int64,MinRaiseTo:Int64,MaxRaiseTo:Int64,LegalActionKinds:IReadOnlyList`1,IsComplete:Boolean,Winners:IReadOnlyList`1
ctx-props=HandNumber:Int32,Seat:Int32,ActionIndex:Int32
agents-assembly=Wip.Tholdem.Agents
agents-refs=System.Runtime,Wip.Tholdem,System.Text.Encoding.Extensions,System.Collections,Wip.Abstractions,System.Text.Json,System.Security.Cryptography,System.Memory,System.Linq,System.Threading
spectator-in-agents-assembly=False
```

The actual session-42 hand-1 opponent cards are `Tc,Jd` and the board is `8h,8s,8d,Th,6c`; none of those tokens appears in the observation. The observation is the only hidden-information projection passed to an `IPokerAgent`; the context is three public ints. The agent-facing runtime tools are bounded as documented: `HandHistoryTool` fixes the seat at construction and returns `SessionHandView` (own cards, board, action text, winners, net chips); `OpponentStatsTool` returns aggregate statistics only; `EquityTool` returns equity/wins/ties/losses for the model-supplied cards; `SessionHandStore.BuildFor` projects through `PlayerObservation.ForSeat` and its public views contain no opponent cards or seed. `SimulationReport.Seed` and `SimulationOptions.Seed` are operator/simulator values and are not passed to an agent.

`docs/tholdem/known-limits.md` states the same (a) and explicitly qualifies that session manifests and hand files are operator replay artifacts written by `play`, never agent inputs. The test suite's source scan (`RuntimeSourceBoundaryTests`) and the compile check below support that no runtime agent source references the spectator type.

### C3(b): the spectator snapshot is unreachable from Agents

`WIP/Wip.Tholdem.Agents/Wip.Tholdem.Agents.csproj` references only `Wip.Abstractions`, `Wip.Runtime` and `Wip.Tholdem`. `Wip.Tholdem.Web.csproj` references `Wip.Tholdem` and `Wip.Tholdem.Agents`, so a Web type cannot be compiled into Agents. A scratch project referencing only `Wip.Tholdem.Agents` with:

```csharp
using Wip.Tholdem.Web.Live;
SpectatorSnapshot? snapshot = null;
```

failed to build with:

```text
error CS0234: The type or namespace name 'Web' does not exist in the namespace 'Wip.Tholdem' (are you missing an assembly reference?)
```

The compiled `Wip.Tholdem.Agents.dll` references no `Wip.Tholdem.Web`, and reflection over that assembly returns no `Wip.Tholdem.Web.Live.SpectatorSnapshot` type. The source tree under `WIP/Wip.Tholdem.Agents` contains no `SpectatorSnapshot`, `TholdemWebHost` or `LiveMatch` identifier.

### C3(c): the seed guard

CLI (Release `Wip.Tholdem.Host.exe`, captured via `Start-Process -RedirectStandardOutput/-RedirectStandardError`):

```text
play --seed 42 --hands 1                         exit 2  tholdem: seed 42 is below the minimum 1099511627776; use --allow-weak-seed for reproducible demos
play --seed=42 --hands 1                         exit 2  tholdem: seed 42 is below ...
play --seed 1099511627775 --hands 1              exit 2  tholdem: seed 1099511627775 is below ...
train --iterations 1 --seed 7                    exit 2  tholdem: seed 7 is below ...
evaluate --challenger nope --seeds 1 --seed 42   exit 2  tholdem: seed 42 is below ...
```

The rejected runs created no `sessions` directory, `ledger.json` or `evaluations` directory. With the explicit flag:

```text
play --seed 42 --hands 1 --allow-weak-seed          exit 0  session written
train --iterations 1 --seed 7 --allow-weak-seed     exit 0  ledger + artifacts written
evaluate ... --seed 42 --allow-weak-seed            exit 0  CLI evaluation written
play --seed 1099511627776 --hands 1                 exit 0  session written
train --iterations 1 --seed 1099511627776           exit 0  ledger + artifacts written
evaluate ... (no --seed; default 1099511627776)     exit 0  CLI evaluation written
```

HTTP on the running viewer at `http://127.0.0.1:5099`:

```text
seed 42, no allowWeakSeed                 400 {"error":"weak-seed","minimum":1099511627776}
seed 42, allowWeakSeed:false              400 {"error":"weak-seed","minimum":1099511627776}
seed 1099511627775, no allowWeakSeed      400 {"error":"weak-seed","minimum":1099511627776}
seed "42", no allowWeakSeed               400 {"error":"weak-seed","minimum":1099511627776}
seed 42, allowWeakSeed:true               201 {"id":"...","seed":42,...}
seed 1099511627776, no allowWeakSeed      201 {"id":"...","seed":1099511627776,...}
seed "1099511627776", no allowWeakSeed    201 {"id":"...","seed":1099511627776,...}
```

The served viewer assets contain the strong default `value="1099511627776"` / `data-minimum-seed`, the `#allow-weak-seed` checkbox, and `app.js` contains `MINIMUM_SEED`, `randomSeedText`, `crypto.getRandomValues` and `allowWeakSeed`.

### C3(d): the documentation

`docs/tholdem/known-limits.md` `## Threat model`:

- (a) states that no agent API or agent-side artifact exposes the session seed, deck order, undealt cards or the other seat's hole cards, and names `PlayerObservation`, `AgentTurnContext`, `SeedDerivation.DeriveAgentSeed` and `RuntimeSourceBoundaryTests`.
- (b) states that the deal is deterministic from the session seed, that a guessable seed lets an observer enumerate candidates and reconstruct the deck (including undealt cards and the opponent's hole cards), that no one-way digest protects it, and reproduces the round-13 numbers.
- (c) states the CLI/HTTP guard, the exact exit/message and the HTTP 400 body, and that the viewer form generates a random 64-bit seed and exposes the opt-in.
- (d) states that an operator who passes `--allow-weak-seed`, sends `"allowWeakSeed": true` or edits the entry point is outside the secrecy guarantee.

A grep of all `docs/tholdem/*.md` for `invert`, `non-invert`, `reveals nothing`, `nothing an agent`, `never`, `cannot`, `guarantee` found only the precise wording above; the only occurrence of "non-invertible" is the sentence saying such earlier wording was replaced. `viewer.md` and `engine-rules.md` say no agent input contains a *stored* value to invert and immediately state that the deal is deterministic and a guessable seed is enumerable. That is the claim (d) requires.

### C3 counterexample attempts and residuals

- Captured real agent inputs and searched for the seed, deck, undealt cards and opponent cards; none appeared.
- Compiled an Agents-only consumer that used `SpectatorSnapshot`; compile error CS0234 (the type is unreachable).
- Tried `--seed 42`, `--seed=42`, `--seed 1099511627775`, `train` seed 7, `evaluate` seed 42, HTTP seed 42, HTTP 1099511627775, HTTP string `"42"`, HTTP `allowWeakSeed:false`; all were refused (exit 2 / HTTP 400). The opt-in forms were accepted, as documented.
- Inspected the agent-facing tool results and the session store; the views are own-seat projections. The public `SessionHandStore.BuildFor(int seat, ...)` accepts a seat argument, but the agent-facing wrapper (`HandHistoryTool`) fixes the seat at construction and the agent never receives the store; this is a design nuance, not an observed agent-facing leak.
- Searched the docs for absolute non-invertibility; only the precise wording remains.

Residuals not counted against the claim as stated: (1) the direct library APIs `MatchSimulator.Run`, `TrainingLoop.Run`, `SimulationOptions` and `LiveMatchRegistry.Start` still accept a weak seed with no opt-in; the docs sentence "Weak seeds therefore exist only behind ..." sits under the `(c) Entry points require strong seeds` heading and C3(c) names only the CLI/HTTP entry points, and the round-13 implementation section 11 records this as out of scope. (2) Source XML comments in `IPokerAgent.cs`/`SeedDerivation.cs` still say an agent input "cannot be inverted" / "is not a function of the session seed"; those are source comments, not `docs/tholdem`, not agent inputs and not an artifact. Neither is a counterexample to the claim as scoped, but both are worth recording for a future docs pass.

## C6 - test and doc integrity

### Tests and waivers

```text
dotnet run --project .tmp/tholdem-verify/RunXunit/RunXunit.csproj -c Release -- WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
TOTAL 194 PASSED 194 FAILED 0 SKIPPED 0
exit: 0
```

The same result was observed after `dotnet build Wip.slnx --configuration Release` (build succeeded, 0 errors, exit 0). Source scan: 51 files, 194 `[Fact]`, 0 `[Theory]`, 0 `[InlineData]`/`[MemberData]`, no `Skip =`, no `#if`/`#endif`, no `Compile Remove` or exclusion in `Wip.Tholdem.Tests.csproj`. The 10 `SessionSeedGuardTests` facts assert exit 2 plus the exact minimum message and the absence of session/ledger/evaluation artifacts for the weak-seed cases, and exit 0 plus the artifact for the opt-in/strong cases; the two `TholdemWebViewerTests` facts assert the exact 400 body and minimum, the 201 opt-in, the 201 minimum, the string-seed transport, and the viewer markup/JS. This is substantive coverage, not a weakened assertion.

`docs/plan/test-waivers.json` has `"Wip.Tholdem.Tests": 194`; `knownAborted` and `failures` contain no `Wip.Tholdem.Tests` entry. No Tholdem test was skipped, deleted or weakened to reach green.

### Documented commands and examples (observed)

All commands were run from the repository root with the Release host. The documented train/evaluate/promote/session examples were re-run verbatim into `artifacts\tholdem-run`:

```text
train --iterations 3 --seed 7 --allow-weak-seed --data-dir artifacts\tholdem-run
iteration=1 champion=tholdem-strategy-v1-f100e793 challenger=tholdem-strategy-v2-b155b98f promoted=false verified=true outcome=rejected meanBbPer100=0.00 lcb=0.00 seeds=24
iteration=2 champion=tholdem-strategy-v1-f100e793 challenger=tholdem-strategy-v2-e1ba56f7 promoted=false verified=true outcome=rejected meanBbPer100=1.67 lcb=-0.76 seeds=24
iteration=3 champion=tholdem-strategy-v1-f100e793 challenger=tholdem-strategy-v2-ea96d95d promoted=false verified=true outcome=rejected meanBbPer100=-1.25 lcb=-3.40 seeds=24
final champion: tholdem-strategy-v1-f100e793

evaluate --challenger tholdem-strategy-v2-b155b98f --seeds 24 --data-dir artifacts\tholdem-run
mean bb/100: 0.00  LCB: 0.00  margin: 5.00
decision: lcb-below-margin
verifier: verified (rejection-justified)

promote --challenger tholdem-strategy-v2-b155b98f --yes --data-dir artifacts\tholdem-run
promotion refused: lcb-below-margin
verifier: rejection-justified
exit=3
```

`play --seed 777 --hands 2 --allow-weak-seed` produced `session: tholdem-f275b4ee69be0557` and `manifest sha256: ab3095f7472f815bbeaa9a7cdbcba6eee0acd802241dc10f1e4185807fffb8ee`; `evaluate --seeds 4` produced the documented 4/4/0 CLI evaluation. A fresh `train --iterations 1 --seed 7 --allow-weak-seed` produced the documented v1 artifact, ledger, training report and pointer.

The seven fenced JSON examples were extracted from `strategy-artifact-schema.md` and compared byte-for-byte:

```text
v1 artifact:      doc=333 actual=333 byteEqual=True
ledger:           doc=273 actual=273 byteEqual=True
training report:  doc=279 actual=279 byteEqual=True
cli eval:         doc=370 actual=370 byteEqual=True
pointer:          doc=59  actual=59  byteEqual=True
session:          doc=335 actual=335 byteEqual=True
hand:             doc=321 actual=321 byteEqual=True
```

Other documented behaviours observed: `--version` -> `0.1.0`; `--help` -> the usage block with `--allow-weak-seed` and the seed policy (the README's train line is wrapped differently but the commands are identical); `replay` anchored/unanchored/correct 64-hex/wrong 64-hex -> exit 0 / 0 with `integrity: unanchored` / 0 / 5 with the hash-mismatch message; `retention --status` -> `sessions: 1`, `hands: 2`, `bytes: 1043`, oldest session; `evaluate --min-seeds` -> exit 2 `unknown option '--min-seeds'` (the runbook says there is no such flag); `promote` without `--yes` -> exit 3 with the documented confirmation line.

### Threat-model statement

The enumeration in `known-limits.md` was re-run against the current Release assemblies:

```text
SeedSearchProbe Qs 4s 1 20000
candidates=20001 matches=11 elapsed_ms=629
recovered=42,2865,2954,5700,6077,9955,11832,12050,12229,16184
deck=Qs Tc 4s Jd 8h 8s 8d Th 6c
opponent_hole=Tc,Jd
board=8h,8s,8d,Th,6c
```

The documented `11 matches`, `first 42`, derived deck, opponent `Tc,Jd` and board are exact. The only difference is the elapsed time: 629 ms observed vs the round-13 document's 388 ms. That is a machine/load timing observation, not a deterministic behavioural output; the substantive threat-model numbers match.

### C6 counterexample attempts and residuals

- Ran the full suite twice and scanned every test source for skip/deletion/weakening markers; all 194 facts executed, 0 skipped.
- Checked the waiver file for Tholdem failures/aborts; none.
- Ran the documented train/evaluate/promote/play/replay/retention/help/build commands and all seven JSON examples; all matched.
- Tried to find a failing documented command or a stale example; none failed.
- Checked the documented seed-guard message (the CLI adds the `tholdem: ` prefix; the quoted exception message is present) and the exact HTTP 400 body.
- Checked the threat-model enumeration; the documented count/first/deck/opponent/board match, with the timing caveat above.
- Residual note: the docs' `Weak seeds therefore exist only behind ...` sentence is scoped by the same section's `Entry points require strong seeds` heading; the direct library APIs named in the round-13 implementation section 11 are outside that scope. The source XML comments noted under C3 are also outside `docs/tholdem`. Neither is a documented command/example, so neither falsifies C6 as stated.

## Not checked / limits

- CI/GitHub Actions execution was not observed (the docs make no CI claim).
- Browser JavaScript, keyboard handling and responsive geometry were not executed end-to-end; only the served assets, the HTTP surface and the markup/JS source assertions were checked.
- The live-model provider path was not exercised (no `DEEPSEEK_API_KEY`; the docs say it is gated).
- `dotnet test` was not used (the local VSTest limitation); the supplementary runner over the compiled assembly is the test evidence.
- `retention --purge` (destructive) was not run; only `retention --status`.
- OS-level side channels (debugger, process memory, an agent deliberately reading files outside the `IPokerAgent` contract) were not attempted.