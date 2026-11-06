namespace Wip.Bones.Agents.Configuration;

public sealed class BonesDeepSeekResilienceOptions
{
    public const string SectionName = "BonesHost:DeepSeekResilience";

    /// <summary>
    /// Maximum retry attempts for transient failures (429, 5xx).
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Base backoff duration in milliseconds for exponential retry.
    /// </summary>
    public int ExponentialBackoffBaseMs { get; set; } = 100;

    /// <summary>
    /// Number of consecutive failures within the rolling window that triggers the circuit breaker to open.
    /// </summary>
    public int CircuitBreakThreshold { get; set; } = 5;

    /// <summary>
    /// Duration in seconds the circuit stays open before transitioning to half-open.
    /// </summary>
    public int CircuitBreakDurationSeconds { get; set; } = 30;

    /// <summary>
    /// Maximum number of concurrent DeepSeek HTTP calls allowed through the bulkhead.
    /// </summary>
    public int MaxConcurrentCalls { get; set; } = 12;

    /// <summary>
    /// Maximum number of calls that can be queued when the bulkhead is at capacity.
    /// Calls beyond this limit fail fast with a bulkhead rejection.
    /// </summary>
    public int MaxQueueDepth { get; set; } = 24;

    /// <summary>
    /// When <c>ratelimit-remaining</c> drops to or below this threshold,
    /// the rate-limit handler preemptively throttles new outbound calls.
    /// </summary>
    public int PreemptiveThrottleThreshold { get; set; } = 5;
}