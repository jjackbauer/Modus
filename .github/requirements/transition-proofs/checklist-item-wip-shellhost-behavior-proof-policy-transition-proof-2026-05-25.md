# Checklist Transition Proof - Wip.ShellHost Behavior-Proof Policy

## Baseline Unchecked Source Text Evidence
- Baseline snapshot source: [baselines/checklist-item-wip-shellhost-behavior-proof-policy.unchecked.txt](baselines/checklist-item-wip-shellhost-behavior-proof-policy.unchecked.txt)
- Exact unchecked checklist line:

	`- [ ] Enforce absolute behavior-proof verification for every planned integration test [mandatory - behavior-proof policy]`

## Checked Completion Evidence
- Exact current checked checklist line:

	`- [x] Enforce absolute behavior-proof verification for every planned integration test [mandatory - behavior-proof policy] [transition-proof: .github/requirements/transition-proofs/checklist-item-wip-shellhost-behavior-proof-policy-transition-proof-2026-05-25.md]`

- Checked checklist source: [../Wip.ShellHost.md#L70](../Wip.ShellHost.md#L70)
- Completion evidence source: [../Wip.ShellHost.md#L122](../Wip.ShellHost.md#L122) and [../Wip.ShellHost.md#L123](../Wip.ShellHost.md#L123)
- Checklist-bound proving policy tests:
	- [IntegrationPlan_GivenAnyChecklistItem_ExpectedAtLeastOneExecutableBehaviorProofTest](../../../tests/Wip.ShellHost.Tests/Hosting/BehaviorProofComplianceGateTests.cs#L14)
	- [IntegrationPlan_GivenMetadataOnlyAssertions_ExpectedComplianceGateRejectsPlan](../../../tests/Wip.ShellHost.Tests/Hosting/BehaviorProofComplianceGateTests.cs#L29)
	- [IntegrationPlan_GivenPlannedBehaviorProofTestWithoutExecutableMapping_ExpectedComplianceGateRejectsPlan](../../../tests/Wip.ShellHost.Tests/Hosting/BehaviorProofComplianceGateTests.cs#L36)
- Behavior-proof policy members enforcing the gate:
	- [BehaviorProofPolicy.RequirementsDocumentPath](../../../src/Wip.ShellHost/Hosting/BehaviorProofPolicy.cs#L23)
	- [BehaviorProofPolicy.ParsePlannedIntegrationTests](../../../src/Wip.ShellHost/Hosting/BehaviorProofPolicy.cs#L64)
	- [BehaviorProofPolicy.ParseUncheckedChecklistItems](../../../src/Wip.ShellHost/Hosting/BehaviorProofPolicy.cs#L119)
	- [BehaviorProofPolicy.DiscoverChecklistBoundTests](../../../src/Wip.ShellHost/Hosting/BehaviorProofPolicy.cs#L128)
	- [BehaviorProofPolicy.VerifyAbsoluteBehaviorProofCompliance](../../../src/Wip.ShellHost/Hosting/BehaviorProofPolicy.cs#L170)
	- [BehaviorProofPolicy.IsAbsoluteBehaviorProofAssumption](../../../src/Wip.ShellHost/Hosting/BehaviorProofPolicy.cs#L193)

## Validation Commands
- `dotnet build src/Wip.ShellHost/Wip.ShellHost.csproj -v minimal`
- `dotnet build tests/Wip.ShellHost.Tests/Wip.ShellHost.Tests.csproj -v minimal`
- `dotnet test tests/Wip.ShellHost.Tests/Wip.ShellHost.Tests.csproj --no-build -v minimal --filter "FullyQualifiedName~BehaviorProofComplianceGateTests"`
