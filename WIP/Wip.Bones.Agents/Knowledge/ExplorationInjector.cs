using Microsoft.Extensions.Logging;
using Wip.Bones.Domain;
using Wip.Bones.Identifiers;

namespace Wip.Bones.Agents.Knowledge;

/// <summary>
/// Proactively injects diversity into the strategy pool when stagnation is detected.
/// Runs at configurable intervals to check pool health and add exploration strategies
/// or targeted challengers when the elite strategy's win rate plateaus.
/// </summary>
public sealed class ExplorationInjector
{
    /// <summary>
    /// Minimum number of distinct strategy sources required in the pool.
    /// Below this threshold, a new exploration strategy is injected.
    /// </summary>
    public const int MinDistinctArchitectures = 8;

    /// <summary>
    /// Number of evaluation cycles without elite improvement before a
    /// targeted challenger is injected.
    /// </summary>
    public const int StagnationWindow = 5;

    private readonly ILogger<ExplorationInjector>? _logger;
    private int _cyclesSinceEliteImproved;

    public ExplorationInjector(ILogger<ExplorationInjector>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Checks pool diversity and injects new strategies if thresholds are not met.
    /// Does nothing if the pool is already sufficiently diverse and not stagnant.
    /// </summary>
    /// <param name="pool">The strategy pool to inject into.</param>
    /// <param name="eliteWinRateChanged">
    /// Whether the elite strategy's win rate has improved since the last evaluation cycle.
    /// </param>
    /// <returns>The number of new strategies injected.</returns>
    public int MaybeInject(StrategyPool pool, bool eliteWinRateChanged)
    {
        ArgumentNullException.ThrowIfNull(pool);

        if (pool.Count == 0)
            return 0;

        var injected = 0;

        // Check diversity: count distinct source texts (simple length-based heuristic).
        var sources = pool.Pool.Values.Select(e => e.Source).Distinct().Count();
        if (sources < MinDistinctArchitectures)
        {
            var id = new BonesStrategyId($"explore-inject-{Guid.NewGuid():N}");
            pool.Add(new StrategyEntry(
                id,
                (sources % 4) + 1,
                BonesStrategyKind.Script,
                GenerateExplorationSource(sources),
                0, 0, 0, 0,
                1400.0)
            { IsElite = false });

            _logger?.LogInformation(
                "ExplorationInjector: Injected diversity strategy {StrategyId} (sources: {SourceCount})",
                id.Value,
                sources + 1);

            injected++;
        }

        // Check stagnation: if elite hasn't improved, inject a targeted challenger.
        if (eliteWinRateChanged)
        {
            _cyclesSinceEliteImproved = 0;
        }
        else
        {
            _cyclesSinceEliteImproved++;
            if (_cyclesSinceEliteImproved >= StagnationWindow && pool.EliteStrategyId is not null)
            {
                var eliteEntry = pool.Pool.TryGetValue(pool.EliteStrategyId.Value, out var found) ? found : null;
                if (eliteEntry is not null)
                {
                    var challengerId = new BonesStrategyId(
                        $"{eliteEntry.Id.Value}-challenger-{Guid.NewGuid():N}");
                    var challengerSource = eliteEntry.Source +
                        "\n// Targeted challenger: aggressive parameter adjustment\n";

                    pool.Add(new StrategyEntry(
                        challengerId,
                        eliteEntry.Seat,
                        eliteEntry.Kind,
                        challengerSource,
                        0, 0, 0, 0,
                        eliteEntry.Rating - 30)
                    { IsElite = false });

                    _logger?.LogInformation(
                        "ExplorationInjector: Injected targeted challenger {ChallengerId} " +
                        "for elite {EliteId} after {StagnationCycles} stagnant cycles",
                        challengerId.Value,
                        pool.EliteStrategyId.Value,
                        _cyclesSinceEliteImproved);

                    _cyclesSinceEliteImproved = 0;
                    injected++;
                }
            }
        }

        return injected;
    }

    private static string GenerateExplorationSource(int variant)
    {
        return (variant % 5) switch
        {
            0 => @"using Wip.Bones.Domain;
using Wip.Bones.Engine;

public sealed class SeatStrategy : IBonesPlayerSlot
{
    public BonesMove ChooseMove(BonesRoundState state, IReadOnlyList<BonesMove> legalMoves)
    {
        // Injected: prefer highest pip tile
        BonesMove? best = null;
        int bestPips = int.MinValue;
        foreach (var move in legalMoves)
        {
            if (move.IsPass) continue;
            if (move.Tile!.Value.TotalPips > bestPips) { best = move; bestPips = move.Tile!.Value.TotalPips; }
        }
        return best ?? legalMoves[0];
    }
}",
        1 => @"using Wip.Bones.Domain;
using Wip.Bones.Engine;

public sealed class SeatStrategy : IBonesPlayerSlot
{
    public BonesMove ChooseMove(BonesRoundState state, IReadOnlyList<BonesMove> legalMoves)
    {
        // Injected: prefer lowest pip tile
        BonesMove? best = null;
        int bestPips = int.MaxValue;
        foreach (var move in legalMoves)
        {
            if (move.IsPass) continue;
            if (move.Tile!.Value.TotalPips < bestPips) { best = move; bestPips = move.Tile!.Value.TotalPips; }
        }
        return best ?? legalMoves[0];
    }
}",
        _ => @"using Wip.Bones.Domain;
using Wip.Bones.Engine;

public sealed class SeatStrategy : IBonesPlayerSlot
{
    public BonesMove ChooseMove(BonesRoundState state, IReadOnlyList<BonesMove> legalMoves)
    {
        // Injected: prefer doubles, then highest pip
        foreach (var move in legalMoves)
        {
            if (move.IsPass) continue;
            if (move.Tile!.Value.IsDouble) return move;
        }
        BonesMove? best = null;
        int bestPips = int.MinValue;
        foreach (var move in legalMoves)
        {
            if (move.IsPass) continue;
            if (move.Tile!.Value.TotalPips > bestPips) { best = move; bestPips = move.Tile!.Value.TotalPips; }
        }
        return best ?? legalMoves[0];
    }
}",
    };
}
}
