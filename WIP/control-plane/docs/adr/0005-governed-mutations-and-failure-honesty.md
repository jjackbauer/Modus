# ADR 0005: Governed mutations, capability gating and failure honesty

## Status
Accepted.

## Context
The factory does not expose pause/resume/cancel/scheduling/rollback commands for work items. A polished UI can easily imply capabilities that do not exist. The contract also needs exact digest binding and review evidence binding.

## Decision
- Mutations are explicit command endpoints with typed conflicts and idempotency keys.
- Contract approval binds `proposalDigest`; merge authorization binds the accepted review digest and requires typing it.
- Non-executable proposals are refused by the adapter because the review oracle could not run; the UI explains the refusal.
- The adapter advertises capabilities; the UI disables or omits unsupported actions. Retry is a new dispatch cycle, not a fabricated retry command.
- No progress animation, optimistic success or fake agent activity is rendered. SSE events and factory responses decide what is shown.
- Independent acceptance is never inferred; release evidence distinguishes engineering verification from evaluator acceptance.

## Consequences
- Some desirable operations are deliberately absent.
- Operators see why an action is unavailable instead of a dead button.
- Every success toast follows an accepted adapter response.