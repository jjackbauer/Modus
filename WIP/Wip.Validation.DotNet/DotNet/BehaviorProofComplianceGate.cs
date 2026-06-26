namespace Wip.Validation.DotNet.DotNet;

public sealed record BehaviorProofTestEvidence(
    string PlannedTestName,
    bool HasRuntimeExecution,
    bool AssertsSuccessContract,
    bool AssertsFailureContract,
    bool MetadataOnly,
    bool IsApiFocusedIntegration = false,
    bool AssertsOwnerSemantics = false,
    bool AssertsBusinessSemantics = false,
    bool AssertsLifetimeBehavior = false,
    bool AssertsCorrelationContinuity = false,
    bool AssertsIsolation = false,
    bool AssertsNegativeContracts = false);

public sealed record BehaviorProofComplianceResult(
    bool IsCompliant,
    IReadOnlyList<string> Violations);

public static class BehaviorProofComplianceGate
{
    public static BehaviorProofComplianceResult Evaluate(IReadOnlyList<BehaviorProofTestEvidence> evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        var violations = new List<string>();
        if (evidence.Count == 0)
        {
            violations.Add("No planned integration test evidence was provided.");
            return new BehaviorProofComplianceResult(false, violations);
        }

        foreach (var item in evidence)
        {
            if (item.MetadataOnly)
                violations.Add($"{item.PlannedTestName}: metadata-only evidence is not permitted.");

            if (!item.HasRuntimeExecution)
                violations.Add($"{item.PlannedTestName}: runtime execution evidence is required.");

            if (!item.AssertsSuccessContract)
                violations.Add($"{item.PlannedTestName}: deterministic success contract is required.");

            if (!item.AssertsFailureContract)
                violations.Add($"{item.PlannedTestName}: deterministic failure contract is required.");

            if (!item.IsApiFocusedIntegration)
                continue;

            if (!item.AssertsOwnerSemantics)
                violations.Add($"{item.PlannedTestName}: owner semantics proof is required.");

            if (!item.AssertsBusinessSemantics)
                violations.Add($"{item.PlannedTestName}: business semantics proof is required.");

            if (!item.AssertsLifetimeBehavior)
                violations.Add($"{item.PlannedTestName}: lifetime behavior proof is required.");

            if (!item.AssertsCorrelationContinuity)
                violations.Add($"{item.PlannedTestName}: correlation continuity proof is required.");

            if (!item.AssertsIsolation)
                violations.Add($"{item.PlannedTestName}: isolation proof is required.");

            if (!item.AssertsNegativeContracts)
                violations.Add($"{item.PlannedTestName}: deterministic negative contract proof is required.");
        }

        return new BehaviorProofComplianceResult(violations.Count == 0, violations);
    }
}
