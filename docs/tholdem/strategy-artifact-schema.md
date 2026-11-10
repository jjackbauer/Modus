# Strategy artifact schema

Everything the learning loop persists is JSON data with a fixed key order produced by
`CanonicalJsonWriter` (`WIP/Wip.Tholdem.Agents/Simulation/CanonicalJsonWriter.cs`). There are no
timestamps, GUIDs or culture-dependent numbers in any identity. The code that defines each document
is named under every section. **No release is claimed**; these files are local artifacts.

## `StrategyGenome`

Defined in `WIP/Wip.Tholdem.Agents/Learning/StrategyGenome.cs`. It is the complete strategy: four
numbers and one prompt string. Nothing is executable.

| Field | Default | Domain | Meaning |
|---|---|---|---|
| `RaiseThreshold` | `0.72` | `[0, 1]` | estimated hand strength at or above which the agent bets/raises |
| `CallThreshold` | `0.45` | `[0, 1]` | estimated strength required to call (also compared with pot odds) |
| `BluffFrequency` | `0.12` | `[0, 1]` | probability-like threshold on the derived unit value for a bluff |
| `SizingMultiplier` | `1.0` | `[0.25, 3.0]` | scales the minimum legal bet/raise; clamped to `[MinRaiseTo, MaxRaiseTo]` |
| `PromptText` | "Play tight-aggressive heads-up: …" | any string | descriptive data carried with the genome |

`StrategyGenome.Clamp()` clamps every numeric field into its domain. `StrategyAgent` also clamps on
construction and interprets the numbers deterministically (`StrategyAgent.SizingTarget`,
`Wip.Tholdem.Agents/Learning/StrategyAgent.cs`); it does **not** read `PromptText`.

## `strategies/<id>.json` — the strategy artifact

Defined by `StrategyArtifact` (`WIP/Wip.Tholdem.Agents/Learning/StrategyArtifact.cs`). Example from
a real `train --iterations 1 --seed 7 --allow-weak-seed` run (seed `7` is a reproducible demo seed,
so it needs the explicit opt-in):

```json
{"id":"tholdem-strategy-v1-f100e793","version":1,"parentId":null,"createdAtIteration":0,"parameters":{"raiseThreshold":0.72,"callThreshold":0.45,"bluffFrequency":0.12,"sizingMultiplier":1},"promptText":"Play tight-aggressive heads-up: raise strong hands, fold weak hands to large bets, and size value bets by the sizing multiplier."}
```

| Field | Rule |
|---|---|
| `id` | `"tholdem-strategy-v" + version + "-" + first 8 lowercase hex chars of the content hash` |
| `version` | `1` for a root artifact, otherwise `parent.Version + 1` |
| `parentId` | the parent artifact id, or `null` for the version-1 root |
| `createdAtIteration` | the training iteration that proposed the artifact (`0` for the root) |
| `parameters` | the four genome numbers above |
| `promptText` | the genome prompt, persisted verbatim |

**Content-hash id rule.** The id cannot include its own hash, so the hash covers the canonical
payload *without* the id: `{"version":…,"parentId":…,"createdAtIteration":…,"parameters":{…},"promptText":…}`
(`StrategyArtifact.ToHashInputJson`) hashed with SHA-256 and rendered as lowercase hex. The persisted
document is that payload with `"id":…` prepended (`ToCanonicalJson`). `StrategyArtifact.Hash`
recomputes the hash from the payload; `StrategyArtifact.Create` derives the id from the same hash.

Readers (`TrainingLoop.ReadArtifact`, `WIP/Wip.Tholdem.Host/Storage/StrategyArtifactStore.cs`) check
that `id` matches the requested id and clamp the parameters; they do **not** recompute or verify the
hash on load. Treat these files as data.

## `ledger.json` — the promotion ledger

Defined by `TrainingLoop.BuildLedgerEntry` (`WIP/Wip.Tholdem.Agents/Learning/TrainingLoop.cs`). It
is a JSON array that **accumulates across runs**: `LoadLedger` reads the existing file and
`UpsertLedger` replaces an entry with the same `(iteration, championId, challengerId)` or appends
it. Example from the same real run:

```json
[{"iteration":1,"championId":"tholdem-strategy-v1-f100e793","challengerId":"tholdem-strategy-v2-b155b98f","promoted":false,"reason":"lcb-below-margin","winRate":0.5,"meanBbPer100":0,"lowerBoundBbPer100":0,"marginBbPer100":5,"seeds":24,"verified":true,"outcome":"rejected"}]
```

| Field | Meaning |
|---|---|
| `iteration` | 1-based training iteration |
| `championId` | the incumbent the challenger faced |
| `challengerId` | the proposed artifact |
| `promoted` | the final verdict after verification |
| `reason` | policy reason (`promoted`, `lcb-below-margin`, `insufficient-seeds`) or the verifier reason when verification fails |
| `winRate` | `challengerWins / (2 * seeds)` (two mirrored matches per seed) |
| `meanBbPer100`, `lowerBoundBbPer100`, `marginBbPer100`, `seeds` | the evaluation and criteria values the verdict used |
| `verified` | whether `PromotionVerifier.Verify` accepted the policy's verdict |
| `outcome` | `promoted`, `rejected`, or `verification-failed` |

## `evaluations/*.json` — two shapes

Both live under `<data>/evaluations/`.

**Training-loop report** (`<challengerId>-vs-<championId>.json`, `TrainingLoop.Run`) is the bare
canonical `EvaluationReport`:

```json
{"challengerId":"tholdem-strategy-v2-b155b98f","incumbentId":"tholdem-strategy-v1-f100e793","seeds":24,"handsPerMatch":10,"bigBlind":10,"meanBbPer100":0,"bbPer100Ci":{"low":0,"high":0},"lowerBoundBbPer100":0,"challengerWins":24,"incumbentWins":24,"ties":0,"challengerNetChips":0}
```

**CLI evaluation document** (`cli-<challenger>-vs-<incumbent>.json`, `evaluate`,
`WIP/Wip.Tholdem.Host/Storage/EvaluationFileStore.cs`) wraps that report with the criteria the
verdict must be re-derived against (schema `tholdem.cli-evaluation.v1`). Real example from
`evaluate --challenger tholdem-strategy-v2-b155b98f --seeds 4 --data-dir artifacts\tholdem-run`
(the default base seed is the minimum strong seed):

```json
{"schema":"tholdem.cli-evaluation.v1","criteria":{"marginBbPer100":5,"minSeeds":24},"report":{"challengerId":"tholdem-strategy-v2-b155b98f","incumbentId":"tholdem-strategy-v1-f100e793","seeds":4,"handsPerMatch":10,"bigBlind":10,"meanBbPer100":0,"bbPer100Ci":{"low":0,"high":0},"lowerBoundBbPer100":0,"challengerWins":4,"incumbentWins":4,"ties":0,"challengerNetChips":0}}
```

`EvaluationReport` fields (`WIP/Wip.Tholdem.Agents/Learning/EvaluationReport.cs`): `challengerId`,
`incumbentId`, `seeds`, `handsPerMatch`, `bigBlind`, `meanBbPer100`, `bbPer100Ci {low, high}`,
`lowerBoundBbPer100`, `challengerWins`, `incumbentWins`, `ties`, `challengerNetChips`. The training
loop and the CLI never overwrite each other because the file names differ by the `cli-` prefix.

## `active-champion.json`

Defined by `TrainingLoop.BuildChampionPointer` and `StrategyArtifactStore.WriteChampionPointer`:

```json
{"strategyId":"tholdem-strategy-v1-f100e793","iteration":0}
```

`strategyId` names `<data>/strategies/<strategyId>.json`. The training loop writes it on a fresh run
(pointing at the version-1 champion it actually used) and again on a promoted iteration with that
iteration number. The CLI `promote` path writes it with `"iteration":0`
(`StrategyArtifactStore.WriteChampionPointer`). Both readers treat a present-but-unreadable pointer
as a loud artifact error rather than silently starting from version 1
(`TrainingLoop.LoadChampion`, `StrategyArtifactStore.ReadChampionStrategyId`).

## Sessions and hand files

`PlayCommand` writes `<data>/sessions/<sessionId>/session.json` and `hand-NNNNN.json`
(`WIP/Wip.Tholdem.Host/Commands/PlayCommand.cs`): it points `MatchSimulator.Run` at a staging
directory (`<data>/sessions/.inprogress`) and moves that directory to `sessions/<sessionId>` on
success. `MatchSimulator.Run` writes the same file names only into the `HistoryDirectory` it is
given and never constructs a `sessions/<sessionId>` path itself
(`WIP/Wip.Tholdem.Agents/Simulation/MatchSimulator.cs`). The simulator runs two to six agents at one
table through `Run(IReadOnlyList<IPokerAgent>, SimulationOptions, CancellationToken)`; the two-agent
overload delegates to it, so every recorded two-seat byte is unchanged. The session id is
`"tholdem-"` plus the first 16 lowercase hex characters of the SHA-256 of the UTF-8 text
`<seed>\n<hands>\n<name0>\n…\n<nameN-1>` — for two seats exactly the recorded
`<seed>\n<hands>\n<name0>\n<name1>`.

**`session.json`** (schema `tholdem.session.v1`). A two-seat session with equal starting stacks is
written exactly as before the multiway change (no `seats` or `startingStacks` member) — real example
from `play --seed 777 --hands 2 --allow-weak-seed`:

```json
{"schema":"tholdem.session.v1","sessionId":"tholdem-f275b4ee69be0557","seed":777,"hands":2,"startingStack":1000,"smallBlind":5,"bigBlind":10,"agents":["tight-aggressive","random-legal"],"handHashes":["571049a9883fcc96d5ea0b85771030d63f66fe0169306d6a16f6bb39b7a8019b","92aa3db0e586fd3b9b31bb1d45777d507aa8f931b7b912aca1c14964ace547f3"]}
```

A multiway session (three to six seats) or a two-seat session with unequal starting stacks uses the
v2 layout: `"seats"` follows `"hands"`, the per-seat `"startingStacks"` array follows
`"startingStack"`, and `"startingStack"` is the first seat's stack. Real example, four seats,
`MultiwaySessionProbe` with per-seat stacks `[1000,800,600,400]`:

```json
{"schema":"tholdem.session.v1","sessionId":"tholdem-1ae40dc5dbb84f25","seed":4242,"hands":5,"seats":4,"startingStack":1000,"startingStacks":[1000,800,600,400],"smallBlind":5,"bigBlind":10,"agents":["probe-seat-0","probe-seat-1","probe-seat-2","probe-seat-3"],"handHashes":["127489d0acbb740aab233d7c387ace4a1c73952f1b737756361ced620a890fb7","befdff87fb3a8bbe9fea3ff7d644c407906e2949508bedc6487b64a458935c3a","57c51ad454649b7fcba8c021d64ffc5e3f274aa0b2af1684800e74c838181434","02fc2bc25e84134219992947abf9a298e819e2ad03e5ee365b6faef70c217112","618ea14b8d16018129768e254c6b175afca5b044d8e6419b6da80a30ec957c3c"]}
```

`handHashes[i]` is the SHA-256 (lowercase hex) of the canonical bytes of hand `i + 1`; `agents` are
the normalized agent names of every seat in seat order. A manifest written before the multiway
change has no `seats` member; `SessionManifest.Parse` treats it as a two-seat session (the recorded
default) and repeats `startingStack` for both seats, so old session directories remain readable.

**`hand-NNNNN.json`** — the canonical `HandRecord` form (`HandRecordCanonicalWriter`). A two-seat
hand with equal starting stacks keeps the recorded v1 form:

```json
{"handNumber":1,"seed":9754104818518126180,"smallBlind":5,"bigBlind":10,"startingStack":1000,"buttonSeat":0,"holeCardsSeat0":["4c","Js"],"holeCardsSeat1":["Tc","Kd"],"board":[],"actions":[{"street":0,"seat":0,"kind":"Fold","amount":0,"potAfter":0}],"showdown":false,"winners":[1],"potAwarded":15,"finalStacks":[995,1005]}
```

Three to six seats, or two seats with unequal starting stacks, write the v2 form with
`"schema":"tholdem.hand.v2"`, `"startingStacks"`, `"seats"`, the per-seat `"holeCards"` array and the
`"pots"` layer array. Real four-seat example (same probe session):

```json
{"schema":"tholdem.hand.v2","handNumber":1,"seed":7927181507143236163,"smallBlind":5,"bigBlind":10,"startingStack":1000,"startingStacks":[1000,800,600,400],"buttonSeat":0,"seats":4,"holeCards":[["Ks","Tc"],["Td","Ac"],["Jd","5s"],["Jc","Jh"]],"board":["4h","8c","5h","6d","Js"],"actions":[{"street":0,"seat":2,"kind":"AllIn","amount":0,"potAfter":615},{"street":0,"seat":3,"kind":"AllIn","amount":0,"potAfter":1015},{"street":0,"seat":0,"kind":"Call","amount":0,"potAfter":1610},{"street":0,"seat":1,"kind":"Call","amount":0,"potAfter":2200},{"street":1,"seat":1,"kind":"AllIn","amount":0,"potAfter":2400},{"street":1,"seat":0,"kind":"Raise","amount":400,"potAfter":0}],"pots":[{"amount":1600,"winners":[3]},{"amount":600,"winners":[2]},{"amount":400,"winners":[1]},{"amount":200,"winners":[0]}],"showdown":true,"winners":[0,1,2,3],"potAwarded":2800,"finalStacks":[200,400,600,1600]}
```

`seed` is the per-hand seed derived by `SeedDerivation.Derive(sessionSeed, hand, 0, 0, 0)`;
`actions[].amount` is the "to" total for `Bet`/`Raise` and 0 otherwise; `actions[].potAfter` is the
pot after the action and any immediate refund or award. `HandRecordJson.Parse` accepts the canonical
property names in any order and with arbitrary whitespace, and rejects every missing or wrongly
typed field with a `FormatException` carrying a reason token.

The simulator's report keeps the recorded `tholdem.report.v1` shape for every seat count: one
`metrics` entry per seat (with its `seat` index), and per-seat `netChips`/`bigBlindsPer100` measured
against that seat's own starting stack. Because the engine settles every pot layer, the per-seat
`netChips` always sum to exactly zero.

## Strategies are data, never executed

- The learning layer is scanned by `LearningSourceSafetyTests`: `Wip.Tholdem.Agents/Learning` must
  contain no `Process`, `HttpClient`, `git `, `Assembly.Load`, `Compile`, `CSharpScript` or
  `CodeDom`, and the artifact/prompt must remain JSON data.
- The deterministic `StrategyAgent` interprets the four numeric parameters only. `PromptText` is
  stored and round-tripped; no code path in the learning loop evaluates, compiles or sends it to a
  model.
- `TrainCommand` and `TrainingLoop` never call a model, a shell or the network; the only identity
  material is the seed and the content hash.
