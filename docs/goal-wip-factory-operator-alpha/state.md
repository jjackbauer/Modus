# State — Wip.Factory Operator Alpha v0.3

- **Round**: 14 complete. M1–M4 done. Two applications delivered.
- **Gates**: G0 PASS; G1 PASS (52 EVD); G2 PASS; G3 0/8 PA (unclaimed); G4 PASS (134 tests); G5 PASS.
- **PA-06**: **partial** — reviewDigest, headSha, baseSha and contractDigest are enforced against current durable
  evidence, each proven by a mutation test. Not yet bound: actor/time/window, policy/guidelines versions,
  profile/tool versions. Also: no test supplies matching evidence and asserts the merge proceeds (OBS-030).
- **PA-05**: mechanism present (git reconciliation before merge), **no end-to-end kill/resume evidence**.
- **Open**: RISK-002 (worktrees inside target repo); OBS-030; PA-01/PA-02 need the user's blind requirements.
- **Next objective (round 15)**: PA-05 end to end — kill the run during merge and during post-merge verification,
  resume, and prove a single merge and no false release; then close the matching-evidence gap (OBS-030).
- **Blocked on user (freeze time only)**: OQ-001.