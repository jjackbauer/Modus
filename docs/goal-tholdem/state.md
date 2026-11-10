# Harness State — tholdem

- **Round**: 13 of 14 (converged)
- **Goal**: Deliver `Wip.Tholdem` as a working heads-up vertical slice — deterministic engine with replayable hashes, baseline agents and reproducible simulator, observation-only LLM agent, statistically gated learning loop, Bones-style viewer, CLI, docs — every claim verified, zero open falsified rows, no release claim.
- **Definition of done**: **9/9 covered at High confidence**
- **Stop criteria**: met, with three explicit residuals (CI not executed, deterministic-deal inference gated rather than eliminated, tree uncommitted)

## Gate status

| Gate | Status | Detail |
|---|---|---|
| G1 Evidence | PASS | EVD-001…106 resolve to a read file line or a command I ran |
| G2 Falsification | PASS | Final tally 0 falsified on the restated C3/C6; five earlier falsifications were found and remediated (recorded, not erased) |
| G3 Coverage | PASS | D1–D9 all High confidence; D8's CI-execution caveat is explicit |
| G4 Reproduction | PASS | I re-ran the suite, the guard matrix, the replay attack matrix, the seed-recovery attack, the viewer and every gate on the current tree |
| G5 Honesty | PASS | CI-not-executed, the weak-seed inference risk and the uncommitted tree are stated in docs, journal and report |

## Objective ledger

| Round | Objective | Mode | Agents | Outcome |
|---|---|---|---|---|
| 1–2 | Engine, evaluator, legality, history, observation boundary | Implement | 2 | delivered |
| 3 | Baseline agents, equity, simulator, metrics | Implement | 1 | delivered; my equity requirement corrected (DEC-007) |
| 4 | Bones-style viewer | Implement | 1 | delivered |
| 5 | Runtime agent, four tools, security suites | Implement | 1 | delivered |
| 6 | Learning loop, gate + verifier, always-promote control | Implement | 1 | delivered |
| 7a/7b/7c | Card faces + UX → independent review (15 findings) → remediation | Implement / Review / Implement | 3 | 15/15 fixed |
| 8a/8b | CLI, parser, persistence fixes; docs, CI surface, replay anchoring | Implement | 2 | delivered |
| 9 | Adversarial (6 claims × 3 falsifiers) | Adversarial | 3 | 3 falsified → remediation |
| 10/10b | Remove session seed from agent; docs consistency | Implement | 2 | delivered; turn seed proved invertible |
| 11–12 | Re-falsify → remove every seed from the agent surface | Adversarial / Implement | 4 | C3 falsified again → round 12 |
| 13 | Entropy guard + precise threat model; final re-falsify | Implement / Adversarial | 4 | C3 & C6 Supported 3/3, G2 PASS |

## Final residuals (stated, not hidden)

1. **CI not executed.** No `gh` CLI and no GitHub token on this machine; local `dotnet test` aborts with `Win32Exception (5)` (documented repo limitation). The CI surface is written and reviewed but no CI run is claimed.
2. **Deterministic-deal inference.** A guessable session seed lets an observer enumerate and recover the deal. Entry points now refuse seeds below 2^40 unless an operator explicitly opts out; the residual is documented in `docs/tholdem/known-limits.md`.
3. **Uncommitted tree.** All Tholdem work and the goal docs are untracked on branch `WIP`; nothing was committed or pushed, and no release is claimed.

## Next objective (separate goal)

Multiway: 4–6 seats with side pots, N-seat table state/observation/history v2, N-seat simulator and viewer. It supersedes the heads-up engine evidence and was explicitly deferred (DEC-011, DEC-013); it is not part of this completion.
