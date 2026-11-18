# State — Wip.Factory Operator Alpha v0.3

- **Round**: 6 complete. M1, M2 done. **M3 partial**: oracle built and tested, not yet wired into review.
- **Gates**: G0 PASS; G1 PASS (26 EVD); G2 PASS; G3 0/8 PA (unclaimed); G4 PASS (114 tests); G5 PASS.
- **Coverage**: supporting tests 28 of 80 executable.
- **Built this round**: `AcceptanceContract` (parse/canonicalise/digest, refusals), `AcceptanceContractRunner`
  (real HTTP, capture substitution, structural subset), executable contracts through intake, 6 new tests.
- **Not done**: the review stage does not execute the contract yet; no review record, no merge approval, no
  merge, no post-merge verification, no `serve`.
- **Next objective (round 7)**: wire the review stage — on `implementation.completed` start the candidate on a
  factory-allocated port, run the frozen contract, record four evidence-backed checks, and advance to
  ReadyForDelivery only on green; otherwise NeedsRework. Then the separate merge approval and the real merge.
- **Blocked on user (freeze time only)**: OQ-001 blind PA-01/02 requirements.