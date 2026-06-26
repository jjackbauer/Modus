# Requirements Cleanup Notes

## Shared boilerplate

Use `harness/schemas/requirements-doc-template.md` for Verification Policy, behavior-proof gates, and format-gate sections instead of copying blocks into each requirements doc.

## Flagged duplicate test-name rows (content cleanup backlog)

These test names appear multiple times across subsections in active requirements docs and should be deduplicated when those docs are next revised:

### Wip.Dynamic-System-Prompt.md
- `PlanProviderInvocation_GivenDynamicPromptContext_ExpectedProviderRequestContainsSystemInstructionsEnvironmentAndConstraints` (appears in Runtime Prompt Context Contract, Prompt Context Composer, Dynamic Provider System Prompt Invocation, Prompt Policy Gate)
- `PlanProviderInvocation_GivenPromptDiagnostics_ExpectedArtifactPersistenceAndStatusSurfacing` (appears twice in Prompt Diagnostics Persistence and Exposure)
- `PlanStepGuardrails_GivenDotNetOnlyPolicy_ExpectedNpmYarnPipCargoCommandsRejectedOrRewrittenDeterministically` (appears three times in Ecosystem Alignment Guardrails)

### Wip.Next-Steps.md
- Review E2E matrix vs Test Plan subsections for overlapping provider invocation coverage when next edited.