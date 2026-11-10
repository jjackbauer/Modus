# Final report — Tholdem continual-harness refinement

> Goal: add an opt-in, data-only harness-refinement path to the Tholdem learning loop that is byte-identical when disabled, leaves the frozen statistical gate untouched, and measurably increases proposal exploration in a pre-registered experiment. Every claim below is backed by an `EVD` row in `evidence.md` and a command or file in the goal directory.

## Outcome

**Met.** The Tholdem learning loop now has a complete, opt-in refinement path: trajectory capture, deterministic failure signatures, canonical memory, verified lineage, bootstrap modes, deterministic skills, skill refinement, an ablation runner, and a refiner-policy field the meta-loop can evolve. The shipped refinement arm passed the pre-registered holdout rule; the legacy mode, the five frozen gate components and the recorded artifact hashes are unchanged.

| Check | Result |
|---|---|
| Full test assembly | **387/387**, 0 failed, 0 skipped (round 9) |
| Legacy CLI equivalence | **7/7** JSON hashes in two fresh directories (round 9) |
| Five frozen gate components | Hashes unchanged from round 1 |
| Default meta artifact | `tholdem-proposer-v1-24c2109a`, 344 bytes, hash `24c2109a…` unchanged |
| Default meta document | 1545 bytes, hash `2f2888d5…` unchanged |
| Pre-registered holdout | Success rule met: median ratio **6.53×**, CI **[4.53, 11.5]**, zero-effect **9/30 → 0/30** |
| Promotions | 0/30 in both experiment arms — no poker-strength claim |

## Definition of done

| # | Dimension | Status | Evidence |
|---|---|---|---|
| D1 | Legacy equivalence | Covered | EVD-009, EVD-010, EVD-011, EVD-031, EVD-063 |
| D2 | Trajectory capture and signatures | Covered | EVD-005, EVD-006, EVD-015, EVD-016 |
| D3 | Memory and lineage | Covered | EVD-027, EVD-028, EVD-029, EVD-032 |
| D4 | Policy and bootstrap | Covered | EVD-014, EVD-035, EVD-038 |
| D5 | Guards and safety | Covered | EVD-036, EVD-046 |
| D6 | Skills | Covered | EVD-040, EVD-042, EVD-043, EVD-044, EVD-046 |
| D7 | Ablation | Covered | EVD-048, EVD-050, EVD-051, EVD-052 |
| D8 | Refiner evolution | Covered | EVD-054, EVD-056, EVD-058 |
| D9 | Significant experiment | Covered | EVD-023 to EVD-026 |

## The experiment

### DEV (six seeds, pre-registered primary comparison)

| Metric | Baseline | Refinement |
|---|---:|---:|
| Mean decision divergence | 1.81% | **9.83%** |
| Median per-seed ratio | — | **9.0×** |
| Bootstrap 95% CI | — | **[6.75, 11.5]** |
| Zero-effect evaluations | 7/30 | **0/30** |
| Verified promotions | 0/30 | 0/30 |

### HOLDOUT (six untouched seeds, one attempt)

| Metric | Baseline | Refinement |
|---|---:|---:|
| Mean decision divergence | 1.81% | **9.14%** |
| Median per-seed ratio | — | **6.53×** (two zero-baseline medians used the pre-registered pooled-mean substitution) |
| Bootstrap 95% CI | — | **[4.53, 11.5]** |
| Zero-effect evaluations | 9/30 | **0/30** |
| Verified promotions | 0/30 | 0/30 |

### Step-size control (DEV only, not pre-registered)

A `legacy-large` arm with the same 0.08 step but no signatures reached 7.83% divergence and 7/30 zero-effect. Refinement vs control was not a reliable divergence increase (median 1.09×, CI [0.80, 1.94]), but it rescued **7/7** of the exact iterations where the baseline and the large-step control both changed nothing, and eliminated the zero-effect evaluations (Fisher exact p ≈ 0.0105).

**Honest interpretation:** the loop explores more and stops producing zero-effect proposals; most raw divergence is step size, and the signature layer's distinct demonstrated benefit is the zero-effect rescue. This is not a poker-strength claim.

## Requirements delivered

| Requirement | Delivery |
|---|---|
| R-01 | Training-trajectory capture through the existing history writer; scratch removed in `finally` |
| R-02 | Six deterministic failure signals, canonical JSON, no seed/card/name/free text |
| R-03 | Canonical harness memory with content hash, 64-entry deterministic eviction, dead-end at 3, tamper refusal |
| R-04 | Verified lineage: ledger chain, root-first chain, depth cap, missing/cycle/hash/ledger refusals |
| R-05 | `IRefinementPolicy` boundary and signature-driven policy; legacy adapter byte-identical |
| R-06 | `None`/`Frozen`/`Updating` bootstrap with recorded hashes and stable refusals |
| R-07 | `RefinementGuard`: out-of-domain, multiple-edit and inert skill edits refused before evaluation |
| R-08 | Injection-inert memory, poisoned-memory gate invariance, 32-source scan |
| R-09 | Four bounded deterministic skills; absent/default decisions and old artifact hash preserved |
| R-10 | Opt-in skill refinement under the unchanged gate; default arm pinned |
| R-11 | Six-configuration ablation runner, canonical report, no pointer writes |
| R-12 | `RefinementStrategy` on the proposer genome; meta-loop opt-in wiring; default hashes preserved |

## Defects and honest findings caught by the harness

1. **Falsified HYP-003:** the signature layer does not beat a large-step legacy proposer on raw divergence (CI includes 1.0). The final claim is worded accordingly.
2. **Meta evaluation documents were written to a nested path** (`meta/evaluations/meta/evaluations`) while the promote audit read the non-nested path. Fixed with a one-line caller change and a real-loop read-back regression test; 387/387.
3. **Lineage graph shape is proven before content verification** because a hash-valid cycle needs a ~2^32 SHA-256 fixed point. Every returned artifact is still content-verified; recorded as a deviation.
4. **One existing test's overbroad predicate was scoped** from `*.json` to `hand-*.json` when memory became a persisted artifact; authorized and recorded, no other assertion changed.
5. **`dotnet test` is unusable on this machine** (`Win32Exception (5)`); all local evidence uses the supplementary runner. No CI-green claim is made.

## Residual limits

- The metric is decision divergence against one fixed baseline on finite seeds, not poker strength; promotions were 0/30 in both arms.
- Memory and lineage are observational/write-only in the measured arm; feeding them into the proposal is a future, separate arm with its own pre-registration.
- Zero-rate signals are still selectable (`OQ-001`, accepted P1 residual); a positive-rate floor was not added after the holdout.
- The null-mean ablation row branch is untested (`OQ-007`).
- The meta ledger hash chain is tamper-evident, not tamper-proof; the frozen manifest is not a signature.
- No release, publish, deploy or CI-green claim.

## Reproduce

```powershell
dotnet build WIP/Wip.Tholdem.Agents/Wip.Tholdem.Agents.csproj -c Release --no-incremental
dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release
.tmp/tholdem-verify/RunXunit/bin/Release/net10.0/RunXunit.exe `
  WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
dotnet run --project WIP/Wip.Tholdem.Host -c Release -- train --seed 1099511627776 --iterations 2 --data-dir <fresh-dir>
powershell -NoProfile -ExecutionPolicy Bypass -File docs/goal-tholdem-harness/round-3/aggregate-holdout.ps1
```

*All claims in this report trace to the evidence file; the one falsified hypothesis is recorded, not hidden.*
