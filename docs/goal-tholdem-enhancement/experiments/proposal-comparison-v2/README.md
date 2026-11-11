# Guarded vs control proposal comparison (v2) — no-benefit

**Status:** completed; outcome **no-benefit**. No learning claim.

- Pre-registration: `preregistration.md`, SHA-256 `26b7a5fe51b5ee61828b8fc9d5244bfc3faa71ac3441b77f8a9c7ec7f392e271`.
- 4 tasks x 8 candidates x 5 arms; guarded uses a 40-hand capture then the 0.02 signature overlay.
- Verified / Unsafe: guarded **0 / 22**; deterministic 0 / 15; random 0 / 32; grid 0 / 28;
  nosearch 0 / 0.
- The rule required guarded to have >=2 verified, 0 unsafe, and strictly more verified than each
  control; it fails all three.

Files: `preregistration.md`, `results/report.json`, `run.log`.

**Interpretation:** the conservative signature overlay does not find a verified proposal and is less
safe than the deterministic control. Anchor 3 remains null.
