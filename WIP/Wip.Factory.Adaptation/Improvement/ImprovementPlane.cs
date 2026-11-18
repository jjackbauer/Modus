using System.Security.Cryptography;
using System.Text;
using Wip.Factory.Contracts.Identifiers;
using Wip.Factory.Contracts.Profiles;

namespace Wip.Factory.Adaptation.Improvement;

/// <summary>A signal that may justify a reflection session (F-J.1).</summary>
public sealed record ReflectionSignal(
    string Reason,
    DateTimeOffset ObservedAtUtc,
    int SampleSize,
    double? MetricValue = null);

/// <summary>Whether reflection should run, and why not when it should not.</summary>
public sealed record ReflectionDecision(bool ShouldReflect, string? Reason)
{
    public static ReflectionDecision Reflect(string reason) => new(true, reason);

    public static ReflectionDecision Skip(string reason) => new(false, reason);
}

/// <summary>
/// Decides when reflection runs. Minimum sample size, cooldown, per-window budget and deduplication
/// stop noisy metrics from oscillating the agent's methodology (F-J.1, source §15).
/// </summary>
public sealed class AgentReflectionPolicy
{
    public const string InsufficientSamples = "insufficient-samples";
    public const string CooldownActive = "cooldown-active";
    public const string BudgetExhausted = "reflection-budget-exhausted";
    public const string DuplicateReason = "duplicate-signal";

    private readonly int _minimumSamples;
    private readonly TimeSpan _cooldown;
    private readonly int _maxPerWindow;
    private readonly TimeSpan _window;

    private DateTimeOffset? _lastReflectionAtUtc;
    private readonly List<DateTimeOffset> _reflections = [];
    private readonly HashSet<string> _seenReasons = new(StringComparer.Ordinal);

    public AgentReflectionPolicy(
        int minimumSamples = 3,
        TimeSpan? cooldown = null,
        int maxPerWindow = 1,
        TimeSpan? window = null)
    {
        if (minimumSamples < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumSamples), minimumSamples, "At least one sample is required.");
        }

        _minimumSamples = minimumSamples;
        _cooldown = cooldown ?? TimeSpan.FromHours(12);
        _maxPerWindow = maxPerWindow;
        _window = window ?? TimeSpan.FromDays(1);
    }

    public ReflectionDecision Decide(ReflectionSignal signal, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(signal);

        if (signal.SampleSize < _minimumSamples)
        {
            return ReflectionDecision.Skip(InsufficientSamples);
        }

        if (_lastReflectionAtUtc is { } last && nowUtc - last < _cooldown)
        {
            return ReflectionDecision.Skip(CooldownActive);
        }

        var inWindow = _reflections.Count(at => nowUtc - at < _window);
        if (inWindow >= _maxPerWindow)
        {
            return ReflectionDecision.Skip(BudgetExhausted);
        }

        if (!_seenReasons.Add(signal.Reason + "@" + signal.ObservedAtUtc.UtcTicks))
        {
            return ReflectionDecision.Skip(DuplicateReason);
        }

        _lastReflectionAtUtc = nowUtc;
        _reflections.Add(nowUtc);
        return ReflectionDecision.Reflect(signal.Reason);
    }
}

/// <summary>An immutable hypothesis produced by a reflection session (F-J.3).</summary>
public sealed record HypothesisArtifact(
    string HypothesisId,
    string Statement,
    IReadOnlyList<string> EvidenceRefs,
    DateTimeOffset PublishedAtUtc,
    string ContentDigest);

/// <summary>Publishes hypotheses once; a published hypothesis is never edited (F-J.3).</summary>
public sealed class HypothesisLedger
{
    private readonly Dictionary<string, HypothesisArtifact> _hypotheses = new(StringComparer.Ordinal);

    public HypothesisArtifact Publish(string hypothesisId, string statement, IReadOnlyList<string> evidenceRefs, DateTimeOffset publishedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hypothesisId);
        ArgumentException.ThrowIfNullOrWhiteSpace(statement);
        ArgumentNullException.ThrowIfNull(evidenceRefs);

        if (evidenceRefs.Count == 0)
        {
            throw new ArgumentException("A hypothesis must name the evidence it rests on.", nameof(evidenceRefs));
        }

        if (_hypotheses.ContainsKey(hypothesisId))
        {
            throw new InvalidOperationException($"Hypothesis '{hypothesisId}' is already published and immutable.");
        }

        var canonical = statement + "|" + string.Join(",", evidenceRefs);
        var digest = "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();

        var artifact = new HypothesisArtifact(hypothesisId, statement, evidenceRefs, publishedAtUtc, digest);
        _hypotheses[hypothesisId] = artifact;
        return artifact;
    }

    public HypothesisArtifact? Get(string hypothesisId)
        => _hypotheses.TryGetValue(hypothesisId, out var artifact) ? artifact : null;
}

/// <summary>The mandatory hard gates. A candidate may never trade one of these for speed (F-J.8).</summary>
public sealed record HardGateResults(
    bool Compilation,
    bool Tests,
    bool RequirementCoverage,
    bool Architecture,
    bool GovernanceIntegrity,
    bool Security)
{
    public bool AllPass => Compilation && Tests && RequirementCoverage && Architecture && GovernanceIntegrity && Security;

    public IReadOnlyList<string> FailedGates()
    {
        var failed = new List<string>();
        if (!Compilation) failed.Add("compilation");
        if (!Tests) failed.Add("tests");
        if (!RequirementCoverage) failed.Add("requirement-coverage");
        if (!Architecture) failed.Add("architecture");
        if (!GovernanceIntegrity) failed.Add("governance-integrity");
        if (!Security) failed.Add("security");
        return failed;
    }
}

/// <summary>One candidate-vs-incumbent comparison (F-J.5).</summary>
public sealed record AgentEvolutionExperiment(
    string ExperimentId,
    AgentDefinitionId AgentDefinitionId,
    AgentProfileVersion Incumbent,
    AgentProfileVersion Candidate,
    int SampleSize,
    double IncumbentLatencyMs,
    double CandidateLatencyMs,
    HardGateResults IncumbentGates,
    HardGateResults CandidateGates,
    double ReworkDelta);

/// <summary>The recorded verdict, with the uncertainty the measurement actually supports (F-J.10).</summary>
public sealed record PromotionDecision(
    string Outcome,
    int SampleSize,
    double ConfidenceIntervalLower,
    double ConfidenceIntervalUpper,
    bool MissingDownstreamData,
    string Reason);

/// <summary>
/// Decides promotion on measured evidence. A candidate that is faster but regresses any hard gate, or
/// worsens rework, is rejected — a locally faster but globally worse change does not promote (F-J.8).
/// </summary>
public sealed class AgentEvolutionEvaluator
{
    public const string Promoted = "Promoted";
    public const string Rejected = "Rejected";
    public const string InsufficientEvidence = "Rejected:insufficient-samples";
    public const string HardGateRegression = "Rejected:hard-gate-regression";
    public const string ReworkRegression = "Rejected:rework-regression";

    private readonly int _minimumSamples;

    public AgentEvolutionEvaluator(int minimumSamples = 3) => _minimumSamples = minimumSamples;

    public PromotionDecision Evaluate(AgentEvolutionExperiment experiment)
    {
        ArgumentNullException.ThrowIfNull(experiment);

        if (experiment.SampleSize < _minimumSamples)
        {
            return new PromotionDecision(InsufficientEvidence, experiment.SampleSize, 0, 0, true, InsufficientEvidence);
        }

        var failedGates = experiment.CandidateGates.FailedGates();
        if (failedGates.Count > 0 && experiment.IncumbentGates.AllPass)
        {
            return new PromotionDecision(
                HardGateRegression,
                experiment.SampleSize,
                0,
                0,
                false,
                HardGateRegression + ":" + string.Join(",", failedGates));
        }

        if (experiment.ReworkDelta > 0)
        {
            return new PromotionDecision(
                ReworkRegression,
                experiment.SampleSize,
                0,
                0,
                false,
                ReworkRegression);
        }

        // A paired comparison over the same corpus: the interval is on the latency improvement.
        var improvement = experiment.IncumbentLatencyMs - experiment.CandidateLatencyMs;
        var standardError = Math.Abs(improvement) / Math.Sqrt(experiment.SampleSize);
        var halfWidth = 1.96 * standardError;

        return improvement <= 0
            ? new PromotionDecision(Rejected, experiment.SampleSize, -halfWidth, halfWidth, true, "no improvement")
            : new PromotionDecision(Promoted, experiment.SampleSize, improvement - halfWidth, improvement + halfWidth, false, Promoted);
    }
}
