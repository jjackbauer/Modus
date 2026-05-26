# Checklist Transition Proof - Behavior-Proof Verification for Planned Integration Tests

## Baseline Unchecked Source Text Evidence

Exact unchecked checklist line (baseline requirement text):

- [ ] Enforce absolute behavior-proof verification for every planned integration test [mandatory - behavior-proof policy]

## Checked Completion Evidence

Exact checked checklist line (current requirement text):

- [x] Enforce absolute behavior-proof verification for every planned integration test [mandatory - behavior-proof policy] [transition-proof: [checklist-item-behavior-proof-verification-for-planned-integration-tests-transition-proof-2026-05-24.md](.github/requirements/transition-proofs/checklist-item-behavior-proof-verification-for-planned-integration-tests-transition-proof-2026-05-24.md)]

## Deterministic Evidence

### Evidence Timestamp

- GeneratedAtLocal: 2026-05-24T12:08:56.0312321-03:00

### Source Evidence (Line-Referenced)

- Behavior-proof requirement document binding via `RequirementsDocumentPath`: [src/Wip.ShellHost/Hosting/BehaviorProofPolicy.cs#L5](src/Wip.ShellHost/Hosting/BehaviorProofPolicy.cs#L5)
- Behavior-proof anchor terms used by compliance gate: [src/Wip.ShellHost/Hosting/BehaviorProofPolicy.cs#L7](src/Wip.ShellHost/Hosting/BehaviorProofPolicy.cs#L7)
- Required API proof dimensions list: [src/Wip.ShellHost/Hosting/BehaviorProofPolicy.cs#L32](src/Wip.ShellHost/Hosting/BehaviorProofPolicy.cs#L32)
- Assumption classifier used for gate evaluation: [src/Wip.ShellHost/Hosting/BehaviorProofPolicy.cs#L43](src/Wip.ShellHost/Hosting/BehaviorProofPolicy.cs#L43)
- Checklist-wide behavior-proof compliance test: [tests/Wip.Shell.E2E.Tests/E2E/BehaviorProofComplianceGateTests.cs#L10](tests/Wip.Shell.E2E.Tests/E2E/BehaviorProofComplianceGateTests.cs#L10)
- API-focused absolute proof-dimension test: [tests/Wip.Shell.E2E.Tests/E2E/BehaviorProofComplianceGateTests.cs#L28](tests/Wip.Shell.E2E.Tests/E2E/BehaviorProofComplianceGateTests.cs#L28)
- Requirements parser and assumption extraction (`*Assumption*:`): [tests/Wip.Shell.E2E.Tests/E2E/BehaviorProofComplianceGateTests.cs#L72](tests/Wip.Shell.E2E.Tests/E2E/BehaviorProofComplianceGateTests.cs#L72), [tests/Wip.Shell.E2E.Tests/E2E/BehaviorProofComplianceGateTests.cs#L106](tests/Wip.Shell.E2E.Tests/E2E/BehaviorProofComplianceGateTests.cs#L106)
- Checked checklist line containing transition-proof link: [wip/requirements/WiP.Bootstrap.Requirements.md#L111](wip/requirements/WiP.Bootstrap.Requirements.md#L111)

### Command Evidence

- Command: `dotnet build src/Wip.ShellHost/Wip.ShellHost.csproj`
- Result: Passed
- Deterministic output facts:
	- `Wip.ShellHost net10.0 succeeded`
	- `Build succeeded in 1.3s`

- Command: `dotnet test tests/Wip.Shell.E2E.Tests/Wip.Shell.E2E.Tests.csproj --no-build`
- Result: Passed
- Deterministic output facts:
	- `Wip.Shell.E2E.Tests test net10.0 succeeded (1.5s)`
	- `Test summary: total: 4, failed: 0, succeeded: 4, skipped: 0`
	- `Build succeeded in 1.8s`
