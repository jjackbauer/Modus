namespace Wip.Tholdem.Agents.Learning.Refinement;

/// <summary>
/// A first-class <see cref="IStrategyProposer"/> adapter for measured failure signatures: it wraps
/// <see cref="SignatureDrivenRefinementPolicy"/> with a fixed signature set, so the strongest
/// usable diagnosis selects one bounded genome edit. An empty signature set, or one whose signals
/// all fall below the policy's sample and rate floors, produces the deterministic proposer's
/// challenger through the policy's legacy path. The adapter is pure data in and data out: it has no
/// clock, GUID, culture or ambient random source, so the same inputs yield the same genome.
/// </summary>
public sealed class DiagnosisDrivenProposer : IStrategyProposer
{
    private readonly SessionSignatures _signatures;

    private readonly SignatureDrivenRefinementPolicy _policy;

    /// <param name="signatures">
    /// The measured failure signatures. They are supplied once, matching the recorded session the
    /// loop analyzed, and are read-only for the lifetime of the adapter.
    /// </param>
    /// <param name="fallback">
    /// The deterministic proposer the policy delegates to when no signal is usable. A null value
    /// uses <see cref="DeterministicStrategyProposer"/> exactly as the shipped policy does.
    /// </param>
    /// <param name="strategy">
    /// Which refinement path the wrapped policy takes. The default is the signature-driven path;
    /// <see cref="RefinementStrategy.SessionDriven"/> keeps the adapter equal to the legacy
    /// proposal even when signals are present.
    /// </param>
    /// <param name="minimumSignalRate">The inclusive positive-evidence floor, in [0, 1].</param>
    public DiagnosisDrivenProposer(
        SessionSignatures signatures,
        IStrategyProposer? fallback = null,
        RefinementStrategy strategy = RefinementStrategy.SignatureDriven,
        double minimumSignalRate = 0.0)
    {
        ArgumentNullException.ThrowIfNull(signatures);
        _signatures = signatures;
        _policy = new SignatureDrivenRefinementPolicy(
            fallback,
            enableSkillEdits: false,
            strategy,
            minimumSignalRate);
    }

    /// <summary>
    /// Returns the challenger genome the shipped refinement policy derives from this adapter's
    /// signature set and the caller's measured analysis.
    /// </summary>
    public StrategyGenome Propose(StrategyGenome incumbent, SessionAnalysis analysis, int iteration)
    {
        ArgumentNullException.ThrowIfNull(incumbent);
        ArgumentNullException.ThrowIfNull(analysis);

        return _policy.Propose(new RefinementContext(incumbent, analysis, _signatures, iteration)).Genome;
    }
}
