# Target-reachability probe (v1)

**Status:** completed; diagnosis **0/8**, deterministic **1/8**. No learning claim.

- Pre-registration: `preregistration.md`, SHA-256 `11671770be8f738b3e8ee86bd798709114e317e360e6371ea85fdb4ae3e6565e`.
- Start default; 8 chained proposals per arm; fixed default incumbent; 8-block holdout, anchor.
- Verified counts: diagnosis **0**, deterministic **1**, random 0, grid 0, nosearch 0.
- The deterministic verified proposal is iteration 7: mean +66.09375, lower +11.669.
- All eight diagnosis proposals were Unsafe (7) or Rejected (1), with strongly negative means.

Files: `preregistration.md`, `results/report.json`, `run.log`.

**Interpretation:** the diagnosis proposer does not reach the verified region and is harmful on this
plan; the deterministic control does find one verified proposal. This is not the pre-registered
four-task protocol and not a learning claim.
