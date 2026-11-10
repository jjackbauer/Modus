# UI-triggered training and the bounded mini-RSI loop

This document describes the training job API, the mini-RSI meta-loop, what they establish and —
more importantly — what they do **not** establish. It documents the code in this working tree.

> **Honest one-line statement.** This is a *bounded, server-gated search* over strategy data and over
> a small proposer policy. It can promote a challenger only when a frozen statistical gate and a
> frozen verifier agree on a held-out evaluation. It is **not** unbounded self-improvement, it does
> **not** establish poker strength, and it does **not** evaluate the LLM prompt template at all.

## 1. Training API

The viewer host (`WIP/Wip.Tholdem.Web`) owns a single-consumer background queue
(`JobScheduler` + `BackgroundJobWorker`). A request validates its options and enqueues work; it never
runs the loop on the request thread. Only one job — training or mini-RSI — runs per process.

| Method and path | Behaviour |
|---|---|
| `POST /api/training` | body `{seed, iterations, trainingHands, evaluationSeeds, evaluationHands, marginBbPer100, minSeeds, strategyId?, allowWeakSeed?}` → `202 {jobId, acceptedAtUtc, optionsHash}` |
| `GET /api/training/{jobId}` | job snapshot: `status`, `phase`, `iteration`, `totalIterations`, `championId`, `challengerId`, `lastReport`, `entries[]`, `error?`, plus `canPromote`, `promotionStatus`, `weakSeed` |
| `GET /api/training` | up to 50 recent jobs and the active job id |
| `DELETE /api/training/{jobId}` | cooperative cancel while queued/running → `202`; terminal → `409` |
| `POST /api/training/{jobId}/promote` | body `{confirm:true}`; server re-verifies from disk (see §3) |

Bounds (`TrainingJobValidator`): `iterations 1..50`, `trainingHands 1..2000`,
`evaluationSeeds 2..500`, `evaluationHands 1..50`, `marginBbPer100 0..100`, `minSeeds 2..1000`.
An out-of-range value returns `400 {error:"invalid-option", field, message, min?, max?}`.

- **Deterministic identity.** `jobId = "train-" + optionsHash[..16]`, where `optionsHash` is SHA-256
  over the canonical accepted options in a fixed key order. The data directory and the clock are not
  part of it. Re-submitting the same options returns the same job (`202`) instead of a second run.
- **Seed reuse is refused.** A persisted register (`training-seeds.json` in the data directory) maps
  each base seed to its `optionsHash`. The same seed with different options returns
  `409 {error:"seed-reuse"}`. This stops an operator from fishing for a favourable verdict over the
  same deals. Same options are allowed (idempotent rerun).
- **Weak seeds.** A seed below `1,099,511,627,776` needs `allowWeakSeed:true`; moreover such a run is
  capped at **5** iterations (`400 {error:"weak-seed-iteration-cap"}`) because a long run amplifies
  the deal-inference problem rather than mitigating it.
- **Phases.** `self-play`, `analyze`, `propose`, `evaluate`, `gate`, `verify`, `persist`. The loop
  reports a phase before it performs it and a completed iteration after the ledger/pointer write, so
  the UI sees a live timeline. Cancellation is observed at every phase boundary and **before** any
  persist, so a cancelled iteration writes no artifact and cannot promote.
- **Artifacts** land in the same data directory the viewer reads: `strategies/<id>.json`,
  `evaluations/<challenger>-vs-<champion>.json`, `ledger.json`, `active-champion.json`.

## 2. Ledger integrity

`TrainingLoop` keeps its accumulating ledger semantics (an entry is keyed by
`iteration + championId + challengerId`, reruns replace rather than duplicate), but every entry is now
wrapped in a hash chain: `entryHash = SHA-256(previousHash + "|" + coreJson)`, the first entry links to
a genesis hash, and the whole array is re-emitted with its chain on each write.

- `TrainingLedger.Verify(dataDirectory)` returns `Verified`, counts and the head hash.
- `TrainingLoop.Run` verifies an existing ledger **before** appending; a broken chain refuses the run.
- Entries written before this chain existed are accepted only as an unanchored prefix; the next write
  upgrades the whole ledger.
- This is tamper-**evident**, not tamper-proof: the construction is public and unsigned, so an
  attacker with write access can recompute the entire chain. See A4 in the
  [threat model](rsi-threat-model.md).

## 3. Promote: a second, read-back phase

`POST /api/training/{jobId}/promote {confirm:true}` never trusts the UI or the job's cached
"eligible" flag. In order it:

1. requires `confirm:true` (`409 confirmation-required`);
2. requires a completed job (`409 job-not-complete`) and a last iteration whose frozen verifier
   passed **and** whose policy decision promoted (`409 report-not-promotable`);
3. re-reads `evaluations/<challenger>-vs-<champion>.json` and requires it to parse **canonically**;
4. requires the report's ids to match the job's last iteration;
5. requires its hash to equal the report hash recorded in memory when the iteration completed;
6. re-reads both strategy artifacts and checks their content-hash ids;
7. **recomputes the evaluation** from those artifacts and the job's seed/iteration with
   `MatchEvaluator` (the frozen evaluator) and compares the recomputed hash;
8. re-runs `StatisticalPromotionPolicy` and `PromotionVerifier` with the job's criteria;
9. verifies the training ledger's hash chain;
10. refuses a stale promotion if the active champion has moved to a third strategy
    (`409 champion-changed`); if the champion is already the challenger it returns
    `200 already-promoted`.

**Nuance, stated plainly:** the standard loop applies its own verified promotions to
`active-champion.json` during the run (that is its existing semantics). The endpoint is therefore an
independent server-side audit and recovery path, not the *only* writer of the pointer. It can repair a
lost pointer and it refuses a tampered or unverifiable report. It cannot be used to promote a report
that the frozen gate did not already justify; there is no "force" parameter.

## 4. Mini-RSI (bounded meta-loop)

### Data

- `ProposerGenome { MutationStep, ParameterSelectionPolicy, EvaluationBudgetShare, PromptTemplate }`,
  clamped to `MutationStep 0.001..0.25`, share `0.1..1.0`, prompt ≤ 2000 characters with control
  characters removed.
- `MetaArtifact`: content-hashed, versioned, parent-linked, exactly like `StrategyArtifact`
  (`tholdem-proposer-v<version>-<hash8>`).
- The **prompt template is text data only**. `PromptTemplateRenderer` does literal replacement of
  `{raiseThreshold}`, `{callThreshold}`, `{bluffFrequency}`, `{sizingMultiplier}`, `{step}` and
  `{iteration}`; unknown placeholders stay untouched. It is never executed, parsed as a template
  language or sent to a model.

### Iteration

Each meta-iteration:

1. re-captures the **frozen manifest** (§5) and refuses on mismatch;
2. deterministically proposes one new `ProposerGenome` from the incumbent
   (`DeterministicMetaProposer`); a candidate whose numeric behaviour equals the incumbent is refused
   **before any evaluation** (`inert-proposer`);
3. runs the standard `TrainingLoop` for `InnerIterations` iterations in a scratch directory, once per
   meta seed, for the candidate proposer and the incumbent proposer;
4. refuses a self-comparison when every candidate episode ends on the same champion genome as its
   incumbent episode (`champion-vs-itself`), before spending evaluator time;
5. builds the primary report by pairing the candidate and incumbent final champions on **one paired
   match per meta seed** (both seat orders, same deal) and aggregating the per-seed means with the
   same normal interval (`Z` from `EvaluationCriteria`); the gate is the same
   `StatisticalPromotionPolicy` + `PromotionVerifier` with the same criteria;
6. computes the pre-registered hit-rate improvement = verified promotions per inner iteration, paired
   by meta seed, with its own normal interval; the lower bound must exceed
   `RequiredHitRateImprovement`;
7. runs a **fresh holdout**: two fresh episodes and a fresh comparison whose seeds were never used in
   selection, and requires non-inferiority `fresh LCB >= -margin` (the ratchet against a meta-overfit
   proposer);
8. persists a canonical `MetaEvaluationDocument` and appends a hash-chained entry to
   `meta/meta-ledger.json`.

An eligible result does **not** move the pointer. `POST /api/rsi/{jobId}/promote {confirm:true}`
re-reads the persisted document, checks its hash, re-runs `MetaDecisionVerifier` and the frozen
manifest, checks the active proposer has not moved, and only then writes
`meta/active-proposer.json`. A meta promotion **never** writes `active-champion.json`.

### Seed domains (disjoint by construction)

| Purpose | Derivation |
|---|---|
| inner training per iteration | `Derive(jobSeed, 1000 + i, 0, 0, 1)` |
| inner evaluation per iteration | `Derive(jobSeed, 5000 + i, 0, 0, 3)` |
| meta episode seed | `Derive(jobSeed, 20000 + metaIndex, 0, 0, 7)` |
| meta comparison seed | `Derive(jobSeed, 30000 + metaIndex, 0, 0, 7)` |
| fresh episode seed | `Derive(jobSeed, 40000, 0, 0, 11)` |
| fresh comparison seed | `Derive(jobSeed, 45000, 0, 0, 11)` |

`MetaSeedDerivation.EpisodeSeedSet` materialises the meta values plus every inner seed the episode
derives and throws if any two are equal. The test suite asserts determinism and distinctness.

### Budgets

`MetaRunOptions` bounds: `metaIterations 1..8`, `innerIterations 1..5`, `evaluationSeeds 2..50`,
`metaSeeds 2..50`, `freshSeeds 2..50`, `requiredHitRateImprovement 0..1`, `maxInnerIterations`,
`maxEvaluationsPerEpisode`, wall clock (5–600 s at the HTTP boundary). Exceeding an episode-count or
wall-clock budget raises `MetaBudgetExceededException`; the job fails (`budget-exceeded:<budget>`)
and the proposer pointer is unchanged. Cancellation throws `OperationCanceledException`; the job ends
`cancelled` and the pointer is unchanged.

## 5. Frozen gate and manifest

The following are frozen for the meta-loop:

- `EvaluationCriteria` (`WIP/Wip.Tholdem.Agents/Learning/EvaluationCriteria.cs`)
- `PromotionPolicy` (`PromotionPolicy.cs`)
- `PromotionVerifier` (`PromotionVerifier.cs`)
- `MatchEvaluator` (`MatchEvaluator.cs`)
- `SeedDerivation` (`WIP/Wip.Tholdem.Agents/SeedDerivation.cs`)

`FrozenComponents.Capture()` computes two digests per component:

- the SHA-256 of canonical source text (no BOM, LF line endings, trailing blanks trimmed), when the
  repository sources are reachable from the running assembly;
- a deterministic **behaviour probe**: criteria defaults; verifier verdicts on crafted reports; policy
  verdicts; one `MatchEvaluator` run with a fixed stub agent pair, seed, seed count and hand count;
  fixed `SeedDerivation` vectors.

The committed pinned constant is version `tholdem-rsi-frozen-v1`, manifest hash
`95e010703d8ef7cd4d7cc2fae4ca839f7dd80b4c99b7645c7ac3d111ca748a5d`. Every meta-iteration recaptures
the manifest and refuses to continue on any source or behaviour mismatch. From a copied build output
without source files the source fields are skipped (`sourcePinned:false`) and the behaviour probes are
still required.

This is drift detection and policy-substitution detection, **not** a signature chain: an attacker who
edits a frozen source file *and* the pinned constant in `FrozenComponents.cs`, then rebuilds, is not
detected. See A14.

## 6. HTTP surface for mini-RSI

| Method and path | Behaviour |
|---|---|
| `POST /api/rsi` | optional `{seed, metaIterations, innerIterations, trainingHands, evaluationSeeds, evaluationHands, marginBbPer100, minSeeds, metaSeeds, metaHands, freshSeeds, freshHands, requiredHitRateImprovement, maxInnerIterations, maxEvaluationsPerEpisode, maxWallClockSeconds, strategyId?, allowWeakSeed?}` → 202 |
| `GET /api/rsi/{jobId}` | snapshot with `phase`, `incumbentProposerId`, `candidateProposerId`, `entries[]`, `lastReport`, `eligibleProposerId`, `canPromote`, `frozenManifestHash` |
| `GET /api/rsi` | recent mini-RSI jobs |
| `DELETE /api/rsi/{jobId}` | cancel |
| `POST /api/rsi/{jobId}/promote` | re-verified proposer promotion (never the strategy champion) |

A weak seed caps a meta run at 2 meta-iterations.

## 7. How to run

```powershell
dotnet build WIP/Wip.Tholdem.Web/Wip.Tholdem.Web.csproj --configuration Release
$source = 'WIP\Wip.Tholdem.Web\bin\Release\net10.0'
$target = '.tmp\tholdem-viewer'
Remove-Item $target -Recurse -Force -ErrorAction SilentlyContinue
Copy-Item $source $target -Recurse
dotnet "$target\Wip.Tholdem.Web.dll" --urls http://127.0.0.1:5099 --data-dir artifacts\tholdem-run
```

Then open `http://127.0.0.1:5099/viewer/` and use the **Training** tab. It has a standard training\nform and, below the champion card, a compact **Mini-RSI (proposer policy)** form that starts/cancels a\nbounded meta run and offers an explicit proposer promote when the server reports an eligible result.\nYou can also call the API directly:

```powershell
$body = @{ seed = 1099511627790; iterations = 1; trainingHands = 120; evaluationSeeds = 50;
           evaluationHands = 10; marginBbPer100 = 0; minSeeds = 2; allowWeakSeed = $false } |
        ConvertTo-Json
$accepted = Invoke-RestMethod -Method Post -Uri http://127.0.0.1:5099/api/training `
              -ContentType 'application/json' -Body $body
$accepted.jobId
# poll GET /api/training/{jobId} until status is complete
Invoke-RestMethod -Method Post -Uri "http://127.0.0.1:5099/api/training/$($accepted.jobId)/promote" `
  -ContentType 'application/json' -Body '{"confirm":true}'
```

Never run the host from `bin/Release` (the loaded assemblies lock the next build); always copy first.

## 8. Tests

All tests are plain xUnit in `WIP/Wip.Tholdem.Tests`. Local `dotnet test` aborts with
`Win32Exception (5)` on this machine, so the supplementary runner executes the same compiled assembly:

```powershell
dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj --configuration Release
.tmp/tholdem-verify/RunXunit/bin/Release/net10.0/RunXunit.exe `
  WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
```

The full Tholdem suite is **317 tests** (275 pre-existing + 42 new). The new suites are:

| Suite | Covers |
|---|---|
| `TrainingJobApiTests` | 202/400/409, deterministic job id, one-at-a-time, progress latency, cancellation without promotion, seed reuse, artifact landing, identical hashes across directories |
| `TrainingPromoteApiTests` | confirm required, server recomputation, tampered report refused, report/job mismatch, unverified report refused, successful pointer write |
| `RsiJobApiTests` | mini-RSI bounds, completion, frozen hash exposed, strategy champion untouched, ineligible promote refused |
| `MetaLoopTests` | identical/inert proposer, frozen mismatch, self-comparison, fresh-holdout overfit, eligibility without pointer change, budgets, cancellation, hit-rate literal CI, verifier, seed domains, artifact hash, prompt bounds |
| `ThreatControlTests` | always-promote control, ledger tamper, inert proposal guard, prompt-injection behavioural equivalence, frozen manifest match/mismatch, canonical source, meta ledger tamper, tampered starting artifact |
| `TrainingUiAssetTests` | tab/form element contract, textContent-only, no external assets, confirmation flow |

The mutation evidence (defense removed → focused control fails; file restored by SHA-256) is recorded
in the [threat model](rsi-threat-model.md) and `docs/goal-rsi/round-1/mutation-evidence.md`.

## 9. What is claimed, and what is not

**Claimed (verified by tests and a real loopback run):**

- The learning loop is triggerable, observable and cancellable from the loopback viewer; one job runs
  at a time; progress is visible on the first poll.
- A promotion requires a held-out paired evaluation whose lower confidence bound clears the margin
  and whose seed count meets the minimum; the frozen verifier re-derives that verdict.
- The promote endpoint refuses a UI confirmation alone, an unverified report, a report for another
  challenger and a coherently edited report (server-side recomputation).
- Strategy artifacts, evaluation reports and the ledger are deterministic byte-for-byte for the same
  options in two fresh data directories.
- The training ledger and the meta ledger are hash-chained; a naive edit is detected and refuses the
  next run.
- The meta-loop evolves proposer data only, re-checks a frozen manifest each iteration, refuses
  identical/inert proposers before evaluation, refuses a champion-versus-itself comparison, requires
  a pre-registered hit-rate improvement with an interval, and requires a fresh-holdout ratchet.
- The prompt template is stored as bounded text and never executed or evaluated.

**Not claimed:**

- **Poker strength.** The objective is mean bb/100 against one fixed baseline bot on held-out seeds.
  That is a proxy, not a claim about real poker, and it can be reward-hacked in principle (A1).
- **That the loop finds improvements in general.** With the shipped v1 champion the deterministic
  proposer's ±0.02 nudges were behaviourally inert in observed runs: evaluation means of exactly
  `0.000` over 24–50 paired seeds. The recorded promotion demonstration used an explicit
  `strategyId` starting artifact with thresholds `0.70/0.70` and 50 evaluation seeds; it exercises the
  promotion path, it does not show that the loop discovers skill from the default champion.
- **Offline evaluation of the prompt.** None exists; the meta gate is blind to prompt-only changes,
  which is why such a candidate is rejected as inert by design.
- **Unbounded self-improvement.** The meta-loop is bounded by meta-iterations, inner iterations,
  per-episode evaluation counts and wall clock; it proposes one small parameter change per iteration.
- **CI green.** CI cannot be triggered or observed from this machine. Local evidence only.
- **Publish/merge/deploy.** None is performed; the projects remain unpackaged local artifacts.
- **Multiway learning.** The loop remains heads-up by design.
