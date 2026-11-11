namespace Wip.Tholdem.Agents.Learning.Refinement;

/// <summary>
/// A conservative overlay on the deterministic proposer: a fold-to-raise signal that is observed
/// at the ten-sample floor with a high direction selects the call-threshold edit, but the step
/// stays at the deterministic 0.02 instead of the diagnosis policy's 0.08 jump. Every other
/// signal, an empty signature set, a below-floor signal, or an edit that is behaviourally inert
/// delegates to the deterministic proposer, so the class never widens the proposal space.
/// </summary>
/// <remarks>
/// The proposer is pure data in and data out: it reads no clock, GUID, culture or ambient random
/// source, so the same signatures, incumbent, analysis and iteration yield the same genome.
/// </remarks>
public sealed class SignatureGuardedProposer : IStrategyProposer
{
    /// <summary>
    /// The deterministic step. The guard deliberately keeps this value rather than reusing
    /// <see cref="SignatureDrivenRefinementPolicy.Step"/>, whose 0.08 jump it exists to avoid.
    /// </summary>
    public const double Step = 0.02;

    /// <summary>The inclusive ten-sample floor a fold-to-raise signal must reach to be usable.</summary>
    private const int MinimumSample = 10;

    private readonly SessionSignatures _signatures;

    private readonly IStrategyProposer _fallback;

    /// <param name="signatures">
    /// The measured failure signatures. They are supplied once, matching the recorded session the
    /// loop analyzed, and are read-only for the lifetime of the guard.
    /// </param>
    /// <param name="fallback">
    /// The proposer used when the guard makes no edit. A null value uses
    /// <see cref="DeterministicStrategyProposer"/>.
    /// </param>
    public SignatureGuardedProposer(SessionSignatures signatures, IStrategyProposer? fallback = null)
    {
        ArgumentNullException.ThrowIfNull(signatures);
        _signatures = signatures;
        _fallback = fallback ?? new DeterministicStrategyProposer();
    }

    public StrategyGenome Propose(StrategyGenome incumbent, SessionAnalysis analysis, int iteration)
    {
        ArgumentNullException.ThrowIfNull(incumbent);
        ArgumentNullException.ThrowIfNull(analysis);

        if (!HasFoldToRaiseHigh())
        {
            return _fallback.Propose(incumbent, analysis, iteration);
        }

        var guarded = (incumbent with { CallThreshold = incumbent.CallThreshold - Step }).Clamp();
        if (!guarded.BehaviorEquals(incumbent))
        {
            return guarded;
        }

        // The implicated threshold was already at its lower boundary, so the guarded edit would be
        // behaviourally inert: hand the proposal back to the deterministic path.
        return _fallback.Propose(incumbent, analysis, iteration);
    }

    /// <summary>
    /// True when at least one observed fold-to-raise signal clears the ten-sample floor with a high
    /// direction. Every qualifying signal selects the same guarded edit, so presence is enough and
    /// the result does not depend on signal order or rate ties.
    /// </summary>
    private bool HasFoldToRaiseHigh() =>
        _signatures.Signals.Any(static signal =>
            string.Equals(signal.Name, FailureSignalNames.FoldToRaise, StringComparison.Ordinal)
            && signal.Sample >= MinimumSample
            && string.Equals(signal.Direction, FailureSignalDirections.High, StringComparison.Ordinal));
}
