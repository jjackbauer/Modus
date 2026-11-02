# DEV-002 — Category traits deferred to project-level CI grouping

**Status:** proposed
**Date:** 2026-11-02
**Plan reference:** WP-0.4 ("add Category traits: Unit, Integration, Live, Playwright, Security")

## What the plan assumed

Every test would carry a `Category` trait so CI can select unit, integration, live, Playwright, and security suites by trait.

## What is true

- The 17 WIP test projects contain 239 test files and 1,300 declarations; adding traits across them is mechanical but would be invalidated by the pending compliance-matrix work (WP-4.3).
- The gate uses **project-level categories**: 15 offline projects run by default; the two Playwright-heavy projects are optional; security has no tests until WP-1.5; live has none.
- The zero-test and waiver gate still enforces test counts and exact known failures.

## Impact

- CI cannot select a fine-grained live/security subset per test yet.
- WP-0.4's literal trait criterion is partially met; WP-4.3 should add traits with the compliance matrix.

## Smallest reversible correction

Keep the project-level gate for Phase 0, document the mapping, and add `Category` traits in WP-4.3 (or when WP-1.5 adds the security suite). Do not skip or filter tests.

## Options

1. **Defer traits to WP-4.3** and record the mapping (recommended).
2. Add assembly/class-level traits project-wide now; coarse but closer to the plan.
3. Add per-test traits now; large change, conflicts with upcoming compliance edits.

## Recommendation

Option 1. This does not change a gate criterion; it changes the mechanism used to satisfy it. The G0 packet lists it as a known deviation.
