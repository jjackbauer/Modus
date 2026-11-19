# State — Wip.Factory Operator Alpha v0.4 remediation

**Status: `implementation-ready / acceptance-pending`. Not accepted.**

- **Measured at this revision:** 259 factory test cases (259 listed methods), 0 failures across core (21), agents
  (69), host (164) and architecture (5). Build: 0 errors.
- **Named v0.3 coverage:** 56 exact-and-executable, 18 equivalent-but-renamed with an explicit map, 6 missing,
  0 name-only placeholders. That is **74 of 80 accounted for**, not 80/80.
- **P0 remediation:** FAC-P0-01, -02, -03 and -04 are implemented with adversarial executable tests.
  FAC-P0-05 (independent PA-01/PA-02) is **not run**: no external evaluator supplied withheld requirements.
- **P1 remediation:** FAC-P1-01 through FAC-P1-07 are implemented and tested, including child-output redaction and
  a security-policy check for it. FAC-P1-08 produced a classified inventory and a CI gate, but six named behaviours
  remain absent; that is reported, not waived.
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
