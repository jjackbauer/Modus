using System.Text;
using Wip.Tholdem.Agents.Learning;
using Wip.Tholdem.Agents.Simulation;

namespace Wip.Tholdem.Agents.Evaluation;

/// <summary>
/// The pre-registered four-seat evaluation plan: exactly one protected seed domain per purpose, the
/// versioned game contract, the opponent pool and compositions, and every inferential setting that
/// must be fixed before candidates are evaluated. The plan is content addressed by its canonical
/// JSON, which embeds the game and each seed domain verbatim. There is no clock, GUID, culture or
/// ambient randomness.
/// </summary>
public sealed record EvaluationPlanV2
{
    public const string Schema = "tholdem.evaluation-plan.v2";
    public const string EvaluatorVersion = "tholdem.population-evaluator.v2.0.0";

    private EvaluationPlanV2(
        GameConfiguration game,
        string opponentPoolId,
        IReadOnlyList<string> compositions,
        SeedDomain training,
        SeedDomain selection,
        SeedDomain holdout,
        SeedDomain audit,
        int handsPerSeatMatch,
        double minimumPracticalEffectBbPer100,
        double confidenceLevel,
        int plannedCandidates,
        double anchorRegressionToleranceBbPer100)
    {
        Game = game;
        OpponentPoolId = opponentPoolId;
        Compositions = compositions;
        Training = training;
        Selection = selection;
        Holdout = holdout;
        Audit = audit;
        Domains = new[] { training, selection, holdout, audit };
        HandsPerSeatMatch = handsPerSeatMatch;
        MinimumPracticalEffectBbPer100 = minimumPracticalEffectBbPer100;
        ConfidenceLevel = confidenceLevel;
        PlannedCandidates = plannedCandidates;
        AnchorRegressionToleranceBbPer100 = anchorRegressionToleranceBbPer100;
    }

    public GameConfiguration Game { get; }

    public string OpponentPoolId { get; }

    public IReadOnlyList<string> Compositions { get; }

    public IReadOnlyList<SeedDomain> Domains { get; }

    public int HandsPerSeatMatch { get; }

    public double MinimumPracticalEffectBbPer100 { get; }

    public double ConfidenceLevel { get; }

    public int PlannedCandidates { get; }

    public double AnchorRegressionToleranceBbPer100 { get; }

    public SeedDomain Training { get; }

    public SeedDomain Selection { get; }

    public SeedDomain Holdout { get; }

    public SeedDomain Audit { get; }

    /// <summary>The number of protected holdout blocks, one per held-out match.</summary>
    public int SeedBlocks => Holdout.Count;

    /// <summary>The per-comparison false-positive rate implied by <see cref="ConfidenceLevel"/>.</summary>
    public double Alpha => 1.0 - ConfidenceLevel;

    /// <summary>The content address of the canonical plan.</summary>
    public string PlanHash => StrategyArtifact.ComputeHash(ToCanonicalJson());

    /// <summary>
    /// The fixed-key-order, culture-free JSON: the game and every seed domain are embedded as their
    /// canonical objects, and domains are written in the fixed purpose order.
    /// </summary>
    public string ToCanonicalJson()
    {
        var builder = new StringBuilder(1024);
        builder.Append("{\"schema\":");
        CanonicalJsonWriter.Text(builder, Schema);
        builder.Append(",\"evaluatorVersion\":");
        CanonicalJsonWriter.Text(builder, EvaluatorVersion);
        builder.Append(",\"game\":");
        builder.Append(Game.ToCanonicalJson());
        builder.Append(",\"opponentPoolId\":");
        CanonicalJsonWriter.Text(builder, OpponentPoolId);
        builder.Append(",\"compositions\":[");
        for (var index = 0; index < Compositions.Count; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            CanonicalJsonWriter.Text(builder, Compositions[index]);
        }

        builder.Append("],\"domains\":[");
        for (var index = 0; index < Domains.Count; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            builder.Append(Domains[index].ToCanonicalJson());
        }

        builder.Append("],\"handsPerSeatMatch\":");
        CanonicalJsonWriter.Number(builder, HandsPerSeatMatch);
        builder.Append(",\"minimumPracticalEffectBbPer100\":");
        CanonicalJsonWriter.Number(builder, MinimumPracticalEffectBbPer100);
        builder.Append(",\"confidenceLevel\":");
        CanonicalJsonWriter.Number(builder, ConfidenceLevel);
        builder.Append(",\"plannedCandidates\":");
        CanonicalJsonWriter.Number(builder, PlannedCandidates);
        builder.Append(",\"anchorRegressionToleranceBbPer100\":");
        CanonicalJsonWriter.Number(builder, AnchorRegressionToleranceBbPer100);
        builder.Append('}');
        return builder.ToString();
    }

    /// <summary>
    /// Validates every plan rule and builds the immutable, purpose-ordered plan. Refusals are
    /// <see cref="InvalidOperationException"/> for a missing or duplicate domain (and for overlap,
    /// via <see cref="SeedDomainOverlapException"/>), <see cref="GameConfigurationException"/> for an
    /// invalid game, and <see cref="ArgumentException"/> for invalid plan arguments.
    /// </summary>
    public static EvaluationPlanV2 Create(
        GameConfiguration game,
        string opponentPoolId,
        IReadOnlyList<string> compositions,
        IReadOnlyList<SeedDomain> domains,
        int handsPerSeatMatch,
        double minimumPracticalEffectBbPer100,
        double confidenceLevel,
        int plannedCandidates,
        double anchorRegressionToleranceBbPer100)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(opponentPoolId);
        ArgumentNullException.ThrowIfNull(compositions);
        ArgumentNullException.ThrowIfNull(domains);

        var byPurpose = new Dictionary<SeedPurpose, SeedDomain>();
        foreach (var domain in domains)
        {
            ArgumentNullException.ThrowIfNull(domain);
            if (!byPurpose.TryAdd(domain.Purpose, domain))
            {
                throw new InvalidOperationException(
                    "The evaluation plan must declare exactly one seed domain per purpose; "
                    + $"purpose '{domain.Purpose}' is declared more than once.");
            }
        }

        foreach (var purpose in new[]
                 {
                     SeedPurpose.Training, SeedPurpose.Selection, SeedPurpose.Holdout, SeedPurpose.Audit,
                 })
        {
            if (!byPurpose.ContainsKey(purpose))
            {
                throw new InvalidOperationException(
                    $"The evaluation plan is missing the required '{purpose}' seed domain.");
            }
        }

        SeedDomain.ValidateDisjoint(domains);
        game.Validate();

        if (string.IsNullOrWhiteSpace(opponentPoolId))
        {
            throw new ArgumentException(
                "The opponent pool id must be non-empty.",
                nameof(opponentPoolId));
        }

        if (compositions.Count == 0)
        {
            throw new ArgumentException(
                "The evaluation plan must declare at least one composition.",
                nameof(compositions));
        }

        var compositionIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var composition in compositions)
        {
            if (string.IsNullOrWhiteSpace(composition))
            {
                throw new ArgumentException(
                    "A composition id must be non-empty.",
                    nameof(compositions));
            }

            if (!compositionIds.Add(composition))
            {
                throw new ArgumentException(
                    $"Composition id '{composition}' is declared more than once.",
                    nameof(compositions));
            }
        }

        if (handsPerSeatMatch < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(handsPerSeatMatch),
                handsPerSeatMatch,
                "At least one hand per seat match is required.");
        }

        if (!double.IsFinite(minimumPracticalEffectBbPer100) || minimumPracticalEffectBbPer100 < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumPracticalEffectBbPer100),
                minimumPracticalEffectBbPer100,
                "The minimum practical effect must be a finite, non-negative bb/100 value.");
        }

        if (!double.IsFinite(confidenceLevel) || confidenceLevel <= 0 || confidenceLevel >= 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(confidenceLevel),
                confidenceLevel,
                "The confidence level must lie strictly between zero and one.");
        }

        if (plannedCandidates < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(plannedCandidates),
                plannedCandidates,
                "At least one planned candidate is required.");
        }

        if (!double.IsFinite(anchorRegressionToleranceBbPer100)
            || anchorRegressionToleranceBbPer100 < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(anchorRegressionToleranceBbPer100),
                anchorRegressionToleranceBbPer100,
                "The anchor regression tolerance must be a finite, non-negative bb/100 value.");
        }

        return new EvaluationPlanV2(
            game.Normalized(),
            opponentPoolId,
            compositions.ToArray(),
            byPurpose[SeedPurpose.Training],
            byPurpose[SeedPurpose.Selection],
            byPurpose[SeedPurpose.Holdout],
            byPurpose[SeedPurpose.Audit],
            handsPerSeatMatch,
            minimumPracticalEffectBbPer100,
            confidenceLevel,
            plannedCandidates,
            anchorRegressionToleranceBbPer100);
    }
}
