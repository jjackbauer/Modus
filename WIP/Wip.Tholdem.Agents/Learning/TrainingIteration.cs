namespace Wip.Tholdem.Agents.Learning;

/// <summary>
/// One challenger-versus-champion iteration including the verified verdict. <see cref="Report"/> is
/// <see langword="null"/> exactly when the iteration was refused before evaluation (an inert
/// proposal); <see cref="Outcome"/> is then <see cref="TrainingLoop.InertProposalOutcome"/>.
/// </summary>
public sealed record TrainingIteration(
    int Iteration,
    string ChampionId,
    string ChallengerId,
    EvaluationReport? Report,
    PromotionDecision Decision,
    bool Verified,
    string Outcome);

/// <summary>The loop phase a progress notification belongs to.</summary>
public enum TrainingPhase
{
    SelfPlay,
    Analyze,
    Propose,
    Evaluate,
    Gate,
    Verify,
    Persist,
}

/// <summary>One observable phase transition of a running training job.</summary>
public sealed record TrainingProgress(
    int Iteration,
    int TotalIterations,
    TrainingPhase Phase,
    TrainingIteration? Completed);

/// <summary>A synchronous progress sink. It must not throw for a well-formed notification.</summary>
public interface ITrainingObserver
{
    void OnProgress(TrainingProgress progress);
}

/// <summary>
/// Refuses a proposed genome before the evaluator is spent. The production guard rejects any
/// proposal whose numeric behaviour equals its incumbent; a prompt-only change is data, not a
/// behavioural challenger.
/// </summary>
public interface IProposalGuard
{
    bool IsAcceptable(StrategyGenome incumbent, StrategyGenome candidate, out string reason);
}

/// <summary>The shipped proposal guard set.</summary>
public static class ProposalGuards
{
    /// <summary>Rejects proposals that would make the same decisions as their incumbent.</summary>
    public static IProposalGuard RejectBehaviorallyInert { get; } = new BehaviorallyInertGuard();

    private sealed class BehaviorallyInertGuard : IProposalGuard
    {
        public bool IsAcceptable(StrategyGenome incumbent, StrategyGenome candidate, out string reason)
        {
            ArgumentNullException.ThrowIfNull(incumbent);
            ArgumentNullException.ThrowIfNull(candidate);

            if (candidate.BehaviorEquals(incumbent))
            {
                reason = TrainingLoop.InertProposalOutcome;
                return false;
            }

            reason = TrainingLoop.ProposalAcceptedReason;
            return true;
        }
    }
}
