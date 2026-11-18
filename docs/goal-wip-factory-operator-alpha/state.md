# State — Wip.Factory Operator Alpha v0.3

- **Round**: 11 complete. M1–M4 done. **M5 blocked** on generality + the blind pair.
- **Gates**: G0 PASS; G1 PASS (43 EVD); G2 PASS; G3 0/8 PA (unclaimed); G4 PASS (128 tests); G5 PASS.
- **New this round**: a second, structurally different requirement (FIFO job queue with state transitions and a
  204 empty case) went through the unchanged factory and was **rejected by the frozen contract for a real defect**
  — the model mapped `POST /jobs/next` where the contract requires `GET`. The gate is not vacuous.
- **Gap proven**: no rework loop. A rejected attempt is terminal; the model never sees its failure.
- **Open**: RISK-002 (worktrees inside target repo); generality unproven (second app not delivered); PA-01/PA-02
  need the user's blind requirements.
- **Next objective (round 12)**: implement the bounded rework loop (`InReview → NeedsRework → InImplementation`
  with the failure summary fed into the implementation prompt, budget 2), then re-run the job-queue requirement and
  see whether the factory recovers by itself.
- **Blocked on user (freeze time only)**: OQ-001 blind PA-01/02 requirements.