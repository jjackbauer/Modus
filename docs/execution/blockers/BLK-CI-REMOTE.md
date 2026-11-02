# BLK-CI-REMOTE — first GitHub Actions run requires an authorized push

**Status:** open
**Opened:** 2026-11-02T12:50:00-03:00
**Owner:** product owner / release engineer

## What is blocked

WP-0.4's acceptance requires the restructured CI to run the offline suites, the zero-test guard, and the waiver gate in GitHub Actions. The workflows are committed, but this session has **not pushed** to `origin` because no push was authorized.

## Evidence

- Workflow: `.github/workflows/tests-ci.yml` (`wip-build-ubuntu`, `wip-build-windows`, `wip-architecture`, `wip-offline-tests`).
- Optional workflow: `.github/workflows/wip-optional.yml`.
- Scripts: `tools/run-wip-tests.ps1`, `tools/verify-test-results.ps1`.
- Local gate: green on 15 projects / 21 waivers; missing, unwaived, and zero-test cases fail (`docs/execution/evidence/WP-0.4/`).
- No push was performed; `origin/WIP` is unchanged.

## Options

1. Authorize a push of `WIP` (or a short-lived branch) and let the workflow run; attach the run URL/TRX to WP-0.4. (Recommended.)
2. Run the workflow manually from a fork/runner with the same commit and provide the run evidence.
3. Accept the local gate + workflow definition as Phase 0 evidence with an explicit risk waiver (not recommended for G0).

## Recommendation

Push a short-lived branch and run `Tests CI`; report the run URL and artifacts. WP-0.4 then closes and G0 can be assessed. No source change is needed.
