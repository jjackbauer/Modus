# Training and promotion runbook

This runbook drives the offline CLI. All paths below are relative to the repository root; the host
executable is `WIP\Wip.Tholdem.Host\bin\Release\net10.0\Wip.Tholdem.Host.exe` after
`dotnet build Wip.slnx --configuration Release`. Artifacts default to `artifacts\tholdem` under the
current directory; pass `--data-dir <path>` to keep a run isolated. Everything is local; **no
release, publish or deploy is claimed or performed** by any of these commands.

`train`, `evaluate` and `play` refuse a session seed below `1,099,511,627,776` unless
`--allow-weak-seed` is passed (`SessionSeedPolicy`). The train example below deliberately uses the
small, reproducible demo seed `7`, so it passes the flag; a production run should use a strong seed
and omit it. `evaluate` defaults to the minimum strong seed, so it needs no flag.

## 1. Train

```powershell
WIP\Wip.Tholdem.Host\bin\Release\net10.0\Wip.Tholdem.Host.exe train --iterations 3 --seed 7 --allow-weak-seed --data-dir artifacts\tholdem-run
```

`TrainCommand` uses the loop defaults: 120 training hands per iteration, 24 held-out evaluation
seeds, 10 hands per match (`TrainingOptions`). One iteration:

1. the active champion (`StrategyAgent`) plays a seeded 120-hand match against the
   `TightAggressiveAgent` baseline;
2. `StrategyAnalyzer` reduces that match to `SessionAnalysis`;
3. `DeterministicStrategyProposer` moves exactly one genome parameter by `0.02` (direction chosen by
   the measured session and iteration parity) and clamps it;
4. `StrategyArtifact.Create` builds the challenger artifact (id and canonical payload);
   `TrainingLoop` persists it as `<data>/strategies/<challengerId>.json`;
5. `MatchEvaluator` runs the paired held-out evaluation against the champion and persists the report;
6. `StatisticalPromotionPolicy` decides; `PromotionVerifier` re-derives the criterion from the
   report;
7. the ledger entry is written, and **only** a verified promotion moves `active-champion.json`.

The loop prints one line per iteration plus the final champion:

```text
iteration=1 champion=tholdem-strategy-v1-f100e793 challenger=tholdem-strategy-v2-b155b98f promoted=false verified=true outcome=rejected meanBbPer100=0.00 lcb=0.00 seeds=24
iteration=2 champion=tholdem-strategy-v1-f100e793 challenger=tholdem-strategy-v2-e1ba56f7 promoted=false verified=true outcome=rejected meanBbPer100=1.67 lcb=-0.76 seeds=24
iteration=3 champion=tholdem-strategy-v1-f100e793 challenger=tholdem-strategy-v2-ea96d95d promoted=false verified=true outcome=rejected meanBbPer100=-1.25 lcb=-3.40 seeds=24
final champion: tholdem-strategy-v1-f100e793
```

`--json` prints the same run as `tholdem.cli-training.v1` instead. A fresh data directory gets a
version-1 champion pointer before iteration 1; a corrupt pointer is a loud error, never a silent
fallback (`TrainingLoop.LoadChampion`).

### Why a rejected iteration is normal

The gate is deliberately hard. A challenger produced by a ±0.02 parameter step may not clear the
default margin (for example, the observed seed-7 run had `meanBbPer100=0.00` and `lcb=0.00`);
the loop still records the challenger, its report and a `rejected` ledger row, and the champion
pointer stays where it was. Rejection is evidence that the gate works, not a failed run. A
`verified=true` rejection means the verifier agreed the report justified rejecting.

## 2. Evaluate

```powershell
WIP\Wip.Tholdem.Host\bin\Release\net10.0\Wip.Tholdem.Host.exe evaluate --challenger tholdem-strategy-v2-b155b98f --seeds 24 --data-dir artifacts\tholdem-run
```

`evaluate` loads `<data>/strategies/<challenger>.json` and either the `--incumbent` artifact or the
current champion pointer, runs `MatchEvaluator.Evaluate` on held-out seeds, writes
`<data>/evaluations/cli-<challenger>-vs-<incumbent>.json` (schema `tholdem.cli-evaluation.v1`,
criteria + report) and prints the `decision:` and `verifier:` lines. It **never** moves the champion
pointer. Options: `--hands` (default 10), `--margin` (default 5 bb/100), `--seed` (default
`1099511627776`, the minimum strong seed; a weaker value needs `--allow-weak-seed`), `--json`.

The CLI always writes `minSeeds: 24` into the criteria (there is no `--min-seeds` flag), so a
`--seeds` value below 24 can never be promoted even if its lower bound clears the margin. Use
`--seeds 24` or more for a promotable evaluation.

## 3. Promote

```powershell
WIP\Wip.Tholdem.Host\bin\Release\net10.0\Wip.Tholdem.Host.exe promote --challenger tholdem-strategy-v2-b155b98f --yes --data-dir artifacts\tholdem-run
```

`promote` reads the matching `cli-…json` document, re-derives the `StatisticalPromotionPolicy`
verdict from the persisted report and criteria, **always** runs `PromotionVerifier.Verify`, and then:

- without `--yes`, prints `promotion requires explicit confirmation; re-run with --yes` and, if the
  report does not justify promotion, `promotion refused: <reason>`; exits 3 and changes nothing;
- with `--yes` but a verdict or verification the report does not justify, prints
  `promotion refused: <reason>` / `verifier: <reason>` and exits 3;
- only `verified && promoted && --yes` rewrites `<data>/active-champion.json` and exits 0.

There is no auto-merge, no push, no release and no deployment: the only effect is the local pointer
file (`Commands/PromoteCommand.cs`).

## The gate criterion

`EvaluationCriteria` (`WIP/Wip.Tholdem.Agents/Learning/EvaluationCriteria.cs`) defaults:

```text
MarginBbPer100 = 5.0 bb/100
MinSeeds       = 24
Z              = 1.959963984540054   (97.5% normal quantile)
```

`MatchEvaluator.Evaluate` (`WIP/Wip.Tholdem.Agents/Learning/MatchEvaluator.cs`) is paired:

- for each held-out seed, the challenger plays **two** matches against the incumbent, once in each
  seat, with the same cards; seat variance cancels inside the pair;
- `net = challengerNet(forward) + challengerNet(reverse)`;
- `perSeedBbPer100 = net / bigBlind / (2 * handsPerMatch) * 100`;
- `mean` is the mean of the per-seed values; `halfWidth = Z * sampleStdDev / sqrt(seeds)`;
- `LowerBoundBbPer100 = mean - halfWidth` and is published as `bbPer100Ci.low`.

Promotion requires **both**, strictly:

```text
report.LowerBoundBbPer100 > criteria.MarginBbPer100   // exactly at the margin is not a promotion
report.Seeds >= criteria.MinSeeds
```

### Held-out seeds

Evaluation seeds are derived, never reused from training
(`WIP/Wip.Tholdem.Agents/SeedDerivation.cs`):

- training match seed: `Derive(runSeed, 1_000 + iteration, 0, 0, stream 1)`;
- evaluation base seed: `Derive(runSeed, 5_000 + iteration, 0, 0, stream 3)`;
- per-evaluation seed: `Derive(baseSeed, 9_000 + index, 0, 0, 0)`.

`evaluate` derives its per-seed values the same way from `--seed` (default `1099511627776`).

### The verifier and the always-promote control

`PromotionVerifier.Verify` (`WIP/Wip.Tholdem.Agents/Learning/PromotionVerifier.cs`) ignores what the
policy claimed and re-derives both conditions **from the evaluation report alone**:

- a `Promoted` verdict without `LCB > margin` is refused with `verifier:lcb-below-margin`;
- a `Promoted` verdict with fewer than `minSeeds` seeds is refused with `verifier:insufficient-seeds`;
- a rejection that the report contradicts (`LCB > margin` and enough seeds) is refused with
  `verifier:unjustified-rejection`.

`AlwaysPromotePolicy` exists only as the negative control: it promotes unconditionally. Because the
training loop (`TrainingLoop.Run`) and `promote` always run the verifier, an always-promote verdict
over a failing report becomes `verification-failed`, the champion pointer does not move, and the
CLI refuses with exit 3. `PromotionGateTests` and `TrainingLoopTests` prove that path
(`Verify_GivenRederivedCriterion_ExpectedControlsRejectedAndPassesAccepted`,
`Run_GivenAlwaysPromotePolicy_ExpectedVerificationFailureAndUnchangedChampion`).

## Inspect the ledger

- Raw: `<data>/ledger.json` is a JSON array (see
  [strategy-artifact-schema.md](strategy-artifact-schema.md#ledgerjson--the-promotion-ledger)); the
  loop accumulates entries across runs and de-duplicates on
  `(iteration, championId, challengerId)`.
- Viewer: the **Ledger** tab reads `GET /api/ledger` and shows Iteration, Champion, Challenger,
  Promoted, Reason, Win rate, bb/100, LCB Δ and Samples. The **Strategies** tab reads
  `GET /api/strategies` and marks the champion. Start the viewer as described in
  [viewer.md](viewer.md).
- CLI: `evaluate --json` prints the whole CLI evaluation document; `train --json` prints every
  iteration's report.

## Reproduce a run

1. Build the solution in Release.
2. Use a **fresh** data directory and the same `--seed`, `--iterations`, `--allow-weak-seed` (when
   the seed is a demo seed) and defaults. There is no
   clock, GUID or ambient randomness in the learning loop. Agent choices are deterministic functions
   of the agent's own observation plus the hand/seat/action coordinates
   (`SeedDerivation.DeriveAgentSeed`; the turn context carries no seed), so the strategy artifacts,
   evaluation reports, ledger and champion pointer are byte-identical across two runs with the same
   inputs
   (`TrainingLoopTests.Run_GivenSameOptionsIntoTwoDirectories_ExpectedIdenticalArtifactsReportsLedgerAndChampion`).
3. Compare file hashes (`Get-FileHash` or SHA-256) or simply diff the directories.
4. Re-running into a directory that already has a ledger appends/deduplicates rather than
   overwriting, so use a copy of the directory if you want a clean reproduction.

## Exit codes

`0` success; `2` usage error (bad option/value, or a seed below `1,099,511,627,776` without
`--allow-weak-seed`); `3` refused (promotion without confirmation or without a justifying report);
`4` missing or invalid artifact (absent strategy, pointer, evaluation or hand record); `5` replay
invariant failure (non-canonical hand bytes, anchor mismatch, failed replay, a session manifest that
is not valid JSON, or a manifest with no entry for the hand).
