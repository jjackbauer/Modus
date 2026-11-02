using System.Text.Json;
using Wip.Bones.Agents.Champions;
using Wip.Bones.Agents.Knowledge;
using Wip.Bones.Domain;
using Wip.Bones.Identifiers;

namespace Wip.Bones.Benchmark;

public sealed record ChampionRunOptions(
    string SnapshotPath,
    string OutputDirectory,
    int Seat = 1,
    int GamesPerSeed = 2000,
    IReadOnlyList<int>? Seeds = null,
    int TargetScore = 12,
    string? IncumbentId = null,
    int MaxDegree = 8,
    double MinLcbDelta = 0.0);

public sealed record ChampionSeedRun(
    int Seed,
    int GamesPerStrategy,
    int Strategies,
    int TotalGames,
    int TotalWins,
    double TotalWinRate);

public sealed record ChampionRunResult(
    string SelectedStrategyId,
    string Outcome,
    int Seat,
    int Games,
    int Wins,
    double WinRate,
    double Lcb,
    string? IncumbentStrategyId,
    int IncumbentGames,
    int IncumbentWins,
    double IncumbentWinRate,
    double IncumbentLcb,
    IReadOnlyList<string> CompileFailures,
    int CandidateCount,
    string? LibraryPath,
    bool LibraryWritten,
    IReadOnlyList<int> Seeds,
    IReadOnlyList<ChampionSeedRun> Runs);

public static class ChampionRunner
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly JsonSerializerOptions ResultOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static ChampionRunResult Run(ChampionRunOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.Seat is < BonesPlayerId.MinSeat or > BonesPlayerId.MaxSeat)
            throw new ArgumentOutOfRangeException(nameof(options), "Seat must be between 1 and 4.");
        if (options.GamesPerSeed < 1)
            throw new ArgumentOutOfRangeException(nameof(options), "GamesPerSeed must be positive.");

        var seeds = options.Seeds is { Count: > 0 }
            ? options.Seeds.ToArray()
            : new[] { 910001, 910002 };

        var snapshot = JsonSerializer.Deserialize<HistorySnapshot>(
            File.ReadAllText(options.SnapshotPath),
            ReadOptions) ?? throw new InvalidOperationException("Unable to read history snapshot.");

        var strategies = snapshot.Strategies
            .Where(strategy => !strategy.Id.StartsWith("deterministic-seed-", StringComparison.Ordinal))
            .ToArray();

        var compiled = Arena.Compile(strategies, out var failures, out _);
        var field = new List<FieldEntry>();
        field.AddRange(DeterministicOpponents.CreateArchetypes());
        foreach (var strategy in compiled)
            field.Add(new ScriptFieldEntry(strategy.Record.Id, strategy.SlotType));

        var samples = new List<BonesChampionCandidateResult>();
        var runs = new List<ChampionSeedRun>();
        foreach (var seed in seeds)
        {
            var arena = new Arena(field, compiled, options.TargetScore, seed, $"champion-{seed}")
                .Run(options.GamesPerSeed, options.MaxDegree);

            runs.Add(new ChampionSeedRun(
                seed,
                options.GamesPerSeed,
                arena.Strategies.Count,
                arena.GamesPlayed,
                arena.Strategies.Sum(metrics => metrics.Wins),
                arena.Strategies.Sum(metrics => metrics.Wins) / (double)Math.Max(1, arena.GamesPlayed)));

            foreach (var metrics in arena.Strategies)
            {
                samples.Add(new BonesChampionCandidateResult(
                    metrics.StrategyId,
                    options.Seat,
                    metrics.Games,
                    metrics.Wins,
                    metrics.MeanScoreDifferential,
                    metrics.WinRateBySeat));
            }
        }

        var aggregated = BonesChampionAggregator.Aggregate(samples, options.Seat);
        var selection = BonesChampionSelector.Select(
            aggregated,
            confirmationResults: null,
            options.IncumbentId,
            options.Seat,
            minGames: options.GamesPerSeed * seeds.Length,
            options.MinLcbDelta);

        var selected = Find(aggregated, selection.StrategyId);
        var incumbent = Find(aggregated, options.IncumbentId);
        var libraryPath = Path.Combine(
            Path.GetFullPath(options.OutputDirectory),
            ".bones",
            "strategy-library",
            $"seat-{options.Seat}.json");
        var libraryWritten = false;

        if (selection.Outcome == BonesChampionSelectionOutcome.Promoted && selected is not null)
        {
            var record = strategies.FirstOrDefault(strategy => string.Equals(strategy.Id, selected.StrategyId, StringComparison.Ordinal))
                ?? throw new InvalidOperationException($"Selected strategy '{selected.StrategyId}' is not in the snapshot.");
            var playerId = new BonesPlayerId(options.Seat);
            var artifact = new BonesStrategyArtifact(
                new BonesStrategyId(record.Id),
                playerId,
                BonesStrategyKind.Script,
                record.Source,
                BonesPromotionStatus.Active);
            var effectiveness = new BonesStrategyEffectivenessRecord(
                artifact.StrategyId,
                playerId,
                selected.Games,
                selected.Wins,
                Math.Max(0, selected.Games - selected.Wins),
                (int)Math.Round(selected.MeanScoreDifferential * selected.Games));

            var library = new BonesStrategyLibrary(options.OutputDirectory);
            libraryWritten = library.UpsertBestStrategy(playerId, artifact, effectiveness);
        }

        var result = new ChampionRunResult(
            selection.StrategyId ?? string.Empty,
            selection.Outcome.ToString(),
            options.Seat,
            selected?.Games ?? 0,
            selected?.Wins ?? 0,
            Rate(selected),
            selected is null ? 0.0 : BonesChampionSelector.WilsonLowerBound(selected.Wins, selected.Games),
            options.IncumbentId,
            incumbent?.Games ?? 0,
            incumbent?.Wins ?? 0,
            Rate(incumbent),
            incumbent is null ? 0.0 : BonesChampionSelector.WilsonLowerBound(incumbent.Wins, incumbent.Games),
            failures,
            aggregated.Count,
            libraryPath,
            libraryWritten,
            seeds,
            runs);

        Directory.CreateDirectory(options.OutputDirectory);
        File.WriteAllText(
            Path.Combine(options.OutputDirectory, "champion-result.json"),
            JsonSerializer.Serialize(result, ResultOptions));

        Console.WriteLine(
            $"champion selected={result.SelectedStrategyId} outcome={result.Outcome} wins={result.Wins}/{result.Games} rate={result.WinRate:P3} lcb={result.Lcb:P3} incumbent={result.IncumbentStrategyId} wins={result.IncumbentWins}/{result.IncumbentGames} rate={result.IncumbentWinRate:P3} library={result.LibraryPath} written={result.LibraryWritten}");

        return result;
    }

    private static BonesChampionCandidateResult? Find(
        IReadOnlyList<BonesChampionCandidateResult> results,
        string? strategyId)
        => strategyId is null
            ? null
            : results.FirstOrDefault(result => string.Equals(result.StrategyId, strategyId, StringComparison.Ordinal));

    private static double Rate(BonesChampionCandidateResult? result)
        => result is null || result.Games == 0 ? 0.0 : result.Wins / (double)result.Games;
}