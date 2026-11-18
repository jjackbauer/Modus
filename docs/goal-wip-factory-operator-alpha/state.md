# State — Wip.Factory Operator Alpha v0.3 — **RE-FROZEN (DEC-019)**

- **Round**: 21 complete.
- **Gates**: G0 PASS; G1 PASS (67 EVD); G2 PASS; G3 6/8 PA demonstrated, PA-06 met, PA-01/PA-02 not started;
  G4 PASS (143 tests); G5 PASS.
- **Named tests**: 16 of 80 (was 15); gap 1 of 5 closed (host lock). Remaining ranked gaps: claim reservation,
  operation-ID ledger, D-09 template policy, D-08 A edge not wired.
- **Blocked**: **OQ-001** — PA-01/PA-02 need the user's blind pair.
- **Next objective (round 22)**: claim reservation with durable owner/deadline/attempts
  (`Claim_GivenLiveClaim_IsNotDispatchedTwice`, `Claim_GivenCrashAfterClaim_IsReclaimableAfterDeadline`), then
  re-measure and re-freeze.