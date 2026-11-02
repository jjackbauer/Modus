namespace Wip.Bones.Host;

/// <summary>
/// Worst-case planning estimate for one learning iteration. Used at startup to make the
/// cost of the current configuration visible before any paid model call is made.
/// This is an estimate, not a budget: the authoritative bound is
/// <see cref="BonesHostOptions.MaxGamesPerRun"/> enforced by the game budget ledger.
/// </summary>
public sealed record BonesStartupPlan(
    int ParallelSessions,
    int LearningSeats,
    int ObserveGamesPerSession,
    int PlayGamesPerSession,
    int EvaluationGamesPerSession,
    int CrossSeatGamesPerSession,
    int PlannedGamesPerIteration,
    int PlannedModelCallsPerIteration)
{
    public string? CapWarning(int maxGamesPerRun)
    {
        if (maxGamesPerRun <= 0)
            return "UNBOUNDED: MaxGamesPerRun=0 disables the hard cap. Set MaxGamesPerRun or BONES_MAX_GAMES_PER_RUN before a paid run.";

        if (PlannedGamesPerIteration > maxGamesPerRun)
            return $"PLAN_EXCEEDS_CAP: one iteration plans ~{PlannedGamesPerIteration} games but MaxGamesPerRun={maxGamesPerRun}; the run will stop inside its first iteration.";

        return null;
    }
}

public static class BonesStartupPlanner
{
    public static void ValidateOrThrow(BonesHostOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.MaxGamesPerRun <= 0 && !options.AllowUnbounded)
        {
            throw new InvalidOperationException(
                $"Bones host configuration is invalid: MaxGamesPerRun={options.MaxGamesPerRun} disables the per-run game cap. "
                + "Set MaxGamesPerRun to a positive integer, or set BonesHost:AllowUnbounded=true "
                + "(BONES_ALLOW_UNBOUNDED=true or BonesHost__AllowUnbounded=true) to explicitly opt in to an unbounded run.");
        }
    }

    public static BonesStartupPlan Compute(BonesHostOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var seats = options.AllSeatsLearning ? 4 : 1;
        var observeGames = options.DefaultParameters.GameCount;
        const int playGames = 1;
        var evaluationGames = options.Promotion.EvaluationMatchCount * 2 * seats;
        var crossSeatGames = options.Promotion.MinimumCrossSeatCount > 1
            ? Math.Max(1, options.Promotion.EvaluationMatchCount / 4) * 3
            : 0;

        var perSession = observeGames + playGames + evaluationGames + crossSeatGames;
        var planned = perSession * options.ParallelSessionCount;

        // Coarse model-call estimate: one ponder per seat that needs it, one enhance per
        // seat, and one play fallback. Markdown play turns can add more calls; this is a
        // floor, not a ceiling.
        var modelCalls = (seats * 2) + 1;

        return new BonesStartupPlan(
            options.ParallelSessionCount,
            seats,
            observeGames,
            playGames,
            evaluationGames,
            crossSeatGames,
            planned,
            modelCalls * options.ParallelSessionCount);
    }

    public static IReadOnlyList<string> Describe(BonesHostOptions options)
    {
        var plan = Compute(options);
        var lines = new List<string>
        {
            $"plan.parallelSessions={plan.ParallelSessions}",
            $"plan.learningSeats={plan.LearningSeats}",
            $"plan.gamesPerIteration={plan.PlannedGamesPerIteration}",
            $"plan.modelCallsPerIteration>={plan.PlannedModelCallsPerIteration}",
        };

        var warning = plan.CapWarning(options.MaxGamesPerRun);
        if (warning is not null)
            lines.Add($"plan.warning={warning}");

        return lines;
    }
}
