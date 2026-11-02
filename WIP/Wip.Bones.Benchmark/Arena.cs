using System.Collections.Concurrent;
using Wip.Bones.Domain;
using Wip.Bones.Engine;
using Wip.Bones.Identifiers;

namespace Wip.Bones.Benchmark;

public sealed class Arena
{
    private readonly IReadOnlyList<FieldEntry> _field;
    private readonly IReadOnlyList<CompiledStrategy> _strategies;
    private readonly int _targetScore;
    private readonly int _masterSeed;
    private readonly string _label;

    public Arena(
        IReadOnlyList<FieldEntry> field,
        IReadOnlyList<CompiledStrategy> strategies,
        int targetScore,
        int masterSeed,
        string label)
    {
        _field = field;
        _strategies = strategies;
        _targetScore = targetScore;
        _masterSeed = masterSeed;
        _label = label;
    }

    public static IReadOnlyList<CompiledStrategy> Compile(
        IReadOnlyList<StrategyRecord> strategies,
        out IReadOnlyList<string> failures,
        out double compileMilliseconds)
    {
        var started = System.Diagnostics.Stopwatch.StartNew();
        var host = new BonesStrategyScriptHost();
        var compiled = new List<CompiledStrategy>(strategies.Count);
        var failed = new List<string>();

        foreach (var strategy in strategies)
        {
            var result = host.TryCompile(new Wip.Bones.Identifiers.BonesStrategyId(strategy.Id), strategy.Source);
            if (!result.Succeeded || result.Compiled is null)
            {
                failed.Add($"{strategy.Id}: {result.FailureReason}");
                continue;
            }

            compiled.Add(new CompiledStrategy(strategy, result.Compiled.PlayerSlot.GetType()));
        }

        compileMilliseconds = started.Elapsed.TotalMilliseconds;
        failures = failed;
        return compiled;
    }

    public ArenaResult Run(int gamesPerStrategy, int maxDegreeOfParallelism)
    {
        var started = System.Diagnostics.Stopwatch.StartNew();
        var results = new ConcurrentDictionary<string, StrategyArenaMetrics>(StringComparer.Ordinal);
        var gamesPlayed = 0;

        Parallel.ForEach(
            _strategies,
            new ParallelOptions { MaxDegreeOfParallelism = maxDegreeOfParallelism },
            strategy =>
            {
                var perGame = new List<GameOutcome>(gamesPerStrategy);
                for (var gameIndex = 0; gameIndex < gamesPerStrategy; gameIndex++)
                {
                    perGame.Add(RunGame(strategy, gameIndex));
                }

                results[strategy.Record.Id] = Summarize(strategy.Record, perGame);
                Interlocked.Add(ref gamesPlayed, gamesPerStrategy);
            });

        started.Stop();
        return new ArenaResult
        {
            Label = _label,
            MasterSeed = _masterSeed,
            GamesPerStrategy = gamesPerStrategy,
            TargetScore = _targetScore,
            FieldDescription = string.Join(", ", _field.Select(entry => entry.Name)),
            Strategies = results.Values
                .OrderBy(metrics => metrics.Seat)
                .ThenByDescending(metrics => metrics.MeanScoreDifferential)
                .ThenBy(metrics => metrics.StrategyId, StringComparer.Ordinal)
                .ToArray(),
            ElapsedMilliseconds = started.Elapsed.TotalMilliseconds,
            GamesPlayed = gamesPlayed,
        };
    }

    private GameOutcome RunGame(CompiledStrategy strategy, int gameIndex)
    {
        var seat = new BonesPlayerId((gameIndex % BonesTableConfig.FixedPlayerCount) + BonesPlayerId.MinSeat);
        var slots = new Dictionary<BonesPlayerId, IBonesPlayerSlot>
        {
            [seat] = strategy.CreateSlot(),
        };

        var offset = (gameIndex * 3) + seat.Seat;
        var opponentIndex = 0;
        for (var index = 0; index < BonesTableConfig.FixedPlayerCount; index++)
        {
            var opponentSeat = new BonesPlayerId(index + BonesPlayerId.MinSeat);
            if (opponentSeat == seat)
                continue;

            var fieldIndex = (offset + opponentIndex) % _field.Count;
            opponentIndex++;
            slots[opponentSeat] = _field[fieldIndex].Create(BonesSeedDerivation.Derive(_masterSeed, gameIndex, opponentSeat.Seat));
        }

        var seed = BonesSeedDerivation.Derive(_masterSeed, gameIndex);
        var config = new BonesMatchConfig(
            new Wip.Bones.Identifiers.BonesGameId($"arena-{strategy.Record.Id}-{gameIndex}"),
            seed,
            _targetScore,
            slots);

        var result = new BonesMatchSimulator().RunMatch(config);
        var ownScore = result.CumulativeScores[seat];
        var bestOpponentScore = 0;
        foreach (var (playerId, score) in result.CumulativeScores)
        {
            if (playerId != seat && score > bestOpponentScore)
                bestOpponentScore = score;
        }

        return new GameOutcome(seat.Seat, result.Winner == seat, ownScore, bestOpponentScore, result.Rounds.Length);
    }

    private static StrategyArenaMetrics Summarize(StrategyRecord strategy, IReadOnlyList<GameOutcome> games)
    {
        var wins = games.Count(game => game.Won);
        var scores = games.Select(game => (double)game.OwnScore).ToArray();
        var bestOpponents = games.Select(game => (double)game.BestOpponentScore).ToArray();
        var differences = games.Select(game => (double)(game.OwnScore - game.BestOpponentScore)).ToArray();
        var winRate = wins / (double)games.Count;
        var winRateInterval = Stats.WilsonInterval(wins, games.Count);
        var scoreMean = scores.Average();
        var opponentMean = bestOpponents.Average();
        var differenceInterval = Stats.MeanConfidenceInterval95(differences);
        var (_, differenceStandardDeviation) = Stats.MeanAndStandardDeviation(differences);

        var bySeat = new Dictionary<int, double>();
        foreach (var group in games.GroupBy(game => game.Seat))
        {
            bySeat[group.Key] = group.Count(game => game.Won) / (double)group.Count();
        }

        return new StrategyArenaMetrics(
            strategy.Id,
            strategy.Seat,
            games.Count,
            wins,
            winRate,
            winRateInterval.Low,
            winRateInterval.High,
            scoreMean,
            opponentMean,
            differences.Average(),
            differenceInterval.Low,
            differenceInterval.High,
            differenceStandardDeviation,
            games.Average(game => game.Rounds),
            bySeat);
    }

    public sealed record CompiledStrategy(StrategyRecord Record, Type SlotType)
    {
        public IBonesPlayerSlot CreateSlot()
            => (IBonesPlayerSlot)(Activator.CreateInstance(SlotType)
                ?? throw new InvalidOperationException($"Unable to instantiate '{SlotType.FullName}'."));
    }

    private sealed record GameOutcome(int Seat, bool Won, int OwnScore, int BestOpponentScore, int Rounds);
}
