using Wip.Bones.Domain;
using Wip.Bones.Identifiers;

namespace Wip.Bones.Agents.Knowledge;

/// <summary>
/// A lineage entry recording the parent-child relationship when a strategy
/// is enhanced or cross-bred from one or two predecessor strategies.
/// </summary>
public sealed record PoolLineageRecord
{
    public PoolLineageRecord(
        BonesStrategyId successorId,
        BonesStrategyId predecessorId,
        BonesStrategyId? secondPredecessorId,
        string outcome,
        string diffSummary,
        DateTimeOffset recordedUtc)
    {
        SuccessorId = successorId;
        PredecessorId = predecessorId;
        SecondPredecessorId = secondPredecessorId;
        Outcome = outcome;
        DiffSummary = diffSummary;
        RecordedUtc = recordedUtc;
    }

    public BonesStrategyId SuccessorId { get; }
    public BonesStrategyId PredecessorId { get; }
    public BonesStrategyId? SecondPredecessorId { get; }
    public string Outcome { get; }
    public string DiffSummary { get; }
    public DateTimeOffset RecordedUtc { get; }
}
