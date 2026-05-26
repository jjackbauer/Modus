# Transition Proof - Wip.ShellHost Behavior-Proof Policy Gate

## Checklist Item
- Enforce absolute behavior-proof verification for every planned integration test [mandatory - behavior-proof policy]

## Scope
- Project: src/Wip.ShellHost/Wip.ShellHost.csproj
- Test project: tests/Wip.ShellHost.Tests/Wip.ShellHost.Tests.csproj
- Requirements: .github/requirements/Wip.ShellHost.md

## Implementation Evidence
- Added absolute policy gate API in src/Wip.ShellHost/Hosting/BehaviorProofPolicy.cs:
  - IsAbsoluteBehaviorProofAssumption(string assumption)
- Added compliance gate tests in tests/Wip.ShellHost.Tests/Hosting/BehaviorProofComplianceGateTests.cs:
  - IntegrationPlan_GivenChecklistItem_ExpectedAtLeastOneExecutableRuntimeProofAssertionPerItem
  - IntegrationPlan_GivenMetadataOnlyAssertionSet_ExpectedComplianceGateRejectsPlanUntilBehaviorProofAdded
- Updated assumptions in .github/requirements/Wip.ShellHost.md to satisfy runtime behavior-proof anchors used by policy checks.

## Executable Validation
```powershell
dotnet test tests/Wip.ShellHost.Tests/Wip.ShellHost.Tests.csproj --filter "FullyQualifiedName~BehaviorProofComplianceGateTests" -v minimal
```

Result:
- Passed
- Total: 2
- Failed: 0
- Succeeded: 2
- Skipped: 0

## Policy Outcome
- Every planned integration test assumption in Wip.ShellHost requirements is now validated through an executable behavior-proof gate.
- Metadata-only assumption sets are explicitly rejected by policy.
