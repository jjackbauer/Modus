# Named-test map — measured at the v0.4 remediation revision

The authoritative machine-readable inventory is `named-test-coverage.json`; the explicit equivalent-name mapping is
`named-test-renames.json`. Both are generated/validated by `TraceabilityTests` from compiled assemblies and source
assertions at this revision.

| Class | Count |
|---|---:|
| Named in v0.3 §12.2–12.3 | 80 |
| `exact-and-executable` | 56 |
| `equivalent-but-renamed` (mapped, assertion-probed) | 18 |
| `missing behavior` | 6 |
| `invalid-superseded` | 0 |
| `name-only-no-assertion` (fails the gate) | 0 |

**Accounted for: 74 of 80.** The gap is real: six behaviours are not proven under either their exact name or a mapped
one. The map does not convert renamed behaviour into exact coverage, and no pass-through tests were written to move
the number.

## Remaining six

1. `Contract_GivenAmbiguousUnobservableProse_RequiresResolutionBeforeFreeze`
2. `Contract_GivenModelAttemptToEditFrozenOracle_IsRefused`
3. `AgentLoop_GivenToolError_ContinuesWithObservation`
4. `AgentLoop_GivenStepBudget_FailsWithCategory`
5. `Contract_GivenHumanRejectsProposal_AllowsReproposalWithoutImplementation`
6. `PA02_GivenDifferentBlindRequirement_UsesUnchangedFactoryBinary` (blind; not-run)

## Why some names are mapped rather than duplicated

For example, `Operation_GivenMergeAlreadyApplied_ReconcilesWithoutSecondMerge` is proven by
`Release_GivenCrashAfterMergeBeforeVerification_ReconcilesGitAndReverifies`, which asserts one Git merge, replayed
verification and no false release. `ToolGateway_GivenSymlinkOrJunctionEscape_Refuses` is proven by
`Factory_GivenSymlinkEscape_RefusesBeforeWrite`, which builds a real junction and asserts no file escaped. Mapping
keeps the suite honest; cloning the test under a second name would inflate coverage without adding a counterexample.

## Reproduce

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File docs/goal-wip-factory-operator-alpha/run-factory-gates.ps1
```

The gate report publishes cases, failures, exact, renamed, missing and name-only counts separately; it exits non-zero
on a failed suite or a name-only placeholder.
