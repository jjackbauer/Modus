# State — Wip.Factory Operator Alpha v0.3

- **Round**: 1 (Phase 0 complete; Intent Auditor dispatched, result pending).
- **Gates**: G0 pending auditor verdict; G1 PASS (8 EVD rows, all orchestrator-verified); G2 n/a this round; G3 0/8 PA gates; G4 PASS (baseline re-run reproduced); G5 PASS (OQ-001 and RISK-001 recorded, not dropped).
- **Coverage**: PA-01..PA-08 all `Not started`. Supporting tests 0/80.
- **Fresh baseline (N-01)**: build 0 errors; architecture 5/5, Core 18/18, Agents 58/58, Host 5/5 = 86 passing, 0 failing (EVD-008).
- **Verified defects carried into the build**: claim does not reserve (EVD-003); worker never touches leases (EVD-004); merge dedupe is in-memory (EVD-005); store unused by host (EVD-006); stage handler exists only in tests (EVD-007); adjacency lacks the D-08 A edge (EVD-002).
- **Next objective (round 2)**: M1 slice — durable intake that publishes `factory.requirement.submitted.v1`, `Recorded` stage outcome, contract proposal persisted without state change, and `status` read from durable state by a fresh process. Stop condition: submit a requirement through the real CLI and reach `RequirementsAligned` after `contract approve`.
- **Blocked on user (not now)**: OQ-001 — blind PA-01/02 requirements must be supplied by the user after implementation freeze.