using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using Wip.Bones.Agents.Configuration;

namespace Wip.Bones.ModelProviders.DeepSeek;

/// <summary>
/// A <see cref="DelegatingHandler"/> that reads DeepSeek rate-limit response headers
/// and dynamically adjusts retry backoff / preemptively throttles outbound calls.
/// </summary>
public class RateLimitAwareDelegatingHandler : DelegatingHandler
{
    private static class HeaderNames
    {
        public const string RateLimitRemaining = "ratelimit-remaining";
        public const string RateLimitReset = "ratelimit-reset";
        public const string RetryAfter = "retry-after";
    }

    private readonly BonesDeepSeekResilienceOptions _options;
    private readonly RateLimitState _state = new();

    public RateLimitAwareDelegatingHandler(BonesDeepSeekResilienceOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Current snapshot of rate-limit state for health reporting.
    /// </summary>
    public RateLimitSnapshot GetSnapshot() => _state.Snapshot();

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        // Preemptive throttle: if remaining tokens are at or below threshold, delay.
        await _state.ThrottleIfNeededAsync(_options.PreemptiveThrottleThreshold, cancellationToken);

        var response = await base.SendAsync(request, cancellationToken);

        _state.UpdateFromResponse(response);

        return response;
    }

    internal sealed class RateLimitState
    {
        private readonly object _gate = new();

        private int _remaining = int.MaxValue;
        private DateTimeOffset _resetAt = DateTimeOffset.MinValue;
        private DateTimeOffset? _retryAfterUtc;

        public void UpdateFromResponse(HttpResponseMessage response)
        {
            var headers = response.Headers;

            lock (_gate)
            {
                if (headers.TryGetValues(HeaderNames.RateLimitRemaining, out var remainingValues)
                    && int.TryParse(remainingValues.FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var remaining))
                {
                    _remaining = remaining;
                }

                if (headers.TryGetValues(HeaderNames.RateLimitReset, out var resetValues)
                    && long.TryParse(resetValues.FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var resetEpoch))
                {
                    _resetAt = DateTimeOffset.FromUnixTimeSeconds(resetEpoch);
                }

                if (headers.TryGetValues(HeaderNames.RetryAfter, out var retryAfterValues))
                {
                    var raw = retryAfterValues.FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(raw))
                    {
                        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
                        {
                            _retryAfterUtc = DateTimeOffset.UtcNow.AddSeconds(seconds);
                        }
                        else if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
                        {
                            _retryAfterUtc = parsed;
                        }
                    }
                }
            }
        }

        public async Task ThrottleIfNeededAsync(int threshold, CancellationToken cancellationToken)
        {
            int remaining;
            DateTimeOffset resetAt;

            lock (_gate)
            {
                remaining = _remaining;
                resetAt = _resetAt;
            }

            if (remaining > threshold)
                return;

            // Compute a safe delay: wait until reset, with a floor of 100ms.
            var delay = resetAt - DateTimeOffset.UtcNow;
            if (delay <= TimeSpan.Zero)
                return;

            if (delay.TotalMilliseconds < 100)
                delay = TimeSpan.FromMilliseconds(100);

            await Task.Delay(delay, cancellationToken);
        }

        public RateLimitSnapshot Snapshot()
        {
            lock (_gate)
            {
                return new RateLimitSnapshot(
                    Remaining: _remaining == int.MaxValue ? null : _remaining,
                    ResetAt: _resetAt == DateTimeOffset.MinValue ? null : _resetAt,
                    RetryAfterUtc: _retryAfterUtc);
            }
        }
    }
}

public sealed record RateLimitSnapshot(
    int? Remaining,
    DateTimeOffset? ResetAt,
    DateTimeOffset? RetryAfterUtc);
