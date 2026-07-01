namespace Wip.Bones.Host;

public sealed record BonesSeatPromotionOutcome(
    int Seat,
    string? Outcome);

public sealed class BonesCoLearningMetrics
{
    private readonly object _gate = new();
    private long _coLearningIterationsCompleted;
    private IReadOnlyList<BonesSeatPromotionOutcome> _lastPromotionOutcomes = Array.Empty<BonesSeatPromotionOutcome>();

    public long CoLearningIterationsCompleted
    {
        get { lock (_gate) return _coLearningIterationsCompleted; }
        internal set { lock (_gate) _coLearningIterationsCompleted = value; }
    }

    public IReadOnlyList<BonesSeatPromotionOutcome> LastPromotionOutcomes
    {
        get { lock (_gate) return _lastPromotionOutcomes; }
        internal set { lock (_gate) _lastPromotionOutcomes = value; }
    }

    internal void RecordIterationCompleted(
        IReadOnlyList<BonesSeatPromotionOutcome> promotionOutcomes)
    {
        lock (_gate)
        {
            _coLearningIterationsCompleted++;
            _lastPromotionOutcomes = promotionOutcomes.ToArray();
        }
    }
}
