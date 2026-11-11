namespace Wip.Tholdem.Agents.Equity;

/// <summary>
/// One opponent seat at the moment of the call: the seat identifier, the chips that seat has
/// already committed to the pot and whether the seat has folded. Folded seats still contribute
/// their committed chips to the pot layers.
/// </summary>
public sealed record CallEvOpponent(int Seat, long TotalCommitted, bool Folded);

/// <summary>
/// Inputs to the layered call-EV calculation. <see cref="LayerEquities"/> supplies one showdown
/// equity per caller-eligible pot layer in ascending level order; the calculator never invents
/// equity. <see cref="FutureCost"/> is additional money the caller expects to put in later.
/// </summary>
public sealed record CallEvInput(
    long Pot,
    long OwnTotalCommitted,
    long OwnStackBehind,
    long AmountToCall,
    IReadOnlyList<CallEvOpponent> Opponents,
    IReadOnlyList<double> LayerEquities,
    long FutureCost = 0);

/// <summary>
/// One pot layer between the previous level and <see cref="Level"/>, with its amount, the live
/// opponent seats eligible to win it, whether the caller is eligible and the caller-supplied
/// showdown equity (zero when the caller is not eligible).
/// </summary>
public sealed record PotLayer(
    int Level,
    long Amount,
    IReadOnlyList<int> EligibleSeats,
    bool CallerEligible,
    double Equity);

/// <summary>
/// Result of the layered call-EV calculation: the effective call after returning chips above the
/// largest live commitment, the pot the caller can contest, call EV, fold EV (zero by definition)
/// and the break-even equity.
/// </summary>
public sealed record CallEvResult(
    long EffectiveCall,
    long ContestablePot,
    double CallEv,
    double FoldEv,
    double BreakEvenEquity,
    IReadOnlyList<PotLayer> Layers);

/// <summary>
/// Contribution- and side-pot-aware call EV. Pot layers are built from the distinct commitment
/// levels of the caller after the call and every opponent (folded seats included), caller-supplied
/// per-layer equities are applied only to the layers the caller is eligible to win, and the
/// break-even equity is (effective call + future cost) / contestable pot. The calculation uses no
/// clock, culture, GUID or ambient randomness, so the same inputs always produce the same result.
/// </summary>
public static class CallEvCalculator
{
    /// <summary>
    /// Computes the layered call EV for the supplied inputs. Returns the effective call (capped by
    /// the largest live commitment), the contestable pot, call EV, zero fold EV, the break-even
    /// equity clamped to [0,1] (zero when the contestable pot is zero) and one <see cref="PotLayer"/>
    /// per distinct positive commitment level in ascending order.
    /// </summary>
    public static CallEvResult Compute(CallEvInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (input.Pot < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(input), input.Pot, "The pot must be non-negative.");
        }

        if (input.OwnTotalCommitted < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(input), input.OwnTotalCommitted, "The own total committed must be non-negative.");
        }

        if (input.OwnStackBehind < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(input), input.OwnStackBehind, "The own stack behind must be non-negative.");
        }

        if (input.AmountToCall < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(input), input.AmountToCall, "The amount to call must be non-negative.");
        }

        if (input.FutureCost < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(input), input.FutureCost, "The future cost must be non-negative.");
        }

        if (input.AmountToCall > input.OwnStackBehind)
        {
            throw new ArgumentException(
                "The amount to call cannot exceed the stack behind.", nameof(input));
        }

        var opponents = input.Opponents
            ?? throw new ArgumentNullException(nameof(input), "The opponent list is required.");
        var layerEquities = input.LayerEquities
            ?? throw new ArgumentNullException(nameof(input), "The layer equity list is required.");

        var seats = new HashSet<int>();
        var liveOpponents = 0;
        var liveMax = 0L;
        foreach (var opponent in opponents)
        {
            if (opponent is null)
            {
                throw new ArgumentException(
                    "The opponent list cannot contain null entries.", nameof(input));
            }

            if (opponent.TotalCommitted < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(input),
                    opponent.TotalCommitted,
                    $"The total committed by seat {opponent.Seat} must be non-negative.");
            }

            if (!seats.Add(opponent.Seat))
            {
                throw new ArgumentException(
                    $"Duplicate opponent seat {opponent.Seat}.", nameof(input));
            }

            if (!opponent.Folded)
            {
                liveOpponents++;
                liveMax = Math.Max(liveMax, opponent.TotalCommitted);
            }
        }

        if (liveOpponents == 0)
        {
            throw new ArgumentException(
                "At least one live opponent is required.", nameof(input));
        }

        var effectiveCall = Math.Min(
            input.AmountToCall,
            Math.Max(0L, liveMax - input.OwnTotalCommitted));
        var ownAfter = input.OwnTotalCommitted + effectiveCall;

        var levels = new SortedSet<long>();
        if (ownAfter > 0)
        {
            levels.Add(ownAfter);
        }

        foreach (var opponent in opponents)
        {
            if (opponent.TotalCommitted > 0)
            {
                levels.Add(opponent.TotalCommitted);
            }
        }

        var rawLayers = new List<RawLayer>(levels.Count);
        var previousLevel = 0L;
        foreach (var level in levels)
        {
            var contributors = ownAfter >= level ? 1 : 0;
            foreach (var opponent in opponents)
            {
                if (opponent.TotalCommitted >= level)
                {
                    contributors++;
                }
            }

            var eligibleSeats = new List<int>();
            foreach (var opponent in opponents)
            {
                if (!opponent.Folded && opponent.TotalCommitted >= level)
                {
                    eligibleSeats.Add(opponent.Seat);
                }
            }

            eligibleSeats.Sort();
            rawLayers.Add(new RawLayer(
                level,
                (level - previousLevel) * contributors,
                eligibleSeats.ToArray(),
                ownAfter >= level));
            previousLevel = level;
        }

        var callerEligibleLayers = 0;
        foreach (var rawLayer in rawLayers)
        {
            if (rawLayer.CallerEligible)
            {
                callerEligibleLayers++;
            }
        }

        if (layerEquities.Count != callerEligibleLayers)
        {
            throw new ArgumentException(
                $"Exactly {callerEligibleLayers} layer equity value(s) are required; " +
                $"received {layerEquities.Count}.",
                nameof(input));
        }

        for (var index = 0; index < layerEquities.Count; index++)
        {
            var equity = layerEquities[index];
            if (!double.IsFinite(equity) || equity < 0.0 || equity > 1.0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(input),
                    equity,
                    $"The layer equity at index {index} must be finite and in [0,1].");
            }
        }

        var layers = new PotLayer[rawLayers.Count];
        var equityIndex = 0;
        var contestablePot = 0L;
        var callEv = 0.0;
        for (var index = 0; index < rawLayers.Count; index++)
        {
            var rawLayer = rawLayers[index];
            var equity = 0.0;
            if (rawLayer.CallerEligible)
            {
                equity = layerEquities[equityIndex];
                equityIndex++;
                contestablePot += rawLayer.Amount;
                callEv += equity * rawLayer.Amount;
            }

            layers[index] = new PotLayer(
                checked((int)rawLayer.Level),
                rawLayer.Amount,
                rawLayer.EligibleSeats,
                rawLayer.CallerEligible,
                equity);
        }

        callEv = callEv - effectiveCall - input.FutureCost;

        var breakEvenEquity = contestablePot == 0
            ? 0.0
            : Math.Clamp(
                (effectiveCall + (double)input.FutureCost) / contestablePot,
                0.0,
                1.0);

        return new CallEvResult(
            effectiveCall,
            contestablePot,
            callEv,
            0.0,
            breakEvenEquity,
            layers);
    }

    private readonly record struct RawLayer(
        long Level,
        long Amount,
        int[] EligibleSeats,
        bool CallerEligible);
}
