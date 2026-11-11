namespace Wip.Tholdem.Agents.Learning;

/// <summary>
/// Pot-relative bet and raise sizing: the target is the player's own street commitment plus the
/// amount to call plus a fraction of the pot after calling, rounded half away from zero and clamped
/// into the legal [MinRaiseTo, MaxRaiseTo] window. No clock, culture or ambient randomness is read.
/// </summary>
public static class PotRelativeSizing
{
    /// <summary>The largest accepted pot fraction.</summary>
    public const double MaximumPotFraction = 2.0;

    /// <summary>
    /// Returns the pot-relative target for a bet or raise. The fraction is applied to the pot after
    /// calling, and the existing street commitment and call are added before the round-half-away
    /// rounding. The result is clamped into the legal raise window.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Any amount is negative, the fraction is not in (0, 2], the minimum raise is not positive, or
    /// the maximum raise is below the minimum.
    /// </exception>
    public static long Target(
        long pot,
        long amountToCall,
        long ownStreetCommitment,
        double potFraction,
        long minRaiseTo,
        long maxRaiseTo)
    {
        if (pot < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pot), pot, "The pot must not be negative.");
        }

        if (amountToCall < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amountToCall),
                amountToCall,
                "The amount to call must not be negative.");
        }

        if (ownStreetCommitment < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ownStreetCommitment),
                ownStreetCommitment,
                "The own street commitment must not be negative.");
        }

        if (!(potFraction > 0.0 && potFraction <= MaximumPotFraction))
        {
            throw new ArgumentOutOfRangeException(
                nameof(potFraction),
                potFraction,
                "The pot fraction must be above zero and at most two.");
        }

        if (minRaiseTo <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minRaiseTo),
                minRaiseTo,
                "The minimum raise must be positive.");
        }

        if (maxRaiseTo < minRaiseTo)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxRaiseTo),
                maxRaiseTo,
                "The maximum raise must not be below the minimum raise.");
        }

        // The pot and the call are widened to double before they are added, so a legal long input
        // pair cannot overflow the intermediate sum.
        var raw = (double)ownStreetCommitment
            + amountToCall
            + (potFraction * ((double)pot + amountToCall));
        var rounded = (long)Math.Round(raw, MidpointRounding.AwayFromZero);
        return Math.Clamp(rounded, minRaiseTo, maxRaiseTo);
    }
}