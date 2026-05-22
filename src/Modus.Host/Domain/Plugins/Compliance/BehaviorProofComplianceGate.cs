namespace Modus.Host.Plugins.Compliance;

public readonly record struct BehaviorProofEvidence(
    bool HasOwnerResolutionProof,
    bool HasBusinessSemanticProof,
    bool HasDiLifetimeProof,
    bool HasCorrelationContinuityProof,
    bool HasIsolationProof,
    bool IsMetadataOnly);

public readonly record struct BehaviorProofEvaluationResult(
    bool IsCompliant,
    IReadOnlyCollection<string> MissingGates);

public sealed class BehaviorProofComplianceGate
{
    public BehaviorProofEvaluationResult Evaluate(BehaviorProofEvidence evidence)
    {
        var missing = new List<string>();

        if (evidence.IsMetadataOnly)
        {
            missing.Add("metadata-only");
        }

        if (!evidence.HasOwnerResolutionProof)
        {
            missing.Add("owner-resolution");
        }

        if (!evidence.HasBusinessSemanticProof)
        {
            missing.Add("business-semantics");
        }

        if (!evidence.HasDiLifetimeProof)
        {
            missing.Add("di-lifetime");
        }

        if (!evidence.HasCorrelationContinuityProof)
        {
            missing.Add("correlation-continuity");
        }

        if (!evidence.HasIsolationProof)
        {
            missing.Add("isolation");
        }

        return new BehaviorProofEvaluationResult(
            IsCompliant: missing.Count == 0,
            MissingGates: missing);
    }
}