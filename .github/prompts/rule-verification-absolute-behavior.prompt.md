````prompt
---
agent: plan
description: Enforce absolute behavior-proof verification for requirements and integration test plans.
---

# Rule: Absolute Behavior Verification

## Purpose
Ensure every requirements plan proves runtime behavior, not metadata alone.
This rule is mandatory for all plans produced by requirements workflows.

## Non-Negotiable Policy
- Every planned integration test must include executable behavior proof.
- Metadata-only assertions are supporting evidence and are never sufficient alone.
- Any checklist item without behavior-proof tests is non-compliant.
- API-focused tests must always verify behavior thoroughly in integration, never with shallow status-only checks.

## Behavior-Proof Requirement
Each scenario must include at least one concrete runtime proof path:
- DI resolver path: runtime plugin instance resolution by expected lifetime path.
- API path: runtime dispatch through the plugin endpoint with expected response contract.
- Scheduled path: scheduled job execution with observed successful runtime outcomes.
- Negative path: deterministic runtime block or failure contract when execution must be rejected.

## Absolute API Integration Gates
- Owner resolution correctness: runtime owner must exist and be unique for successful execution.
- Business behavior proof: assert operation-specific payload semantics that prove logic executed correctly.
- DI lifetime proof: assert expected singleton/scoped/transient behavior under live requests.
- Correlation continuity: response correlation must match request correlation on success and rejection paths.
- Isolation guarantee: after failed load/remove, API must fail deterministically with no side-effect execution.
- Negative contract proof: rejected/failed paths must assert response contract semantics, not only HTTP status.

API tests that do not satisfy all applicable gates are non-compliant.

## Absolute Schedule Gates (for timer and scheduled jobs)
- Recurring execution count must satisfy a bounded-window minimum derived from interval.
- Observed cadence deltas must stay within explicit tolerance bounds.
- Counted runs must match expected plugin, operation, source, and success outcome.
- One-time schedules must run exactly once in the allowed window.
- Resolver-unavailable scenarios must emit deterministic unresolvable-via-di failure evidence.

## Plan Compliance Condition
A plan is PASS only when:
1. Every unchecked checklist item has corresponding xUnit-named tests.
2. Every corresponding test includes behavior-proof assertions.
3. Metadata-only tests are absent as sole evidence for any item.
4. API-focused tests satisfy the Absolute API Integration Gates.

If any condition fails, the plan must be revised before save.
````