using Wip.Tholdem.Hands;
using Wip.Tholdem.History;

namespace Wip.Tholdem.Agents.Learning.Refinement;

/// <summary>
/// Reduces recorded hands to the bounded, deterministic <see cref="SessionSignatures"/> value. It
/// emits at most twelve named signals with counts, samples, rates and fixed-vocabulary directions.
/// </summary>
/// <remarks>
/// Every signal is binary at the hand level: <c>Sample</c> counts the hands in which the failure was
/// possible and <c>Count</c> the hands in which it happened, so a rate is always in [0, 1]. Hands are
/// visited in hand-number order so the result does not depend on the caller's list order. Seat 0 is
/// the measured strategy, matching the training and evaluation match layout.
/// </remarks>
public static class FailureSignatureExtractor
{
    private static readonly HandCategory MinimumMissedValueCategory = HandCategory.TwoPair;

    public static SessionSignatures Extract(IReadOnlyList<HandRecord> hands)
    {
        ArgumentNullException.ThrowIfNull(hands);

        var foldToRaiseCount = 0;
        var foldToRaiseSample = 0;
        var weakCallCount = 0;
        var weakCallSample = 0;
        var missedValueCount = 0;
        var missedValueSample = 0;
        var postflopAggressiveActions = 0;
        var postflopActions = 0;
        var showdownLossCount = 0;
        var showdownSample = 0;
        var stackOffLossCount = 0;
        var stackOffSample = 0;

        foreach (var hand in hands.OrderBy(static record => record.HandNumber))
        {
            var seatZeroWon = hand.Winners.Contains(0);

            if (FacedRaise(hand, out var foldedAfterRaise))
            {
                foldToRaiseSample++;
                if (foldedAfterRaise)
                {
                    foldToRaiseCount++;
                }
            }

            var category = SeatZeroCategory(hand);
            var called = hand.Actions.Any(
                static action => action.Seat == 0 && string.Equals(action.Kind, "Call", StringComparison.Ordinal));

            if (category is not null && called)
            {
                weakCallSample++;
                if (category is HandCategory.HighCard or HandCategory.OnePair && !seatZeroWon)
                {
                    weakCallCount++;
                }
            }

            if (category is not null && (int)category.Value >= (int)MinimumMissedValueCategory)
            {
                missedValueSample++;
                if (!HasPostflopAggression(hand))
                {
                    missedValueCount++;
                }
            }

            foreach (var action in hand.Actions)
            {
                if (action.Seat != 0 || action.Street < 1)
                {
                    continue;
                }

                postflopActions++;
                if (IsAggressive(action.Kind))
                {
                    postflopAggressiveActions++;
                }
            }

            if (hand.Showdown)
            {
                showdownSample++;
                if (!seatZeroWon)
                {
                    showdownLossCount++;
                }
            }

            var wentAllIn = hand.Actions.Any(
                static action => action.Seat == 0 && string.Equals(action.Kind, "AllIn", StringComparison.Ordinal));
            var busted = hand.FinalStacks.Count > 0 && hand.FinalStacks[0] == 0;
            if (wentAllIn || busted)
            {
                stackOffSample++;
                if (busted && !seatZeroWon)
                {
                    stackOffLossCount++;
                }
            }
        }

        var foldToRaiseRate = Rate(foldToRaiseCount, foldToRaiseSample);
        var weakCallRate = Rate(weakCallCount, weakCallSample);
        var missedValueRate = Rate(missedValueCount, missedValueSample);
        var aggressionRate = Rate(postflopAggressiveActions, postflopActions);
        var showdownLossRate = Rate(showdownLossCount, showdownSample);
        var stackOffLossRate = Rate(stackOffLossCount, stackOffSample);

        return new SessionSignatures(
        [
            new FailureSignal(
                FailureSignalNames.FoldToRaise,
                foldToRaiseCount,
                foldToRaiseSample,
                foldToRaiseRate,
                ThresholdDirection(foldToRaiseRate)),
            new FailureSignal(
                FailureSignalNames.CallWithWeakHand,
                weakCallCount,
                weakCallSample,
                weakCallRate,
                ThresholdDirection(weakCallRate)),
            new FailureSignal(
                FailureSignalNames.MissedValue,
                missedValueCount,
                missedValueSample,
                missedValueRate,
                ThresholdDirection(missedValueRate)),
            new FailureSignal(
                FailureSignalNames.AggressionByStreet,
                postflopAggressiveActions,
                postflopActions,
                aggressionRate,
                AggressionDirection(aggressionRate)),
            new FailureSignal(
                FailureSignalNames.ShowdownLoss,
                showdownLossCount,
                showdownSample,
                showdownLossRate,
                ThresholdDirection(showdownLossRate)),
            new FailureSignal(
                FailureSignalNames.StackOffLoss,
                stackOffLossCount,
                stackOffSample,
                stackOffLossRate,
                ThresholdDirection(stackOffLossRate)),
        ]);
    }

    /// <summary>
    /// True when seat 0 answered a same-street opponent aggression. <paramref name="folded"/> is true
    /// when that answer was a fold, which is the exact fold-to-raise condition.
    /// </summary>
    private static bool FacedRaise(HandRecord hand, out bool folded)
    {
        folded = false;
        var faced = false;
        for (var index = 1; index < hand.Actions.Count; index++)
        {
            var action = hand.Actions[index];
            var previous = hand.Actions[index - 1];
            if (action.Seat != 0
                || previous.Seat == 0
                || action.Street != previous.Street
                || !IsAggressive(previous.Kind))
            {
                continue;
            }

            faced = true;
            if (string.Equals(action.Kind, "Fold", StringComparison.Ordinal))
            {
                folded = true;
            }
        }

        return faced;
    }

    private static bool HasPostflopAggression(HandRecord hand) =>
        hand.Actions.Any(
            static action => action.Seat == 0 && action.Street >= 1 && IsAggressive(action.Kind));

    /// <summary>
    /// The seat-0 final hand category, or <see langword="null"/> when the board is too short to make
    /// the category meaningful. The evaluator is only invoked for a complete five-card board.
    /// </summary>
    private static HandCategory? SeatZeroCategory(HandRecord hand)
    {
        if (hand.Board.Count < 5 || hand.HoleCards.Count == 0)
        {
            return null;
        }

        var cards = hand.HoleCards[0].Concat(hand.Board).ToArray();
        return HandEvaluator.Evaluate(cards).Category;
    }

    private static bool IsAggressive(string kind) =>
        string.Equals(kind, "Bet", StringComparison.Ordinal)
        || string.Equals(kind, "Raise", StringComparison.Ordinal)
        || string.Equals(kind, "AllIn", StringComparison.Ordinal);

    private static double Rate(int count, int sample) => sample == 0 ? 0.0 : count / (double)sample;

    private static string ThresholdDirection(double rate) =>
        rate >= 0.5 ? FailureSignalDirections.High : FailureSignalDirections.Low;

    private static string AggressionDirection(double rate) =>
        rate > 0.5
            ? FailureSignalDirections.High
            : rate < 0.5
                ? FailureSignalDirections.Low
                : FailureSignalDirections.Neutral;
}
