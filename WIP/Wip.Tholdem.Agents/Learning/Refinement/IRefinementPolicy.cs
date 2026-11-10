using Wip.Tholdem.Agents.Simulation;

namespace Wip.Tholdem.Agents.Learning.Refinement;

/// <summary>A signature-driven, data-only refinement proposal with a fixed-vocabulary rationale.</summary>
public sealed record RefinementProposal(StrategyGenome Genome, string RationaleCode);

/// <summary>
/// Everything the refinement policy may see. It carries only data the loop already measured or
/// derived from recorded hands; it never carries executable content or a tool permission.
/// </summary>
public sealed record RefinementContext(
    StrategyGenome Incumbent,
    SessionAnalysis Analysis,
    SessionSignatures Signatures,
    int Iteration);

/// <summary>Produces one data-only challenger genome from the measured and recorded context.</summary>
public interface IRefinementPolicy
{
    RefinementProposal Propose(RefinementContext context);
}

/// <summary>The fixed rationale-code vocabulary the refinement policy may return.</summary>
public static class RefinementRationaleCodes
{
    public const string FoldToRaise = "fold-to-raise";

    public const string CallWithWeakHand = "call-with-weak-hand";

    public const string MissedValue = "missed-value";

    public const string AggressionLow = "aggression-by-street-low";

    public const string AggressionHigh = "aggression-by-street-high";

    public const string ShowdownLoss = "showdown-loss";

    public const string StackOffLoss = "stack-off-loss";

    public const string ClampedFlip = "clamped-flip";

    public const string LegacySessionDriven = "legacy-session-driven";
}
