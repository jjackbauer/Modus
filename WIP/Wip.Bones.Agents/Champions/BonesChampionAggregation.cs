using System;
using System.Collections.Generic;
using System.Linq;

namespace Wip.Bones.Agents.Champions;

public static class BonesChampionAggregator
{
    public static IReadOnlyList<BonesChampionCandidateResult> Aggregate(
        IEnumerable<BonesChampionCandidateResult> samples,
        int targetSeat)
    {
        ArgumentNullException.ThrowIfNull(samples);

        var results = samples
            .GroupBy(sample => sample.StrategyId, StringComparer.Ordinal)
            .Select(group =>
            {
                var materialized = group.ToArray();
                var games = materialized.Sum(sample => sample.Games);
                var wins = materialized.Sum(sample => sample.Wins);
                var meanScoreDifferential = games == 0
                    ? 0.0
                    : materialized.Sum(sample => sample.MeanScoreDifferential * sample.Games) / games;

                var seatRates = new Dictionary<int, double>();
                foreach (var seat in materialized.SelectMany(sample => sample.WinRateBySeat.Keys).Distinct())
                {
                    var weighted = materialized.Sum(sample =>
                        sample.WinRateBySeat.TryGetValue(seat, out var rate) ? rate * sample.Games : 0.0);
                    seatRates[seat] = games == 0 ? 0.0 : weighted / games;
                }

                return new BonesChampionCandidateResult(
                    group.Key,
                    targetSeat,
                    games,
                    wins,
                    meanScoreDifferential,
                    seatRates);
            })
            .OrderBy(result => result.StrategyId, StringComparer.Ordinal)
            .ToArray();

        return results;
    }
}