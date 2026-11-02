using Wip.Bones.Domain;
using Wip.Bones.Identifiers;

namespace Wip.Bones.Agents.Knowledge;

public sealed record BonesStrategyDocument
{
    public BonesStrategyDocument(BonesStrategyId strategyId, BonesPlayerId playerId, string markdown)
    {
        StrategyId = strategyId;
        PlayerId = playerId;
        Markdown = markdown ?? throw new ArgumentNullException(nameof(markdown));
    }

    public BonesStrategyId StrategyId { get; }

    public BonesPlayerId PlayerId { get; }

    public string Markdown { get; }
}

public sealed record BonesStrategyArtifact
{
    public BonesStrategyArtifact(
        BonesStrategyId strategyId,
        BonesPlayerId playerId,
        BonesStrategyKind kind,
        string source,
        BonesPromotionStatus promotionStatus)
    {
        StrategyId = strategyId;
        PlayerId = playerId;
        Kind = kind;
        PromotionStatus = promotionStatus;
        Source = ValidateSource(kind, source);
    }

    public BonesStrategyId StrategyId { get; }

    public BonesPlayerId PlayerId { get; }

    public BonesStrategyKind Kind { get; }

    public string Source { get; }

    public BonesPromotionStatus PromotionStatus { get; }

    public static BonesStrategyArtifact FromLegacyDocument(
        BonesStrategyDocument document,
        BonesPromotionStatus promotionStatus = BonesPromotionStatus.Active)
    {
        ArgumentNullException.ThrowIfNull(document);

        return new BonesStrategyArtifact(
            document.StrategyId,
            document.PlayerId,
            BonesStrategyKind.Markdown,
            document.Markdown,
            promotionStatus);
    }

    private static string ValidateSource(BonesStrategyKind kind, string source)
    {
        _ = kind;

        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        if (string.IsNullOrWhiteSpace(source))
        {
            throw new ArgumentException("Source cannot be null or whitespace.", nameof(source));
        }

        return source;
    }
}

public sealed record BonesObservationTranscript
{
    public BonesObservationTranscript(BonesGameId gameId, BonesPlayerId observerId, string markdown)
    {
        GameId = gameId;
        ObserverId = observerId;
        Markdown = markdown ?? throw new ArgumentNullException(nameof(markdown));
    }

    public BonesGameId GameId { get; }

    public BonesPlayerId ObserverId { get; }

    public string Markdown { get; }
}

public sealed record BonesMatchHistoryTranscript
{
    public BonesMatchHistoryTranscript(BonesGameId gameId, BonesPlayerId playerId, string markdown)
    {
        GameId = gameId;
        PlayerId = playerId;
        Markdown = markdown ?? throw new ArgumentNullException(nameof(markdown));
    }

    public BonesGameId GameId { get; }

    public BonesPlayerId PlayerId { get; }

    public string Markdown { get; }
}

public sealed record BonesStrategyEffectivenessRecord
{
    public BonesStrategyEffectivenessRecord(
        BonesStrategyId strategyId,
        BonesPlayerId playerId,
        int matchesPlayed,
        int wins,
        int losses,
        int cumulativeScoreDifferential)
    {
        StrategyId = strategyId;
        PlayerId = playerId;

        if (matchesPlayed < 0)
            throw new ArgumentOutOfRangeException(nameof(matchesPlayed));

        if (wins < 0)
            throw new ArgumentOutOfRangeException(nameof(wins));

        if (losses < 0)
            throw new ArgumentOutOfRangeException(nameof(losses));

        if (wins + losses > matchesPlayed)
        {
            throw new ArgumentException(
                "Wins and losses cannot exceed matches played.",
                nameof(matchesPlayed));
        }

        MatchesPlayed = matchesPlayed;
        Wins = wins;
        Losses = losses;
        CumulativeScoreDifferential = cumulativeScoreDifferential;
    }

    public BonesStrategyId StrategyId { get; }

    public BonesPlayerId PlayerId { get; }

    public int MatchesPlayed { get; }

    public int Wins { get; }

    public int Losses { get; }

    public int CumulativeScoreDifferential { get; }

    public static BonesStrategyEffectivenessRecord Empty(
        BonesStrategyId strategyId,
        BonesPlayerId playerId)
        => new(strategyId, playerId, matchesPlayed: 0, wins: 0, losses: 0, cumulativeScoreDifferential: 0);

    public BonesStrategyEffectivenessRecord RecordWin(int scoreDifferential)
        => new(
            StrategyId,
            PlayerId,
            MatchesPlayed + 1,
            Wins + 1,
            Losses,
            CumulativeScoreDifferential + scoreDifferential);

    public BonesStrategyEffectivenessRecord RecordLoss(int scoreDifferential)
        => new(
            StrategyId,
            PlayerId,
            MatchesPlayed + 1,
            Wins,
            Losses + 1,
            CumulativeScoreDifferential + scoreDifferential);
}

internal sealed record BonesStrategyArtifactPersistence(
    string StrategyId,
    int PlayerSeat,
    BonesStrategyKind Kind,
    BonesPromotionStatus PromotionStatus,
    string Source)
{
    public static BonesStrategyArtifactPersistence FromArtifact(BonesStrategyArtifact artifact)
        => new(
            artifact.StrategyId.Value,
            artifact.PlayerId.Seat,
            artifact.Kind,
            artifact.PromotionStatus,
            artifact.Source);

    public BonesStrategyArtifact ToArtifact(BonesPlayerId playerId)
    {
        if (playerId.Seat != PlayerSeat)
        {
            throw new InvalidOperationException(
                $"Strategy artifact seat {PlayerSeat} does not match player seat {playerId.Seat}.");
        }

        return new BonesStrategyArtifact(
            new BonesStrategyId(StrategyId),
            playerId,
            Kind,
            Source,
            PromotionStatus);
    }
}

public sealed record BonesStrategyMatchOutcome(
    BonesPlayerId Winner,
    IReadOnlyDictionary<BonesPlayerId, int> FinalScores)
{
    public int GetScoreDifferentialForPlayer(BonesPlayerId playerId)
    {
        ArgumentNullException.ThrowIfNull(FinalScores);

        if (!FinalScores.TryGetValue(playerId, out var playerScore))
        {
            throw new InvalidOperationException(
                $"Final score for player seat {playerId.Seat} was not recorded in the match outcome.");
        }

        if (!FinalScores.TryGetValue(Winner, out var winnerScore))
        {
            throw new InvalidOperationException(
                $"Final score for match winner seat {Winner.Seat} was not recorded in the match outcome.");
        }

        return playerScore - winnerScore;
    }
}

public enum BonesPromotionGateMode
{
    LegacyOr = 0,
    ConfidenceComposite = 1,
}

public sealed class BonesStrategyPromotionOptions
{
    public int EvaluationMatchCount { get; set; } = 40;

    /// <summary>
    /// Maximum evaluation matches allowed to run concurrently for one seat.
    /// Evaluation matches are CPU-bound; unbounded fan-out starves the thread pool.
    /// </summary>
    public int MaxConcurrentMatches { get; set; } = 8;

    public double MinimumWinRateImprovement { get; set; } = 0.05;

    public int MinimumScoreDifferentialImprovement { get; set; } = 1;

    /// <summary>
    /// Minimum number of distinct seats from which a candidate strategy must
    /// achieve at least MinimumWinRateImprovement to be promoted.
    /// Default 2 means the candidate must beat the incumbent from at least two
    /// different seat positions.
    /// </summary>
    public int MinimumCrossSeatCount { get; set; } = 2;

    /// <summary>
    /// Promotion decision rule. <see cref="BonesPromotionGateMode.ConfidenceComposite"/>
    /// combines paired score evidence and win-rate evidence into a z-score and requires
    /// at least a minimum amount of evidence; <see cref="BonesPromotionGateMode.LegacyOr"/>
    /// preserves the earlier "either metric passes" OR rule.
    /// </summary>
    public BonesPromotionGateMode GateMode { get; set; } = BonesPromotionGateMode.ConfidenceComposite;

    /// <summary>
    /// When true, the candidate and incumbent are evaluated on the same deal seed
    /// (paired design). Pairing removes deal-to-deal variance and increases the power
    /// of the promotion decision at the same match cost.
    /// </summary>
    public bool UsePairedEvaluationSeeds { get; set; } = true;

    /// <summary>
    /// Composite z-score required by <see cref="BonesPromotionGateMode.ConfidenceComposite"/>.
    /// One-sided 84% confidence corresponds to 1.0.
    /// </summary>
    public double ConfidenceZThreshold { get; set; } = 1.0;

    /// <summary>
    /// A candidate is rejected when its win-rate delta is below this floor, regardless
    /// of its score evidence.
    /// </summary>
    public double WinRateHardFloor { get; set; } = -0.10;

    /// <summary>
    /// A candidate is rejected when its score-differential delta is below this floor,
    /// regardless of its win-rate evidence.
    /// </summary>
    public double ScoreDifferentialHardFloor { get; set; } = -2.0;
}

internal sealed record BonesStrategyPromotionDecisionPersistence(
    string IncumbentStrategyId,
    int LearningPlayerSeat,
    string CandidateStrategyId,
    string Outcome,
    int EvaluationMatchCount,
    int CandidateWins,
    int IncumbentWins,
    double CandidateWinRate,
    double IncumbentWinRate,
    double WinRateImprovement,
    double CandidateAverageScore,
    double IncumbentAverageScore,
    double ScoreDifferentialDelta)
{
    public static BonesStrategyPromotionDecisionPersistence FromDecision(
        BonesPlayerId learningPlayerId,
        BonesPromotionDecision decision)
        => new(
            decision.IncumbentId.Value,
            learningPlayerId.Seat,
            decision.CandidateId.Value,
            decision.Outcome.ToString(),
            decision.Metrics.EvaluationMatchCount,
            decision.Metrics.CandidateWins,
            decision.Metrics.IncumbentWins,
            decision.Metrics.CandidateWinRate,
            decision.Metrics.IncumbentWinRate,
            decision.Metrics.WinRateImprovement,
            decision.Metrics.CandidateAverageScore,
            decision.Metrics.IncumbentAverageScore,
            decision.Metrics.ScoreDifferentialDelta);
}

internal sealed record BonesActiveStrategyPointerPersistence(
    string StrategyId,
    int PlayerSeat)
{
    public static BonesActiveStrategyPointerPersistence FromPointer(
        BonesStrategyId strategyId,
        BonesPlayerId playerId)
        => new(strategyId.Value, playerId.Seat);

    public BonesStrategyId ToStrategyId(BonesPlayerId playerId)
    {
        if (playerId.Seat != PlayerSeat)
        {
            throw new InvalidOperationException(
                $"Active strategy pointer seat {PlayerSeat} does not match player seat {playerId.Seat}.");
        }

        return new BonesStrategyId(StrategyId);
    }
}

internal sealed record BonesStrategyEffectivenessPersistence(
    string StrategyId,
    int PlayerSeat,
    int MatchesPlayed,
    int Wins,
    int Losses,
    int CumulativeScoreDifferential)
{
    public static BonesStrategyEffectivenessPersistence FromRecord(BonesStrategyEffectivenessRecord record)
        => new(
            record.StrategyId.Value,
            record.PlayerId.Seat,
            record.MatchesPlayed,
            record.Wins,
            record.Losses,
            record.CumulativeScoreDifferential);

    public BonesStrategyEffectivenessRecord ToRecord(BonesPlayerId playerId)
    {
        if (playerId.Seat != PlayerSeat)
        {
            throw new InvalidOperationException(
                $"Effectiveness record seat {PlayerSeat} does not match player seat {playerId.Seat}.");
        }

        return new BonesStrategyEffectivenessRecord(
            new BonesStrategyId(StrategyId),
            playerId,
            MatchesPlayed,
            Wins,
            Losses,
            CumulativeScoreDifferential);
    }
}

public sealed record BonesStrategyLineageEntry(
    BonesStrategyId PredecessorStrategyId,
    BonesStrategyId SuccessorStrategyId,
    BonesPlayerId PlayerId,
    BonesPromotionDecisionOutcome PromotionOutcome,
    string DiffSummary,
    DateTimeOffset RecordedAtUtc);

internal sealed record BonesStrategyLineagePersistence(
    string PredecessorStrategyId,
    string SuccessorStrategyId,
    int PlayerSeat,
    string PromotionOutcome,
    string DiffSummary,
    DateTimeOffset RecordedAtUtc)
{
    public static BonesStrategyLineagePersistence FromEntry(BonesStrategyLineageEntry entry)
        => new(
            entry.PredecessorStrategyId.Value,
            entry.SuccessorStrategyId.Value,
            entry.PlayerId.Seat,
            entry.PromotionOutcome.ToString(),
            entry.DiffSummary,
            entry.RecordedAtUtc);

    public BonesStrategyLineageEntry ToEntry()
        => new(
            new BonesStrategyId(PredecessorStrategyId),
            new BonesStrategyId(SuccessorStrategyId),
            new BonesPlayerId(PlayerSeat),
            Enum.TryParse<BonesPromotionDecisionOutcome>(PromotionOutcome, out var outcome)
                ? outcome
                : BonesPromotionDecisionOutcome.Rejected,
            DiffSummary,
            RecordedAtUtc);
}

public sealed record BonesStrategyCandidateEntry(
    BonesStrategyId StrategyId,
    BonesPlayerId PlayerId,
    BonesStrategyKind Kind,
    string Source,
    BonesPromotionEvaluationMetrics EvaluationMetrics,
    DateTimeOffset StoredAtUtc);

internal sealed record BonesStrategyCandidatePersistence(
    string StrategyId,
    int PlayerSeat,
    string Kind,
    string Source,
    int EvaluationMatchCount,
    int CandidateWins,
    int IncumbentWins,
    double CandidateWinRate,
    double IncumbentWinRate,
    double WinRateImprovement,
    double CandidateAverageScore,
    double IncumbentAverageScore,
    double ScoreDifferentialDelta,
    DateTimeOffset StoredAtUtc)
{
    public static BonesStrategyCandidatePersistence FromEntry(BonesStrategyCandidateEntry entry)
        => new(
            entry.StrategyId.Value,
            entry.PlayerId.Seat,
            entry.Kind.ToString(),
            entry.Source,
            entry.EvaluationMetrics.EvaluationMatchCount,
            entry.EvaluationMetrics.CandidateWins,
            entry.EvaluationMetrics.IncumbentWins,
            entry.EvaluationMetrics.CandidateWinRate,
            entry.EvaluationMetrics.IncumbentWinRate,
            entry.EvaluationMetrics.WinRateImprovement,
            entry.EvaluationMetrics.CandidateAverageScore,
            entry.EvaluationMetrics.IncumbentAverageScore,
            entry.EvaluationMetrics.ScoreDifferentialDelta,
            entry.StoredAtUtc);

    public BonesStrategyCandidateEntry ToEntry(BonesPlayerId playerId)
        => new(
            new BonesStrategyId(StrategyId),
            playerId,
            Enum.TryParse<BonesStrategyKind>(Kind, out var kind)
                ? kind
                : BonesStrategyKind.Markdown,
            Source,
            new BonesPromotionEvaluationMetrics(
                EvaluationMatchCount,
                CandidateWins,
                IncumbentWins,
                CandidateWinRate,
                IncumbentWinRate,
                WinRateImprovement,
                CandidateAverageScore,
                IncumbentAverageScore,
                ScoreDifferentialDelta,
                0, // StandardError not stored
                0), // ConfidenceInterval95 not stored
            StoredAtUtc);
}