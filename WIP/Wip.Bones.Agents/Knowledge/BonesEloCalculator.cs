namespace Wip.Bones.Agents.Knowledge;

/// <summary>
/// Standard 4-player ELO rating calculator.
/// For each player, the expected score is computed against every opponent,
/// summed, and divided by (N-1) where N is the total number of players.
/// </summary>
public static class BonesEloCalculator
{
    /// <summary>
    /// Default K-factor used for ELO updates (standard chess K-factor for intermediate players).
    /// </summary>
    public const double DefaultKFactor = 32.0;

    /// <summary>
    /// Default starting ELO rating.
    /// </summary>
    public const double DefaultRating = 1500.0;

    /// <summary>
    /// The standard ELO scale factor. A 400-point difference means a ~10:1 win probability.
    /// </summary>
    private const double EloScale = 400.0;

    /// <summary>
    /// Computes the expected score for a player with <paramref name="myRating"/>
    /// against a set of <paramref name="opponentRatings"/>.
    /// For 4-player games this is: sum(expected_vs_each_opponent) / (N - 1).
    /// </summary>
    public static double CalculateExpectedScore(double myRating, IReadOnlyList<double> opponentRatings)
    {
        if (opponentRatings.Count == 0)
            return 0.5; // No opponents → neutral expectation

        var totalExpected = 0.0;
        foreach (var opponentRating in opponentRatings)
        {
            totalExpected += ExpectedVsOne(myRating, opponentRating);
        }

        return totalExpected / opponentRatings.Count;
    }

    /// <summary>
    /// Computes a new ELO rating from the old rating, expected score, and actual score.
    /// </summary>
    /// <param name="oldRating">The player's current rating.</param>
    /// <param name="expectedScore">Expected score (0.0 to 1.0).</param>
    /// <param name="actualScore">Actual score (1.0 for win, 0.0 for loss).</param>
    /// <param name="kFactor">The K-factor controlling rating volatility. Default 32.</param>
    /// <returns>The new rating, clamped to a minimum of 100.0.</returns>
    public static double CalculateNewRating(
        double oldRating,
        double expectedScore,
        double actualScore,
        double kFactor = DefaultKFactor)
    {
        var newRating = oldRating + kFactor * (actualScore - expectedScore);
        return Math.Max(100.0, newRating);
    }

    /// <summary>
    /// Expected score in a 1-vs-1 match using the standard ELO formula:
    /// E = 1 / (1 + 10^((opponent_rating - my_rating) / 400))
    /// </summary>
    private static double ExpectedVsOne(double myRating, double opponentRating)
    {
        return 1.0 / (1.0 + Math.Pow(10.0, (opponentRating - myRating) / EloScale));
    }
}
