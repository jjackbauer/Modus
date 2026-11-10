# Known limits

This page lists what `Wip.Tholdem` does not do and what cannot be verified from this machine. It is
part of the honest contract: where a check was not run, it says so instead of implying coverage.
**No publish, signing or deployment is performed or automated by this delivery.** The projects are
ordinary packable .NET artifacts; any package produced during review was scratch evidence and was
not delivered.

## Rules and scope

- **The engine, simulator, viewer and `play` are multiway (2–6 seats); the learning loop is not.**
  `HoldemHandEngine` deals and settles 2–6 seats with side pots, `MatchSimulator`/`play` record
  3–6-seat v2 hands, and the viewer/live API expose every seat. The learning loop
  (`train`/`evaluate`/`promote`) and the strategy evaluation remain **heads-up**: `TrainCommand` and
  `EvaluateCommand` still run two-agent matches, so a multiway strategy is not trained or promoted
  here. The two-seat path remains byte-identical to the pre-multiway engine. See
  [engine-rules.md](engine-rules.md).
- **The odd-chip split rule is defensive.** `SplitPot` gives an odd chip to the non-button seat, but
  in legal play a showdown pot is always even (equal contributions), so that branch is unreachable
  without calling `SplitPot` directly. It is a guard, not a claim about real odd-chip situations.
- **No real money, accounts or external betting APIs.** Chips are integers in a local simulation;
  there is no wallet, no identity, no payment path and no telemetry.
- **No model is called by the default path.** The training loop, the simulator, the baselines and
  the CLI are deterministic and offline. The runtime agent exists and is tested with scripted/mock
  models; see the live-model gate below.
- **Agent randomness is bound to the observation JSON.** Baselines and `StrategyAgent` derive each
  turn's randomness base from `PlayerObservation.ToCanonicalJson()` plus the hand/seat/action index
  (`SeedDerivation.DeriveAgentSeed`), never from a seed in the turn context. Changing the observation
  schema or its canonical formatting therefore changes every agent's choices and every recorded hand
  hash; it cannot change the deals, which come from the session seed through the unchanged
  `SeedDerivation.Derive`. See [engine-rules.md](engine-rules.md#observation-boundary).

## Threat model

The deal is deterministic from the session seed, so "the agent never sees the seed" is not the same
statement as "the deal is secret". This section states the precise claim; it replaces any earlier
absolute "non-invertible" wording in these documents.

**(a) Nothing agent-facing exposes the deal.** No agent API or agent-side artifact exposes the
session seed, the deck order, the undealt cards or the other seat's hole cards.
`PlayerObservation` carries the acting seat's own hole cards plus public state
(`WIP/Wip.Tholdem/History/PlayerObservation.cs`); `AgentTurnContext` is
`(HandNumber, Seat, ActionIndex)` (`WIP/Wip.Tholdem.Agents/AgentTurnContext.cs`); the baseline and
strategy agents derive their randomness from that observation
(`SeedDerivation.DeriveAgentSeed`); and the runtime-agent sources are scanned for the
hidden-information identifiers by `RuntimeSourceBoundaryTests`. Session manifests and
`hand-NNNNN.json` files do contain the seed and the full deal, but they are operator replay
artifacts written by `play`, never inputs to an agent.

**(b) A guessable seed defeats the deal.** Because the deal is deterministic from the session seed,
an adversary that holds its own observation can enumerate candidate seeds, recompute the deal and
reconstruct the deck — including the undealt cards and the other seat's hole cards — whenever the
seed space is small enough to search. No one-way digest protects this: the cards themselves are the
observable. In round 13, enumerating candidate seeds `0..20000` against the `Qs,4s` hand-1 cards of
`play --seed 42 --hands 3 --allow-weak-seed` produced 11 matches, the first `42`, in 388 ms; the
derived deck `Qs Tc 4s Jd 8h 8s 8d Th 6c` reproduced the opponent's `Tc,Jd` and the board.

**(c) Entry points require strong seeds.** `play`, `train` and `evaluate` refuse a seed below
`1,099,511,627,776` (2^40) with exit code 2 and the message
`seed <n> is below the minimum 1099511627776; use --allow-weak-seed for reproducible demos`
(`WIP/Wip.Tholdem.Host/SessionSeedPolicy.cs`). `POST /api/live` refuses the same seeds with HTTP 400
`{"error":"weak-seed","minimum":1099511627776}` unless the body says `"allowWeakSeed": true`
(`WIP/Wip.Tholdem.Web/SessionSeedPolicy.cs`); the viewer form generates a random 64-bit seed on load
and exposes the opt-in as a checkbox. `POST /api/training` and `POST /api/rsi` enforce the same
guard, and an opted-in weak seed is additionally capped (5 training iterations / 2 meta-iterations)
because a longer run amplifies deal inference. Weak seeds therefore exist only behind
`--allow-weak-seed` (CLI) or `"allowWeakSeed": true` (HTTP), for reproducible demos and tests.

**(d) Opting out is outside the secrecy guarantee.** An operator who disables or bypasses that guard
— passes `--allow-weak-seed`, sends `"allowWeakSeed": true`, or edits the entry point — is outside
the guarantee above. A weak seed is recoverable by enumeration, and the deal, the undealt cards and
the opponent's hole cards can then be reconstructed from what an agent observes.

## Local verification limitations

- **Local Windows VSTest is not usable in this environment.** `dotnet test` aborts at testhost
  startup with `System.ComponentModel.Win32Exception (5): Access is denied` in
  `SetParentProcessExitCallback` (documented in `docs/runbooks/operator-guide.md` and
  `docs/testing/test-status.md`; reproduced in earlier rounds). Local evidence therefore comes from
  `dotnet build`, the supplementary runner `.tmp/tholdem-verify/RunXunit` over the compiled test
  assembly, and direct CLI/HTTP execution. `dotnet test` was not run for this work.
- **No CI result is claimed here.** `Tests CI` cannot be triggered or observed from this machine (no
  GitHub CLI/token in this environment). The workflow change in `.github/workflows/tests-ci.yml` is
  a source edit; whether GitHub Actions runs it green is not verified by this document.
- **`%TEMP%` is denied for workspace-launched helpers.** On this machine apphost processes started
  from the workspace cannot create directories under `%TEMP%`
  (`UnauthorizedAccessException`/`EPERM`). Test scratch therefore lives under the compiled test
  output directory (`AppContext.BaseDirectory/test-temp/...`, deleted in `finally`), and headless
  browser/Playwright tooling must run with `TEMP`/`TMP` redirected into the workspace.
- **The viewer must not be run from `bin/Release`.** Running the web host from the build output
  locks the loaded assemblies and subsequently breaks `dotnet build` (RISK-006). Copy the output to
  a scratch directory first — the procedure is in [viewer.md](viewer.md).
- **Browser-only checks are not part of the suite in this working tree.** The keyboard model (Space
  pause/resume, arrow keys for replay, `1`–`5` view switching) and the 390 px responsive geometry were
  verified in headless Chromium during the viewer rounds and are recorded as browser evidence, not as
  facts in `Wip.Tholdem.Tests`. The suite in this working tree, not yet committed, drives the real
  HTTP surface and asserts markup/CSS contracts; it does not execute JavaScript.

## Learning-loop limitations

- **The shipped proposer is behaviourally inert against the baseline in observed runs.** With the
  default v1 champion, ±0.02 nudges to the bluff frequency or sizing multiplier produced exactly
  `meanBbPer100 = 0` / `lowerBoundBbPer100 = 0` over 24–50 paired seeds, so the gate rejected every
  challenger. A real promotion demo therefore starts from an explicit `strategyId` (0.70/0.70) with
  50 evaluation seeds; this exercises the promote path, not skill discovery. See
  [rsi.md §9](rsi.md#9-what-is-claimed-and-what-is-not).
- **The mini-RSI meta-loop refuses the default trajectory as a self-comparison.** When inner
  iterations do not promote, both proposers leave the champion unchanged; the loop reports
  `champion-vs-itself` instead of inventing a comparison. An eligible meta result is not automatic:
  it needs a pre-registered hit-rate improvement and a fresh-holdout non-inferiority check, and it
  still requires an explicit server-verified promote.
- **The prompt template is never evaluated.** It is bounded text data, not an executable prompt; a
  prompt-only proposer change is rejected as inert before spending evaluation.
- **The promotion ledger hash chain is unsigned.** It detects naive edits and broken links; an
  attacker with write access can recompute the chain. See
  [rsi-threat-model.md](rsi-threat-model.md) A4/A14.
- **`SessionAnalysis.Vpip` is always `0`.** `SimulationReport` does not carry action-level VPIP
  counts, so `StrategyAnalyzer.Analyze` reports VPIP as zero rather than fabricating it from unrelated
  metrics; the proposer never uses it (`WIP/Wip.Tholdem.Agents/Learning/SessionAnalysis.cs`). The
  runtime agent's opponent-statistics tool does compute VPIP from the hands it has seen through
  `SessionHandStore.BuildStatisticsFor`, but that is a different aggregate and does not feed the
  training loop.
- **The CLI has no `--min-seeds` flag.** `evaluate` always writes `minSeeds: 24` into its criteria,
  so an evaluation with `--seeds` below 24 cannot be promoted even if its lower bound clears the
  margin. Use `--seeds 24` or more for a promotable evaluation.
- **Promotion only moves a local pointer.** `promote --yes` rewrites `active-champion.json`; it does
  not merge code, tag, push, publish or deploy.

## Live-model path

- The live-model path is **gated**: `LiveModelGate.IsEnabled` requires `THOLDEM_LIVE_MODEL` to be
  `1` or `true` **and** `DEEPSEEK_API_KEY` to be non-empty
  (`WIP/Wip.Tholdem.Agents/Runtime/LiveModelGate.cs`). The gate never constructs a model, never
  reads the key value beyond an emptiness check, and its failure reason names the missing variable
  without printing the key.
- **That path is not exercised here without a key.** The runtime tests in this working tree, not yet
  committed, use scripted chat models and assert the hidden-information/permission boundaries; no live
  provider call is part of the local or CI-observable evidence reported in this work.
