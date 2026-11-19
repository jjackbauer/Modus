# State — Wip.Factory Operator Alpha v0.4 remediation

**Status: `implementation-ready / acceptance-pending`. Not accepted.**

- **Measured at this revision:** 264 factory test cases (264 listed methods), 0 failures across core (21), agents
  (69), host (169) and architecture (5). Build: 0 errors.
- **Named v0.3 coverage:** 61 exact-and-executable, 18 equivalent-but-renamed with an explicit map, 1 missing
  (`PA02_GivenDifferentBlindRequirement_UsesUnchangedFactoryBinary`, the blind gate), 0 name-only placeholders.
  That is **79 of 80 accounted for**.
- **P0 remediation:** FAC-P0-01, -02, -03 and -04 are implemented with adversarial executable tests.
  FAC-P0-05 (independent PA-01/PA-02) is **not run**: no external evaluator supplied withheld requirements.
- **P1 remediation:** FAC-P1-01 through FAC-P1-07 are implemented and tested. FAC-P1-08 is complete except the
  blind PA-02 execution, which is not a code gap.
- **v0.3 PA-03–PA-08:** prior session-reported evidence was not re-certified by this remediation. PA-06/PA-07
  mechanisms were strengthened and tested; live blind acceptance was not re-run.
- **Why not accepted:** PA-01/PA-02 require two requirements authored and withheld outside this session, plus
  evaluator-owned expectations. The harness refuses an auto-approved or uninspected contract; it cannot manufacture
  an evaluator.
- **Exact next action:** run `run-acceptance.ps1` twice with the two withheld requirement files and their evaluator
  expectation JSON, then `factory acceptance verify-pair` over both evidence manifests. Until then the status does
  not change, regardless of green suites.

See `v0.4-closure-map.md`, `v0.4-test-report.md`, `v0.4-fault-injection.md`, `v0.4-status.md`,
`v0.4-evaluator-handoff.md`.
