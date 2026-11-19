# State — Wip.Factory Operator Alpha v0.4 remediation

**Status: `implementation-ready / acceptance-pending`. Not accepted.**

- **Measured at this revision:** 235 factory test cases (235 listed methods), 0 failures across core (21), agents
  (69), host (140) and architecture (5). Build: 0 errors.
- **Named v0.3 coverage:** 32 of 80 exact-and-executable; 48 missing behaviour; 0 name-only placeholders
  (`named-test-coverage.json`).
- **P0 remediation:** FAC-P0-01, -02, -03 and -04 are implemented with adversarial executable tests.
  FAC-P0-05 (independent PA-01/PA-02) is **not run**: no external evaluator supplied withheld requirements.
- **P1 remediation:** FAC-P1-01, -02, -03, -04, -05 and -07 are implemented and tested. FAC-P1-06 is implemented
  except secret redaction (P1-06.5). FAC-P1-08 produced a machine-readable inventory and a CI gate, but 48 named
  behaviours remain absent; that is reported, not waived.
- **v0.3 PA-03–PA-08:** prior session-reported evidence was not re-certified by this remediation. PA-06 and PA-07
  mechanisms were strengthened and tested; live blind acceptance was not re-run.
- **Why not accepted:** PA-01/PA-02 require two requirements authored and withheld outside this session, plus
  evaluator-owned expectations. The harness now refuses an auto-approved or uninspected contract; it cannot
  manufacture an evaluator.
- **Exact next action:** run `run-acceptance.ps1` twice with the two withheld requirement files and their evaluator
  expectation JSON, then `factory acceptance verify-pair` over both evidence manifests. Until then the status does
  not change, regardless of green suites.

See `v0.4-closure-map.md`, `v0.4-test-report.md`, `v0.4-fault-injection.md`, `v0.4-status.md`.
