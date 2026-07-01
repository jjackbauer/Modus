namespace Wip.Bones.Agents.Budget;

public sealed class BonesAdaptiveEvaluationBudgetOptions
{
    public int BatchSize { get; set; } = 5;

    public int FullEvaluationMatchCount { get; set; } = 20;

    public int EarlyStopCheckpoint { get; set; } = 10;

    public double ClearWinnerThreshold { get; set; } = 0.90;

    public double ClearLoserThreshold { get; set; } = 0.10;

    public void Validate()
    {
        if (BatchSize < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(BatchSize),
                BatchSize,
                "BatchSize must be at least 1.");
        }

        if (FullEvaluationMatchCount < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(FullEvaluationMatchCount),
                FullEvaluationMatchCount,
                "FullEvaluationMatchCount must be at least 1.");
        }

        if (EarlyStopCheckpoint < 1 || EarlyStopCheckpoint > FullEvaluationMatchCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(EarlyStopCheckpoint),
                EarlyStopCheckpoint,
                "EarlyStopCheckpoint must be between 1 and FullEvaluationMatchCount.");
        }

        if (ClearWinnerThreshold is < 0.0 or > 1.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ClearWinnerThreshold),
                ClearWinnerThreshold,
                "ClearWinnerThreshold must be between 0.0 and 1.0.");
        }

        if (ClearLoserThreshold is < 0.0 or > 1.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ClearLoserThreshold),
                ClearLoserThreshold,
                "ClearLoserThreshold must be between 0.0 and 1.0.");
        }

        if (ClearLoserThreshold >= ClearWinnerThreshold)
        {
            throw new ArgumentException(
                "ClearLoserThreshold must be less than ClearWinnerThreshold.",
                nameof(ClearLoserThreshold));
        }
    }
}

public enum BonesAdaptiveDecision
{
    Continue,
    StopEarlyPromote,
    StopEarlyReject,
}

public sealed record BonesAdaptiveBudgetState(
    int MatchesCompleted,
    int CandidateWins,
    BonesAdaptiveDecision Decision);

public sealed class BonesAdaptiveEvaluationBudget
{
    private readonly BonesAdaptiveEvaluationBudgetOptions _options;

    public BonesAdaptiveEvaluationBudget(BonesAdaptiveEvaluationBudgetOptions? options = null)
    {
        _options = options ?? new BonesAdaptiveEvaluationBudgetOptions();
        _options.Validate();
    }

    public BonesAdaptiveEvaluationBudgetOptions Options => new()
    {
        BatchSize = _options.BatchSize,
        FullEvaluationMatchCount = _options.FullEvaluationMatchCount,
        EarlyStopCheckpoint = _options.EarlyStopCheckpoint,
        ClearWinnerThreshold = _options.ClearWinnerThreshold,
        ClearLoserThreshold = _options.ClearLoserThreshold,
    };

    public int DetermineMatchCount(int matchesCompleted, int candidateWins)
    {
        if (matchesCompleted < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(matchesCompleted),
                matchesCompleted,
                "MatchesCompleted cannot be negative.");
        }

        if (candidateWins < 0 || candidateWins > matchesCompleted)
        {
            throw new ArgumentOutOfRangeException(
                nameof(candidateWins),
                candidateWins,
                $"CandidateWins must be between 0 and {matchesCompleted}.");
        }

        var remaining = _options.FullEvaluationMatchCount - matchesCompleted;
        if (remaining <= 0)
        {
            return 0;
        }

        // Only check for early termination at or after the early stop checkpoint.
        if (matchesCompleted < _options.EarlyStopCheckpoint)
        {
            var nextBatch = Math.Min(_options.BatchSize, remaining);
            var nextCheckpoint = _options.EarlyStopCheckpoint;

            // Ensure we don't skip the checkpoint: if next batch would overshoot
            // the checkpoint, run only enough matches to reach it.
            if (matchesCompleted + nextBatch > nextCheckpoint)
            {
                return nextCheckpoint - matchesCompleted;
            }

            return nextBatch;
        }

        // At or past the early stop checkpoint: evaluate win rate.
        var winRate = (double)candidateWins / matchesCompleted;

        if (winRate >= _options.ClearWinnerThreshold)
        {
            return 0; // Stop early — candidate is clearly better.
        }

        if (winRate <= _options.ClearLoserThreshold)
        {
            return 0; // Stop early — candidate is clearly worse.
        }

        // Borderline: continue to the full budget.
        return remaining;
    }

    public BonesAdaptiveBudgetState GetState(int matchesCompleted, int candidateWins)
    {
        var next = DetermineMatchCount(matchesCompleted, candidateWins);
        if (next <= 0 && matchesCompleted >= _options.EarlyStopCheckpoint)
        {
            var winRate = (double)candidateWins / matchesCompleted;
            if (winRate >= _options.ClearWinnerThreshold)
            {
                return new BonesAdaptiveBudgetState(
                    matchesCompleted,
                    candidateWins,
                    BonesAdaptiveDecision.StopEarlyPromote);
            }

            if (winRate <= _options.ClearLoserThreshold)
            {
                return new BonesAdaptiveBudgetState(
                    matchesCompleted,
                    candidateWins,
                    BonesAdaptiveDecision.StopEarlyReject);
            }
        }

        return new BonesAdaptiveBudgetState(
            matchesCompleted,
            candidateWins,
            BonesAdaptiveDecision.Continue);
    }
}
