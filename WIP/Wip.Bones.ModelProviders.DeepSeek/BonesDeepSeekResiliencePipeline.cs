using System.Net;
using System.Threading.RateLimiting;
using Polly;
using Polly.CircuitBreaker;
using Polly.RateLimiting;
using Polly.Retry;
using Polly.Timeout;
using Wip.Bones.Agents.Configuration;

namespace Wip.Bones.ModelProviders.DeepSeek;

/// <summary>
/// Wraps DeepSeek HTTP calls with a Polly resilience pipeline that combines
/// retry (with exponential backoff + jitter), circuit breaker, bulkhead isolation,
/// and a rate-limit-aware delegating handler.
/// </summary>
public sealed class BonesDeepSeekResiliencePipeline
{
    private readonly ResiliencePipeline _pipeline;
    private readonly CircuitBreakerStateProvider _circuitStateProvider;
    private readonly RateLimitAwareDelegatingHandler _rateLimitHandler;
    private readonly RollingFailureTracker _failureTracker;
    private readonly ConcurrencyLimiter _bulkheadLimiter;
    private readonly int _bulkheadMax;

    public BonesDeepSeekResiliencePipeline(
        BonesDeepSeekResilienceOptions options,
        RateLimitAwareDelegatingHandler rateLimitHandler)
    {
        ArgumentNullException.ThrowIfNull(options);
        _rateLimitHandler = rateLimitHandler ?? throw new ArgumentNullException(nameof(rateLimitHandler));
        _failureTracker = new RollingFailureTracker(options.CircuitBreakThreshold);
        _bulkheadMax = options.MaxConcurrentCalls;

        _circuitStateProvider = new CircuitBreakerStateProvider();
        _bulkheadLimiter = new ConcurrencyLimiter(new ConcurrencyLimiterOptions
        {
            PermitLimit = options.MaxConcurrentCalls,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = options.MaxQueueDepth,
        });

        _pipeline = BuildPipeline(options);
    }

    public ResiliencePipeline Pipeline => _pipeline;

    public RateLimitAwareDelegatingHandler RateLimitHandler => _rateLimitHandler;

    public string CircuitState => _circuitStateProvider.CircuitState switch
    {
        Polly.CircuitBreaker.CircuitState.Open => "open",
        Polly.CircuitBreaker.CircuitState.HalfOpen => "half-open",
        _ => "closed",
    };

    public int BulkheadActive
    {
        get
        {
            var stats = _bulkheadLimiter.GetStatistics();
            if (stats is null)
                return 0;

            var available = (int)stats.CurrentAvailablePermits;
            return _bulkheadMax - available;
        }
    }

    /// <summary>
    /// The maximum number of concurrent calls configured in the bulkhead.
    /// </summary>
    public int BulkheadMax => _bulkheadMax;

    public int? RateLimitRemaining => _rateLimitHandler.GetSnapshot().Remaining;

    public DateTimeOffset? RateLimitReset => _rateLimitHandler.GetSnapshot().ResetAt;

    public double RollingFailureRate => _failureTracker.FailureRate;

    private ResiliencePipeline BuildPipeline(BonesDeepSeekResilienceOptions options)
    {
        var concurrencyLimiterOptions = new ConcurrencyLimiterOptions
        {
            PermitLimit = options.MaxConcurrentCalls,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = options.MaxQueueDepth,
        };

        var builder = new ResiliencePipelineBuilder()
            .AddRateLimiter(new RateLimiterStrategyOptions
            {
                DefaultRateLimiterOptions = concurrencyLimiterOptions,
                OnRejected = args =>
                {
                    throw new InvalidOperationException(
                        "DeepSeek bulkhead rejected call: all slots busy and queue full.");
                },
            });

        if (options.MaxRetries > 0)
        {
            builder = builder.AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder()
                    .Handle<HttpRequestException>()
                    .Handle<TimeoutRejectedException>()
                    .HandleResult(response =>
                    {
                        if (response is HttpResponseMessage httpResponse)
                            return httpResponse.StatusCode == HttpStatusCode.TooManyRequests
                                || (int)httpResponse.StatusCode >= 500;
                        return false;
                    }),

                MaxRetryAttempts = options.MaxRetries,
                BackoffType = DelayBackoffType.Exponential,
                Delay = TimeSpan.FromMilliseconds(options.ExponentialBackoffBaseMs),
                MaxDelay = TimeSpan.FromSeconds(30),
                UseJitter = true,

                OnRetry = args =>
                {
                    if (args.Outcome.Exception is not null)
                    {
                        _failureTracker.RecordFailure();
                    }
                    else if (args.Outcome.Result is HttpResponseMessage httpResponse
                        && ((int)httpResponse.StatusCode >= 500
                            || httpResponse.StatusCode == HttpStatusCode.TooManyRequests))
                    {
                        _failureTracker.RecordFailure();
                    }

                    return ValueTask.CompletedTask;
                },
            });
        }

        return builder
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                ShouldHandle = new PredicateBuilder()
                    .Handle<HttpRequestException>()
                    .Handle<TimeoutRejectedException>()
                    .HandleResult(response =>
                    {
                        if (response is HttpResponseMessage httpResponse)
                            return (int)httpResponse.StatusCode >= 500
                                || httpResponse.StatusCode == HttpStatusCode.TooManyRequests;
                        return false;
                    }),

                FailureRatio = 1.0,
                MinimumThroughput = options.CircuitBreakThreshold,
                SamplingDuration = TimeSpan.FromSeconds(options.CircuitBreakDurationSeconds * 2),
                BreakDuration = TimeSpan.FromSeconds(options.CircuitBreakDurationSeconds),
                StateProvider = _circuitStateProvider,
            })
            .Build();
    }

    /// <summary>
    /// Tracks a rolling failure count for health reporting.
    /// </summary>
    internal sealed class RollingFailureTracker
    {
        private readonly int _windowSize;
        private readonly bool[] _window;
        private int _index;

        public RollingFailureTracker(int windowSize)
        {
            _windowSize = Math.Max(1, windowSize);
            _window = new bool[_windowSize];
        }

        public void RecordFailure()
        {
            var idx = Interlocked.Increment(ref _index) % _windowSize;
            if (idx < 0) idx += _windowSize;
            _window[idx] = true;
        }

        public void RecordSuccess()
        {
            var idx = Interlocked.Increment(ref _index) % _windowSize;
            if (idx < 0) idx += _windowSize;
            _window[idx] = false;
        }

        public double FailureRate
        {
            get
            {
                var failures = 0;
                for (var i = 0; i < _windowSize; i++)
                {
                    if (_window[i]) failures++;
                }

                return _windowSize == 0 ? 0.0 : (double)failures / _windowSize;
            }
        }
    }
}
