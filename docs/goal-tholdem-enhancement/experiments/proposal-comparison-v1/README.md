# LLM vs non-LLM proposal comparison (v1) — no-benefit

**Status:** completed; outcome **no-benefit**. Not an attribution claim.

- Pre-registration: `preregistration.md`, SHA-256 `f91724dd5e96bf7e2b4f29c5c4b5bbb6aec7a29bdd68b1a0d63b1f620f2f1c06`.
- Four tasks x four candidates x five arms; identical plans per (task, candidate index).
- Verified / Unsafe: LLM **0 / 11**; random 0 / 15; grid 0 / 15; greedy 0 / 7; nosearch 0 / 0.
- Outcome rule required the LLM arm to have >=2 verified, 0 unsafe, and strictly more verified than
  each control; it fails all three.

Files: `preregistration.md`, `results/report.json`, `run.log`.

**Interpretation:** on this small equal-budget comparison no arm produced a verified improvement;
the LLM arm was the least safe. No LLM attribution or learning claim.
