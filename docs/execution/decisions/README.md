# WIP architecture decision records

Accepted at **G0 on 2026-11-02** by the product owner. Acceptance of an ADR does **not** replace the later gate decision named in the Gate column (G1–G4 still decide scope/contracts/residual risk).

| ADR | Title | Owner | Gate |
|---|---|---|---|
| [ADR-IMPL-01](ADR-IMPL-01-solution-topology.md) | Solution topology | tech lead | G0 |
| [ADR-IMPL-02](ADR-IMPL-02-sandbox-mechanism.md) | Sandbox mechanism | platform/security + tech lead | G2 |
| [ADR-IMPL-03](ADR-IMPL-03-permission-model.md) | Permission model | security + runtime lead | G3 |
| [ADR-IMPL-04](ADR-IMPL-04-workflow-contract.md) | Workflow definition/executor | tech lead + runtime lead | G4 |
| [ADR-IMPL-05](ADR-IMPL-05-agent-scope.md) | Agent runtime scope | product + agent engineer | G1 |
| [ADR-IMPL-06](ADR-IMPL-06-compatibility-policy.md) | Compatibility/deprecation | tech lead + product | G0/G5 |

Each ADR records its context, decision, consequences, reversal triggers, and open questions with an owner. The G0 packet lists which decisions are requested.
| [ADR-IMPL-07](ADR-IMPL-07-temporary-shell-bones-coupling.md) | Temporary ShellHost→Bones waiver | tech lead | Superseded by WP-2.6 (2026-11-04) |