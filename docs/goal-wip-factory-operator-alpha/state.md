# State — Wip.Factory Operator Alpha v0.3

- **Round**: 3 complete. **M1 stop condition MET.**
- **Gates**: G0 PASS; G1 PASS (14 EVD rows); G2 PASS; G3 0/8 PA gates; G4 PASS (93 factory tests, build clean); G5 PASS.
- **Milestones**: M0 partial (harness docs + control root); **M1 done**; M2–M5 not started.
- **Coverage**: PA-01..PA-08 `Not started`. Supporting tests: 7 of 80 newly executable (intake, proposal,
  approval, control root, idempotency, refusals).
- **Working now through the real CLI**: `requirement submit`, `run --until-idle`, `contract show`,
  `contract approve`, `status`, all over a durable append-only log in a control root outside the target repo.
- **Known gaps before PA gates can move**: no implementation role dispatch yet (M2); contract scenarios are
  prose, not executable HTTP (`executable: false`), so no oracle exists (M3); no review/merge wiring (M3);
  no `serve` supervisor (M4).
- **Next objective (round 4)**: M2 slice — dispatch the Implementations role with the six-tool gateway
  against a worktree, require an attributable non-empty diff plus a real `dotnet build`, and refuse
  `no-attributable-change` when the model only talks.
- **Blocked on user (freeze time only)**: OQ-001 blind PA-01/02 requirements.