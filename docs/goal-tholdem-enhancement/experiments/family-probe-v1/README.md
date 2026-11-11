# Policy-family feasibility probe (v1)

**Status:** completed; **existence result**, not a learning claim.

- Pre-registration: `preregistration.md`, SHA-256 `4913cf33123656de574da69e2b939c08551fb1fc882f1552a96b94976d19e3c4`.
- 144 grid genomes (raise 4 x call 4 x bluff 3 x sizing 3) evaluated against the default on the
  holdout pool, 8 blocks x 2 hands, margin 5, anchor `three-tag`.
- **1 verified**: raise 0.95, call 0.20, bluff 0.40, sizing 0.75; mean +150.625, lower +71.452.
- The highest-mean candidate (0.40/0.40/0.20/1.25, +1646.25) was **Unsafe** at the anchor.
- 143 of 144 did not verify.

Files: `preregistration.md`, `results/probe.json`, `run.log`.

**Interpretation:** the four-parameter family can represent a gate-passing improvement over the
default, so the family is not the sole bottleneck; finding that region is a proposal/search problem.
No claim that any proposer finds it.
