using Wip.Tholdem.Agents.Learning.Refinement;

namespace Wip.Tholdem.Agents.Learning;

/// <summary>Everything one training run needs. There is no clock and no ambient randomness in it.</summary>
public sealed record TrainingOptions(
    ulong Seed,
    int Iterations,
    int TrainingHands = 120,
    int EvaluationSeeds = 24,
    int EvaluationHandsPerMatch = 10,
    EvaluationCriteria? Criteria = null,
    string? DataDirectory = null,
    string? StartingStrategyId = null,
    bool HarnessRefinement = false,
    HarnessBootstrapMode BootstrapMode = HarnessBootstrapMode.None,
    string? BootstrapFrom = null,
    bool SkillRefinement = false,
    RefinementStrategy RefinementStrategy = RefinementStrategy.SignatureDriven,
    double SignalRateFloor = 0.0);
