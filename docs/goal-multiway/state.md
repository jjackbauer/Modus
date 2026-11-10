# Harness State — multiway

- **Round**: 6 of 8 (converged)
- **Goal**: Extend `Wip.Tholdem` from heads-up to 2–6 seat no-limit Hold'em with correct side pots, N-seat observation/history, simulator and viewer, without breaking the heads-up evidence.
- **Definition of done**: **8/8 covered**
- **Stop criteria**: met, with the residuals stated below

## Gate status

| Gate | Status | Detail |
|---|---|---|
| G1 Evidence | PASS | EVD-001…015 resolve to a command or a read line |
| G2 Falsification | PASS | M1–M4 Supported 3/3 (round 4); M5 falsified twice, fixed twice, final re-test Supported 3/3 |
| G3 Coverage | PASS | M1–M8 covered at High confidence |
| G4 Reproduction | PASS | 275/275 suite, my own 3-seat side-pot hand, 4-seat simulator and live probes, clean-checkout runs, final build/architecture/coverage/waiver |
| G5 Honesty | PASS | Two falsifications and one of my own false claim premises are recorded; residuals below are explicit |

## Objective ledger

| Round | Objective | Mode | Agents | New EVD | Falsified | Gates | Outcome |
|---|---|---|---|---|---|---|---|
| 1 | N-seat engine, side pots, observation, history v2 | Implement | 1 | 3 | 0 | G1✓ G2✓ G3✗ G4✓ G5✓ | delivered; side pots only via a test seam |
| 1b | Per-seat stacks + public side-pot tests | Implement | 1 | 1 | 0 | ✓ | my 900/600/400 probe passes |
| 2 | N-seat simulator, metrics, runtime store | Implement | 1 | 2 | 0 | ✓ | 4-seat deterministic, zero-sum |
| 3 | N-seat viewer, CLI `--seats`, docs | Implement | 1 | 2 | 0 | ✓ | 4-seat live verified and screenshotted |
| 4 | Adversarial gate (M1–M5 × 3) | Adversarial | 3 | 0 | 1 (M5) | G1✓ G2✗ G3✗ G4✓ G5✓ | M1–M4 pass; M5 fails on a brittle test |
| 5 | Self-contained schema-aware tests | Implement | 1 | 1 | 0 | G2✗ (new defect) | suite green; v2 seat rule still wrong |
| 6 | Correct v2 invariant + final M5 | Implement + Adversarial | 4 | 2 | 0 | G2✓ | Supported 3/3; gates green |

## Final residuals (stated, not hidden)

1. **CI not executed.** No `gh` CLI and no token; local `dotnet test` aborts with `Win32Exception (5)`. The CI surface is written and reviewed; no CI green is claimed.
2. **Deterministic-deal inference.** A guessable seed lets an observer recover the deal; CLI/HTTP entry points refuse seeds below 2^40 unless explicitly opted out, and the limitation is documented.
3. **Learning loop remains heads-up.** The engine, simulator and viewer support 2–6 seats; strategy training/evaluation is heads-up by design and documented in `known-limits.md`.
4. **Uncommitted tree.** All multiway code and docs are untracked on branch `WIP`; nothing was committed or pushed, and no release is claimed.

## Next objective

None for this goal. Candidate follow-ons: multiway learning-loop evaluation, viewer polish, or committing the untracked tree.
