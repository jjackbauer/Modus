namespace Wip.Bones.Benchmark;

public sealed record StrategyRecord(string Id, int Seat, string Source);

public sealed record PromotionDecisionRecord(
    int Seat,
    string IncumbentId,
    string CandidateId,
    string Outcome,
    int EvaluationMatchCount,
    int CandidateWins,
    int IncumbentWins,
    double WinRateImprovement,
    double ScoreDifferentialDelta,
    double CandidateAverageScore,
    double IncumbentAverageScore);

public sealed record StudyPair(
    int Seat,
    string IncumbentKey,
    string CandidateKey,
    string IncumbentId,
    string CandidateId,
    string IncumbentHash,
    string CandidateHash,
    string ExampleOutcome,
    int PromotedCount,
    int RejectedCount);

public sealed class StrategyBank
{
    public required IReadOnlyList<StrategyRecord> Strategies { get; init; }
}

public sealed class HistorySnapshot
{
    public required int SessionCount { get; init; }
    public required int PromotionArtifactCount { get; init; }
    public required int StrategyArtifactCount { get; init; }
    public required int ResolvedDecisionCount { get; init; }
    public required int DistinctContentPairCount { get; init; }
    public required int IdenticalSourceDecisionCount { get; init; }
    public required int IdenticalSourcePromotedCount { get; init; }
    public required IReadOnlyList<StrategyRecord> Strategies { get; init; }
    public required IReadOnlyList<StudyPair> StudyPairs { get; init; }
    public required IReadOnlyList<PromotionDecisionRecord> Decisions { get; init; }
}

public sealed record StrategyArenaMetrics(
    string StrategyId,
    int Seat,
    int Games,
    int Wins,
    double WinRate,
    double WinRateCiLow,
    double WinRateCiHigh,
    double MeanScore,
    double MeanBestOpponentScore,
    double MeanScoreDifferential,
    double ScoreDifferentialCiLow,
    double ScoreDifferentialCiHigh,
    double ScoreDifferentialStandardDeviation,
    double MeanRounds,
    IReadOnlyDictionary<int, double> WinRateBySeat);

public sealed class ArenaResult
{
    public required string Label { get; init; }
    public required int MasterSeed { get; init; }
    public required int GamesPerStrategy { get; init; }
    public required int TargetScore { get; init; }
    public required string FieldDescription { get; init; }
    public required IReadOnlyList<StrategyArenaMetrics> Strategies { get; init; }
    public required double ElapsedMilliseconds { get; init; }
    public required int GamesPlayed { get; init; }
    public double MillisecondsPerGame => GamesPlayed == 0 ? 0 : ElapsedMilliseconds / GamesPlayed;
}
