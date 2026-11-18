# Goal — Wip.Factory Operator Alpha (v0.3)

## Verbatim user request

> `/stretch-goal-harness`
> followed by the full **Wip.Factory Operator Alpha — Requirements Specification v0.3** document,
> whose normative body is committed at `harness/requirements/Wip.Factory.Operator.Alpha.v0.3.md`.
> Binding approvals in that request: **D-08 A** (add exactly one edge `DeliveryValidation → NeedsRework`,
> pre-merge only) and **D-09** (policy approval only for explicitly preapproved immutable templates,
> human fallback otherwise).

## One-sentence outcome

The unchanged Modus/WIP factory host accepts two independently withheld, behaviourally different
supported requirements through its public CLI, builds attributable applications with real model tool
calls, passes immutable independently approved HTTP contracts plus existing regression gates,
authorises and reconciles an actual merge, verifies the merged revision, and can supervise the result
with `factory serve` — while also rejecting an intentionally incorrect candidate.

## Definition of done — PA-01 … PA-08 (all mandatory)

| Gate | Falsifiable check | Command / observable |
|---|---|---|
| PA-01 | Unfamiliar supported requirement → verified release | operator CLI only; manifest emitted |
| PA-02 | Second, behaviourally different requirement, unchanged binary | same binary; no source edit between runs |
| PA-03 | Nonempty attributable diff equals merged revision | `git diff base..head` non-empty; merged tree contains it |
| PA-04 | Known-wrong candidate rejected by the frozen contract | contract fails ≥1 scenario; no release |
| PA-05 | Kill during implementation/merge/verification → safe resume | no duplicate merge; no corrupt state |
| PA-06 | Every approval-bound field mutated → merge refused | each category independently |
| PA-07 | Served release identity + contract verified | PID/port/SHA ownership proven |
| PA-08 | Unpinned tools, path escapes, unsupported isolation refused | named categories |

**Non-negotiable:** PA-01/PA-02 use requirements withheld until the implementation is frozen; PA-04 must
reject. A green build, 40 unit tests, nine roles, or a successful `factory demo` never substitutes.

## Hard constraints

- **N-02** no second model loop, tool gateway, worktree engine, merge gate or central LLM supervisor.
- **N-01** existing factory + baseline suites stay green, re-run and recorded (prior `86` and `460/3`
  are historical assertions, not substitutes).
- **N-09** no claim of sandboxing, exactly-once external effects, or multi-process scheduling.
- **D-08 A** is the only permitted new state edge.
- Reuse WIP `IAgentExecutor`, `IToolInvoker`, `FactoryReviewService`, `FactoryMergeGate`, WIP worktrees.

## Intent anchors (all KEPT — no replacement, no exclusion)

| Anchor (user mechanism) | Status |
|---|---|
| "receive requirements" via CLI into a durable store | KEPT |
| "build working software" with real tools | KEPT |
| "no per-application C# demo" | KEPT |
| "tool calls to get it right" | KEPT (§6.1 six tools) |
| two blind requirements on one unchanged binary | KEPT |
| frozen independent HTTP acceptance oracle | KEPT |
| merge authorisation separate from contract approval | KEPT |
| `factory serve` supervision distinct from `run` verification | KEPT |
| effectively-once outcomes via reconciliation | KEPT |
| fail-closed execution isolation | KEPT |

## Stop criteria

1. All eight PA gates pass, or a gate is honestly recorded as failing.
2. Round cap 30. Stop when done — not when the cap is near.
3. A blocker is declared only after the same concrete condition blocks three consecutive rounds.