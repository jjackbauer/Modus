# `Wip.Tholdem` documentation

`Wip.Tholdem` is a deterministic, offline, 2–6 seat no-limit Texas Hold'em domain with side pots: an
engine, three baseline bots, an LLM runtime agent, a statistically gated learning loop (still
heads-up), a local spectator viewer with a UI-triggered training tab, a bounded mini-RSI meta-loop over
proposer data, and a CLI. This directory documents the code as it exists in this working tree. Every
behavioural claim below names the source file that implements it.

**No publish, signing or deployment is performed or automated by this delivery.** The projects are
ordinary packable .NET artifacts; any package produced during review was scratch evidence and was
not delivered. The CLI and the viewer are local developer tools run from a build output.

| Document | What it covers |
|---|---|
| [engine-rules.md](engine-rules.md) | Exact rules: blinds and order, action semantics, min-raise/all-in, streets, dealing, showdown, split pot, chip conservation, observation boundary |
| [strategy-artifact-schema.md](strategy-artifact-schema.md) | Genome fields, strategy artifact JSON and content-hash id, ledger, evaluation documents, champion pointer, session/hand files |
| [training-promotion-runbook.md](training-promotion-runbook.md) | `train` → `evaluate` → `promote --yes`, the gate criterion and verifier, inspecting the ledger, reproducing a run |
| [rsi.md](rsi.md) | UI-triggered training API, deterministic job ids, seed-reuse refusal, server-side promote, bounded mini-RSI, frozen manifest, budgets, how to run, what is and is not claimed |
| [rsi-threat-model.md](rsi-threat-model.md) | Adversarial analysis A1–A15 with severity, defense, committed control test, and the honest residual limits of this loop |
| [known-limits.md](known-limits.md) | Threat model and seed policy, learning loop still heads-up, local VSTest and `%TEMP%` limitations, viewer build lock, VPIP = 0, browser-only checks, live-model gate, no real money |
| [viewer.md](viewer.md) | Running the spectator viewer safely, tabs/controls, data directory, HTTP endpoints, spectator vs agent visibility |

## Quick start (local, offline)

```powershell
dotnet build Wip.slnx --configuration Release
WIP\Wip.Tholdem.Host\bin\Release\net10.0\Wip.Tholdem.Host.exe --help
```

## CLI commands

All commands are dispatched by `WIP/Wip.Tholdem.Host/TholdemCli.cs`; artifacts default to
`<current directory>\artifacts\tholdem` and can be redirected with `--data-dir <path>`.

```text
tholdem play --seed <n> [--hands <n>=200] [--seats <2..6>=2] [--allow-weak-seed]
             [--agents <name>[,<name>...]] [--starting-stacks <n>[,<n>...]]
             [--agent0 <random-legal|tight-aggressive|pot-odds-equity>]
             [--agent1 <random-legal|tight-aggressive|pot-odds-equity>]
             [--json] [--data-dir <path>]
tholdem train --iterations <n> --seed <n> [--allow-weak-seed] [--json] [--data-dir <path>]
tholdem evaluate --challenger <id> --seeds <n> [--hands <n>=10]
             [--incumbent <id>] [--margin <bb/100>=5]
             [--seed <n>=1099511627776] [--allow-weak-seed]
             [--json] [--data-dir <path>]
tholdem promote --challenger <id> [--incumbent <id>] [--yes] [--data-dir <path>]
tholdem replay <hand.json> [--expected-hash <hex>] [--data-dir <path>]
tholdem retention --status [--json] [--data-dir <path>]
tholdem retention --purge --older-than-days <n> [--yes] [--data-dir <path>]
tholdem --help | --version
```

`play`, `train` and `evaluate` require a session seed at or above `1,099,511,627,776` because the
deal is deterministic from that seed and a guessable seed can be recovered from an observer's own
cards; a lower seed is accepted only with `--allow-weak-seed`, for reproducible demos and tests. See
the [threat model](known-limits.md#threat-model).

- `play` runs a reproducible 2–6 seat match between the deterministic baseline bots and writes
  `sessions/<sessionId>/session.json` plus `hand-NNNNN.json` (`Commands/PlayCommand.cs`). `--seats`
  defaults to 2; `--agents` names one agent per seat and takes precedence over
  `--agent0`/`--agent1`; `--starting-stacks` optionally gives each seat its own stack (each at least
  the big blind, `10`). A 3–6 seat session writes v2 hand records.
  ```powershell
  tholdem play --seats 4 --seed 42 --hands 20 --agents random-legal,tight-aggressive,pot-odds-equity,random-legal --starting-stacks 1000,800,600,400 --allow-weak-seed
  ```
- `train` runs the gated learning loop and persists strategies, evaluation reports, the ledger and
  the champion pointer (`Commands/TrainCommand.cs`).
- `evaluate` runs one paired, held-out evaluation and writes
  `evaluations/cli-<challenger>-vs-<incumbent>.json`; it never moves the champion
  (`Commands/EvaluateCommand.cs`).
- `promote` re-verifies the persisted evaluation, requires `--yes`, and only then rewrites
  `active-champion.json` (`Commands/PromoteCommand.cs`).
- `replay` re-executes a recorded hand. The file must be byte-identical to its canonical form; the
  bytes are then checked against the session manifest (`handHashes[handNumber - 1]`) or an explicit
  `--expected-hash`, and a file with neither says `integrity: unanchored`
  (`Commands/ReplayCommand.cs`).
- `retention` reports or purges old session directories (`Commands/RetentionCommand.cs`); it is the
  only command that reads the clock.

Exit codes are fixed by `TholdemCli`: `0` ok, `2` usage error, `3` refused, `4` missing or invalid
artifact, `5` replay invariant failure.
