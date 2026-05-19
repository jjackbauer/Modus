# Requirements: Plugin.Host.Telemetry Authoring Workflow

> Scope: Author a scheduled plugin that emits Host telemetry snapshots (CPU, memory, and GC metrics), validates through contract and integration tests, and provides concrete Host CLI runtime evidence.

---

## Functionality Worktree

### Coverage Matrix

| Capability | Required Outcome | Dependency Note | Status |
|---|---|---|---|
| Plugin artifact onboarding | Plugin project follows Host discovery conventions and deterministic metadata | [onboarding foundation] | Implemented |
| Contract and lifecycle compliance | Plugin implements required contract, lifecycle, operation catalog, and scheduled interfaces | [depends on onboarding] | Implemented |
| Deterministic operation and schedule catalog | Operation and recurring schedule names are deterministic and stable | [depends on contract compliance] | Implemented |
| Diagnostics and failure semantics | Host emits startup, discovery, validation, activation, scheduling, and operation diagnostics with deterministic outcomes | [depends on onboarding and contracts] | Implemented |
| Regression and runtime verification workflow | Build/test/CLI verification command set is explicit and reproducible | [depends on all implementation capabilities] | Implemented |

### Completeness Checklist

- [x] Document plugin artifact and metadata requirements for Host discovery [onboarding]
- [x] Document plugin contract and lifecycle requirements (IPluginContract, IPluginLifecycle) [contracts]
- [x] Document plugin operation catalog behavior and deterministic ordering [operations]
- [x] Document diagnostics and failure semantics for startup, activation, and operation [diagnostics]
- [x] Document regression workflow and runtime CLI evidence requirements [verification]
- [x] Document recurring schedule registration requirements (IPluginScheduledEvents, IPluginScheduler.ScheduleRecurring) [scheduling]

### Plugin Artifact and Metadata Requirements

1. Project identity and metadata
   - Project file: `plugins/Plugin.Host.Telemetry.csproj`
   - Deterministic identity: `AssemblyName=Plugin.Host.Telemetry`
   - Deterministic contract metadata: `ModusVersion=1.0.0`, `ModusCapabilities=Cap.Host;Cap.Telemetry`, `ModusOperations=Telemetry.Host.CollectSnapshot`

2. Host discovery behavior
   - Runtime assembly scan discovers `Plugin.Host.Telemetry` from plugin output artifacts.
   - Project-file onboarding remains deterministic and ignores duplicate active plugin identity when already activated from runtime assembly scan.

### Contract and Lifecycle Requirements

1. Implemented mandatory interfaces in `HostTelemetryPlugin`
   - `IPluginContract`
   - `IPluginLifecycle`
   - `IPluginOperationCatalog`
   - `IPluginScheduledEvents`
   - `IEventSubscriber`
   - `ISyncResponder`

2. Lifecycle invariants
   - `Load`, `Start`, `Stop`, and `Unload` guard against null context input.
   - `Start` establishes baseline CPU sample state used by subsequent telemetry snapshots.

### Operation Catalog and Deterministic Ordering Requirements

1. Operation catalog
   - `SupportedOperations` exposes exactly one canonical operation: `Telemetry.Host.CollectSnapshot`.

2. Deterministic scheduled registration
    - `RegisterSchedules` always registers one recurring job:
       - Job name: `Telemetry.Host.CollectSnapshot.EverySecond`
       - Interval: `TimeSpan.FromSeconds(1)`
     - Operation: `Telemetry.Host.CollectSnapshot`

### Diagnostics and Failure Semantics Requirements

1. Host runtime diagnostics coverage
   - Startup: `stage=startup ... outcome=success`
   - Discovery/validation/activation: `stage=discovery|validation|activation plugin=Plugin.Host.Telemetry ... outcome=success`
   - Scheduling registration: `stage=scheduling plugin=Plugin.Host.Telemetry ... outcome=registered`
   - Scheduled execution: `stage=operation plugin=Plugin.Host.Telemetry operation=Telemetry.Host.CollectSnapshot source=scheduled ... outcome=success`

2. Scheduled operation failure semantics
   - If a scheduled plugin does not implement `ISyncResponder`, Host emits ignored scheduled-operation diagnostics.
   - If scheduled operation handling throws or returns non-success, Host emits deterministic `stage=operation ... outcome=failure reason=...` diagnostics.

### Regression and Runtime CLI Evidence Requirements

| Command | Result |
|---|---|
| `dotnet build plugins/Plugin.Host.Telemetry.csproj` | Passed |
| `dotnet build tests/Modus.Host.IntegrationTests/Modus.Host.IntegrationTests.csproj` | Passed |
| `dotnet test tests/Modus.Host.IntegrationTests/Modus.Host.IntegrationTests.csproj --no-build` | Passed |
| `dotnet build src/Modus.Host/Modus.Host.csproj` | Passed |
| `dotnet test tests/Modus.Host.IntegrationTests/Modus.Host.IntegrationTests.csproj --no-build` | Passed |
| `dotnet run --project src/Modus.Host/Modus.Host.csproj -- plugins` | Passed (startup, activation, scheduling, and telemetry operation diagnostics observed) |

---

## Test Plan

### Plugin.Host.Telemetry Artifact and Discovery

1. `TelemetryPluginDescriptor_GivenScheduledMetadata_ExpectedDeterministicDescriptorValues`
   *Assumption*: Plugin project metadata must produce deterministic plugin identity, version, capabilities, and operations for Host onboarding.

2. `TelemetryPluginHostStartup_GivenPluginsPathContainsTelemetryPlugin_ExpectedDiscoveryValidationAndActivationDiagnostics`
   *Assumption*: Host startup must discover and activate telemetry plugin with deterministic stage diagnostics.

### Plugin.Host.Telemetry Contract and Lifecycle

1. `TelemetryPluginContract_GivenCompliantImplementation_ExpectedValidationPassesScheduledCapabilities`
   *Assumption*: Scheduled telemetry plugin must satisfy contract, lifecycle, operation catalog, and scheduled-events requirements.

2. `TelemetryPluginContract_GivenMissingScheduledCapability_ExpectedValidationReportsIPluginScheduledEvents`
   *Assumption*: Missing scheduled registration capability is a deterministic contract failure.

### Deterministic Schedule and Operation Behavior

1. `TelemetryPluginSchedules_GivenRegisterSchedules_ExpectedRecurringCpuRamGcCollection`
   *Assumption*: The plugin must register deterministic recurring schedule metadata for telemetry collection.

2. `TelemetryPluginOperations_GivenHandleTelemetryCollection_ExpectedPayloadContainsCpuMemoryAndGcMetrics`
   *Assumption*: Telemetry operation execution must return observable CPU, memory, and GC metrics.

### Diagnostics and Runtime Evidence

1. `TelemetryRuntime_GivenScheduledPluginLifecycle_ExpectedSchedulingAndScheduledOperationDiagnostics`
   *Assumption*: Scheduled plugin runtime must produce diagnostics for schedule registration and scheduled operation execution.

2. `TelemetryRuntime_GivenHostCliExecution_ExpectedStartupActivationAndFunctionalTelemetryOutput`
   *Assumption*: Acceptance requires concrete Host CLI evidence, not only unit or integration tests.

---

*All assumptions verified by Falsify Claims. Zero Falsified rows.*
