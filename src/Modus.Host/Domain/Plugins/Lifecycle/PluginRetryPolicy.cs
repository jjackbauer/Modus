namespace Modus.Host.Plugins.Lifecycle;

public sealed class PluginRetryPolicy
{
    public PluginRetryPolicy(int quarantineThreshold)
    {
        if (quarantineThreshold < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(quarantineThreshold), "Quarantine threshold must be greater than zero.");
        }

        QuarantineThreshold = quarantineThreshold;
    }

    public int QuarantineThreshold { get; }

    public bool ShouldQuarantine(int consecutiveFailureCount)
    {
        return consecutiveFailureCount >= QuarantineThreshold;
    }
}
