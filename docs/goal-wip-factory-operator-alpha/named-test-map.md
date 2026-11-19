# Named-test map — measured at the v0.4 remediation revision

The authoritative machine-readable inventory is `named-test-coverage.json`; the explicit equivalent-name mapping is
`named-test-renames.json`. Both are generated/validated by `TraceabilityTests` from compiled assemblies and source
assertions at this revision.

| Class | Count |
|---|---:|
| Named in v0.3 §12.2–12.3 | 80 |
| `exact-and-executable` | 61 |
| `equivalent-but-renamed` (mapped, assertion-probed) | 18 |
| `missing behavior` | 1 |
| `invalid-superseded` | 0 |
| `name-only-no-assertion` (fails the gate) | 0 |

**Accounted for: 79 of 80.** The only remaining name is
`PA02_GivenDifferentBlindRequirement_UsesUnchangedFactoryBinary`: the blind gate is **not-run**, not missing code.
No independent evaluator supplied a second withheld requirement.

## Why some names are mapped rather than duplicated

For example, `Operation_GivenMergeAlreadyApplied_ReconcilesWithoutSecondMerge` is proven by
`Release_GivenCrashAfterMergeBeforeVerification_ReconcilesGitAndReverifies`, which asserts one Git merge, replayed
verification and no false release. `ToolGateway_GivenSymlinkOrJunctionEscape_Refuses` is proven by
`Factory_GivenSymlinkEscape_RefusesBeforeWrite`, which builds a real junction and asserts no file escaped. Mapping
keeps the suite honest; cloning the test under a second name would inflate coverage without adding a counterexample.

## Round-3 additions

- `Contract_GivenHumanRejectsProposal_AllowsReproposalWithoutImplementation` — `contract reject` records a durable
  rejection, refuses later approval of that digest, leaves the state Proposed, and permits a revised requirement to
  produce a new proposal.
- `Contract_GivenAmbiguousUnobservableProse_RequiresResolutionBeforeFreeze` — expectations with no observable HTTP
  cue are surfaced as unresolved questions and cannot be frozen.
- `AgentLoop_GivenToolError_ContinuesWithObservation` / `AgentLoop_GivenStepBudget_FailsWithCategory` — the runtime
  loop's continuation and budget stop are asserted by exact name.
- `Contract_GivenModelAttemptToEditFrozenOracle_IsRefused` — a model-shaped path escape toward the control root is
  refused and the frozen contract digest is unchanged.

## Reproduce

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File docs/goal-wip-factory-operator-alpha/run-factory-gates.ps1
```

The gate report publishes cases, failures, exact, renamed, missing and name-only counts separately; it exits non-zero
on a failed suite or a name-only placeholder.
