namespace Wip.Bones.Agents.Ponder;

public sealed class BonesDeepSeekPonderRetryBudget
{
    public int MaxRetries { get; }
    public int RetriesUsed { get; private set; }
    public bool IsExhausted => RetriesUsed >= MaxRetries;

    public BonesDeepSeekPonderRetryBudget(int maxRetries)
    {
        if (maxRetries < 1)
            throw new ArgumentOutOfRangeException(nameof(maxRetries), "MaxRetries must be at least 1.");

        MaxRetries = maxRetries;
        RetriesUsed = 0;
    }

    public BonesDeepSeekPonderRetryBudget Consume()
    {
        if (IsExhausted)
            throw new InvalidOperationException("Cannot consume retry budget when already exhausted.");

        RetriesUsed++;
        return this;
    }
}
