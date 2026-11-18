# State — Wip.Factory Operator Alpha v0.3

- **Round**: 15 complete. M1–M4 done. Two applications delivered.
- **Gates**: G0 PASS; G1 PASS (55 EVD); G2 PASS; G3 0/8 PA (unclaimed); G4 PASS (135 tests); G5 PASS.
- **PA-05**: demonstrated for the duplication-prone window (kill after merge applied, before recording): two kills,
  one merge, no false release. Pre-merge kill untested.
- **PA-06**: partial — four content facts enforced both ways (accept correct, reject stale). Actor/time/window,
  policy and profile versions not bound.
- **OBS-030**: closed — matching evidence is now tested.
- **Open**: RISK-002 (worktrees inside target repo); pre-merge kill; remaining approval bindings; PA-01/PA-02 need
  the user's blind requirements.
- **Next objective (round 16)**: fix RISK-002 (additive `worktreeRoot` on the WIP provider so worktrees live under
  the control root), then freeze and request the blind pair.
- **Blocked on user (freeze time only)**: OQ-001.