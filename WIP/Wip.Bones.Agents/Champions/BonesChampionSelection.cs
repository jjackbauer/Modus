namespace Wip.Bones.Agents.Champions;

public enum BonesChampionSelectionOutcome
{
    Promoted,
    Retained,
    Skipped,
}

public sealed record BonesChampionCandidateResult(
    string StrategyId,
    int Seat,
    int Games,
    int Wins,
    double MeanScoreDifferential,
    IReadOnlyDictionary<int, double> WinRateBySeat);

public sealed record BonesChampionSelection(
    BonesChampionSelectionOutcome Outcome,
    string? StrategyId,
    string? IncumbentId,
    double SelectionLcb,
    double IncumbentLcb,
    double ConfirmationLcb,
    double IncumbentConfirmationLcb,
    string Reason);

public static class BonesChampionSelector
{
    private const int SeatsPerMatch = 4;

    public static double WilsonLowerBound(int wins, int games, double z = 1.96)
    {
        if (games <= 0)
        {
            return 0.0;
        }

        var clampedWins = Math.Clamp(wins, 0, games);
        var n = (double)games;
        var proportion = clampedWins / n;
        var zSquared = z * z;
        var denominator = 1.0 + (zSquared / n);
        var center = proportion + (zSquared / (2.0 * n));
        var margin = z * Math.Sqrt(((proportion * (1.0 - proportion)) + (zSquared / (4.0 * n))) / n);
        var lowerBound = (center - margin) / denominator;

        return Math.Max(0.0, lowerBound);
    }

    public static BonesChampionSelection Select(
        IReadOnlyList<BonesChampionCandidateResult> selectionResults,
        IReadOnlyList<BonesChampionCandidateResult>? confirmationResults,
        string? incumbentId,
        int targetSeat,
        int minGames,
        double minLcbDelta = 0.0)
    {
        ArgumentNullException.ThrowIfNull(selectionResults);

        var eligible = selectionResults
            .Where(result => result.Games >= minGames)
            .ToList();

        if (eligible.Count == 0)
        {
            return new BonesChampionSelection(
                BonesChampionSelectionOutcome.Skipped,
                StrategyId: null,
                incumbentId,
                SelectionLcb: 0.0,
                IncumbentLcb: 0.0,
                ConfirmationLcb: 0.0,
                IncumbentConfirmationLcb: 0.0,
                Reason: "NoEligibleCandidates");
        }

        var ranked = eligible
            .OrderByDescending(result => WilsonLowerBound(result.Wins, result.Games))
            .ThenByDescending(result => TargetSeatLowerBound(result, targetSeat))
            .ThenByDescending(result => result.MeanScoreDifferential)
            .ThenBy(result => result.StrategyId, StringComparer.Ordinal)
            .ToList();

        var top = ranked[0];
        var topSelectionLcb = WilsonLowerBound(top.Wins, top.Games);

        if (string.IsNullOrEmpty(incumbentId))
        {
            return new BonesChampionSelection(
                BonesChampionSelectionOutcome.Promoted,
                top.StrategyId,
                IncumbentId: null,
                topSelectionLcb,
                IncumbentLcb: 0.0,
                ConfirmationLcb: 0.0,
                IncumbentConfirmationLcb: 0.0,
                Reason: "NoIncumbent");
        }

        if (string.Equals(top.StrategyId, incumbentId, StringComparison.Ordinal))
        {
            return new BonesChampionSelection(
                BonesChampionSelectionOutcome.Retained,
                incumbentId,
                incumbentId,
                topSelectionLcb,
                topSelectionLcb,
                ConfirmationLcb: 0.0,
                IncumbentConfirmationLcb: 0.0,
                Reason: "IncumbentStillBest");
        }

        var incumbent = FindByStrategyId(eligible, incumbentId);
        var incumbentSelectionLcb = incumbent is null
            ? 0.0
            : WilsonLowerBound(incumbent.Wins, incumbent.Games);

        if (topSelectionLcb < incumbentSelectionLcb + minLcbDelta)
        {
            return new BonesChampionSelection(
                BonesChampionSelectionOutcome.Retained,
                incumbentId,
                incumbentId,
                topSelectionLcb,
                incumbentSelectionLcb,
                ConfirmationLcb: 0.0,
                IncumbentConfirmationLcb: 0.0,
                Reason: "LcbBelowIncumbent");
        }

        var topConfirmationLcb = 0.0;
        var incumbentConfirmationLcb = 0.0;

        if (confirmationResults is not null)
        {
            var eligibleConfirmation = confirmationResults
                .Where(result => result.Games >= minGames)
                .ToList();
            var topConfirmation = FindByStrategyId(eligibleConfirmation, top.StrategyId);
            var incumbentConfirmation = FindByStrategyId(eligibleConfirmation, incumbentId);

            if (topConfirmation is null || incumbentConfirmation is null)
            {
                return new BonesChampionSelection(
                    BonesChampionSelectionOutcome.Retained,
                    incumbentId,
                    incumbentId,
                    topSelectionLcb,
                    incumbentSelectionLcb,
                    ConfirmationLcb: 0.0,
                    IncumbentConfirmationLcb: 0.0,
                    Reason: "ConfirmationMissing");
            }

            topConfirmationLcb = WilsonLowerBound(topConfirmation.Wins, topConfirmation.Games);
            incumbentConfirmationLcb = WilsonLowerBound(incumbentConfirmation.Wins, incumbentConfirmation.Games);

            if (topConfirmationLcb < incumbentConfirmationLcb + minLcbDelta)
            {
                return new BonesChampionSelection(
                    BonesChampionSelectionOutcome.Retained,
                    incumbentId,
                    incumbentId,
                    topSelectionLcb,
                    incumbentSelectionLcb,
                    topConfirmationLcb,
                    incumbentConfirmationLcb,
                    Reason: "ConfirmationFailed");
            }
        }

        return new BonesChampionSelection(
            BonesChampionSelectionOutcome.Promoted,
            top.StrategyId,
            incumbentId,
            topSelectionLcb,
            incumbentSelectionLcb,
            topConfirmationLcb,
            incumbentConfirmationLcb,
            Reason: "Promoted");
    }

    private static BonesChampionCandidateResult? FindByStrategyId(
        IReadOnlyList<BonesChampionCandidateResult> results,
        string strategyId)
        => results.FirstOrDefault(result => string.Equals(result.StrategyId, strategyId, StringComparison.Ordinal));

    private static double TargetSeatLowerBound(BonesChampionCandidateResult result, int targetSeat)
    {
        var seatGames = result.Games / SeatsPerMatch;

        if (seatGames <= 0)
        {
            return 0.0;
        }

        var seatWinRate = 0.0;

        if (result.WinRateBySeat is not null &&
            result.WinRateBySeat.TryGetValue(targetSeat, out var recordedRate))
        {
            seatWinRate = recordedRate;
        }

        var seatWins = (int)Math.Round(seatWinRate * seatGames, MidpointRounding.AwayFromZero);

        return WilsonLowerBound(seatWins, seatGames);
    }
}