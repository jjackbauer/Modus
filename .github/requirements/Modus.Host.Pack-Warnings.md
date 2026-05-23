# Modus.Host Warning Remediation Requirements

> Scope: Derive a completeness checklist and behavior-proof xUnit plan to resolve current `dotnet pack` warnings in Modus.Host (NU1510 and ASPDEPR002) without regressing runtime plugin behavior.

## Functionality Worktree

### Verification Policy

- Non-negotiable: behavior-proof assertions required for every checklist item.
- Metadata-only assertions are supporting evidence only.
- API tests are valid only when thorough integration gates are asserted.
- Include absolute schedule gates when scheduled jobs are in scope.

### Analysis Inputs

| Input | Value |
|---|---|
| CsProject | Modus.Host |
| AnalysisSource | `dotnet pack src/Modus.Host/Modus.Host.csproj -c Release` warning stream |
| MandatoryItems (caller) | none |
| Injected mandatory item | Enforce absolute behavior-proof verification for every planned integration test |
| PlanType | requirements |
| OutputPath | `.github/requirements/Modus.Host.Pack-Warnings.md` |

### Completeness Checklist

- [x] Remove or justify the redundant package dependency path producing NU1510 while preserving host runtime composition behavior [depends on project dependency graph audit] [Audit](../artifacts/iterative-implementation-modus-host-pack-warnings-nu1510-transition-proof-2026-05-23.md)
- [x] Replace deprecated `WithOpenApi` endpoint decoration usage with supported OpenAPI mapping primitives for all affected endpoint mappers [depends on API endpoint mapping refactor]
- [x] Prove plugin operation endpoint runtime dispatch remains correct after OpenAPI mapping refactor [depends on endpoint integration behavior proof] [Audit](../artifacts/iterative-implementation-modus-host-pack-warnings-openapi-dispatch-transition-proof-2026-05-23.md)
- [x] Prove management API runtime contracts remain stable after OpenAPI mapping refactor [depends on management endpoint integration behavior proof] [Audit](../artifacts/iterative-implementation-modus-host-pack-warnings-management-api-runtime-contracts-transition-proof-2026-05-23.md)
- [x] Enforce absolute behavior-proof verification for every planned integration test [mandatory - behavior-proof policy] [Audit](../artifacts/iterative-implementation-modus-host-pack-warnings-behavior-proof-policy-2026-05-23.md)

---

## Test Plan

### NU1510 Dependency Remediation (`Modus.Host.csproj` dependency set)

1. `BuildHostComposition_GivenDependencyPruningRemediation_ExpectedRuntimeServiceResolutionUnchanged`
   *Assumption*: Removing or narrowing the flagged dependency does not change runtime DI resolution for host composition services required during startup.

2. `StartHostRunner_GivenDependencyPruningRemediation_ExpectedDiscoveryValidationActivationSuccess`
   *Assumption*: Startup stage execution (`discovery`, `validation`, `activation`) remains successful after dependency cleanup, proving runtime composition was not weakened.

3. `ResolvePluginContract_GivenDependencyPruningRemediation_ExpectedConfiguredLifetimeBehavior`
   *Assumption*: Plugin contract resolution still follows declared singleton/scoped/transient semantics under live runtime resolution after dependency cleanup.

### ASPDEPR002 OpenAPI Mapping Remediation (`*EndpointMapper` classes)

1. `MapPluginOperationEndpoint_GivenSupportedOpenApiMapping_ExpectedOperationDispatchBusinessSemanticsPreserved`
   *Assumption*: Migrating away from `WithOpenApi` preserves successful runtime dispatch and operation-specific response semantics, not only endpoint registration metadata.

2. `MapPluginOperationEndpoint_GivenValidRequest_ExpectedOwnerResolutionCorrelationContinuityAndSuccessContract`
   *Assumption*: API integration still proves owner resolution correctness and correlation continuity for successful operation execution.

3. `MapPluginOperationEndpoint_GivenMissingOrInactivePlugin_ExpectedDeterministicFailureContractWithoutSideEffects`
   *Assumption*: Negative execution paths remain deterministic and isolated, with explicit failure contract assertions beyond HTTP status.

### Management Endpoint Stability After Refactor

1. `GetManagementStatus_GivenOpenApiRemediation_ExpectedTelemetryAndLifecycleSnapshotSemanticsPreserved`
   *Assumption*: Management status endpoint behavior remains semantically equivalent for runtime status payloads after OpenAPI refactor.

2. `GetPluginCapabilities_GivenOpenApiRemediation_ExpectedCapabilitiesProjectionMatchesActiveRuntimeState`
   *Assumption*: Capability projection still reflects active runtime ownership and activation state after endpoint mapping changes.

3. `UploadAndListPluginArtifacts_GivenOpenApiRemediation_ExpectedAuthorizationIsolationAndMonotonicUploadState`
   *Assumption*: Upload/list management flows preserve authorization, isolation, and monotonic status transitions after API mapping migration.

### Absolute Behavior-Proof Policy Compliance

1. `IntegrationGate_GivenEveryChecklistItem_ExpectedAtLeastOneExecutableBehaviorProofPath`
   *Assumption*: Every checklist item is covered by at least one executable runtime proof path (DI, API, schedule, or deterministic negative).

2. `IntegrationGate_GivenApiFocusedTests_ExpectedOwnerBusinessLifetimeCorrelationIsolationAndNegativeGatesAsserted`
   *Assumption*: API-focused tests are only compliant when all applicable absolute integration gates are asserted in one or more integration scenarios.

---

*All assumptions verified by Falsify Claims. Zero Falsified rows.*
