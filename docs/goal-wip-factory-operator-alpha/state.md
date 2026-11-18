# State — Wip.Factory Operator Alpha v0.3

- **Round**: 4 complete. M1 done; **M2 open** (tools done, dispatch not wired).
- **Gates**: G0 PASS; G1 PASS (17 EVD); G2 PASS; G3 0/8 PA; G4 PASS (102 factory tests, build clean); G5 PASS.
- **Coverage**: PA-01..PA-08 `Not started`. Supporting tests: 16 of 80 executable (7 lifecycle + 9 tool gateway).
- **Six tools**: all implemented — list_files, read_file, search_text, apply_patch (atomic unified diff),
  write_file, run_command (dotnet allowlist, argument vector, fail-closed isolation).
- **Next objective (round 5)**: wire M2 — provision a worktree, publish the Implementations profile pinned
  to the six tools, dispatch with the real model, then require non-empty attributable diff + real
  `dotnet build` before advancing `InImplementation → InReview`; refuse `no-attributable-change`.
- **Blocked on user (freeze time only)**: OQ-001 blind PA-01/02 requirements.