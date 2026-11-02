namespace Wip.Bones.Host;

public sealed record BonesSeatPromotionOutcome(
    int Seat,
    string? Outcome,
    DateTimeOffset TimestampUtc = default);

public sealed record BonesPerSeatPromotionCounts(int Promoted, int Rejected);

public sealed class BonesCoLearningMetrics
{
    private readonly object _gate = new();
    private long _coLearningIterationsCompleted;
    private IReadOnlyList<BonesSeatPromotionOutcome> _lastPromotionOutcomes = Array.Empty<BonesSeatPromotionOutcome>();

    // Cumulative promotion/rejection counters across all iterations.
    private long _cumulativeStrategiesPromoted;
    private long _cumulativeStrategiesRejected;

    // Cumulative per-seat promotion/rejection counters.
    private readonly Dictionary<int, BonesPerSeatPromotionCounts> _perSeatPromotionCounts = new();

    // Throughput metrics.
    private long _totalGamesCompleted;
    private long _totalGameDurationMs;
    private DateTimeOffset _firstGameStartedAt = DateTimeOffset.MinValue;

    public long CoLearningIterationsCompleted
    {
        get { lock (_gate) return _coLearningIterationsCompleted; }
        internal set { lock (_gate) _coLearningIterationsCompleted = value; }
    }

    public IReadOnlyList<BonesSeatPromotionOutcome> LastPromotionOutcomes
    {
        get { lock (_gate) return _lastPromotionOutcomes; }
        internal set { lock (_gate) _lastPromotionOutcomes = value; }
    }

    public (long Promoted, long Rejected) CumulativePromotionCounts
    {
        get { lock (_gate) return (_cumulativeStrategiesPromoted, _cumulativeStrategiesRejected); }
    }

    public IReadOnlyDictionary<int, BonesPerSeatPromotionCounts> PerSeatPromotionCounts
    {
        get
        {
            lock (_gate)
            {
                return new Dictionary<int, BonesPerSeatPromotionCounts>(_perSeatPromotionCounts);
            }
        }
    }

    /// <summary>Games completed per hour, computed as a running average.</summary>
    public double GamesPerHour
    {
        get
        {
            lock (_gate)
            {
                if (_firstGameStartedAt == DateTimeOffset.MinValue || _totalGamesCompleted == 0)
                    return 0.0;

                var elapsedHours = (DateTimeOffset.UtcNow - _firstGameStartedAt).TotalHours;
                if (elapsedHours <= 0)
                    return 0.0;

                return _totalGamesCompleted / elapsedHours;
            }
        }
    }

    /// <summary>Average game duration in milliseconds.</summary>
    public double AverageGameDurationMs
    {
        get
        {
            lock (_gate)
            {
                if (_totalGamesCompleted == 0)
                    return 0.0;

                return (double)_totalGameDurationMs / _totalGamesCompleted;
            }
        }
    }

    /// <summary>
    /// Ratio of active games to max concurrent capacity (0.0 - 1.0).
    /// Set externally by the host based on current semaphore usage.
    /// </summary>
    public double ConcurrentUtilizationPercent { get; internal set; }

    /// <summary>Record a completed game for throughput calculation.</summary>
    internal void RecordGameCompleted(long durationMs)
    {
        lock (_gate)
        {
            if (_firstGameStartedAt == DateTimeOffset.MinValue)
                _firstGameStartedAt = DateTimeOffset.UtcNow;

            _totalGamesCompleted++;
            _totalGameDurationMs += durationMs;
        }
    }

    internal void RecordIterationCompleted(
        IReadOnlyList<BonesSeatPromotionOutcome> promotionOutcomes)
    {
        var now = DateTimeOffset.UtcNow;
        lock (_gate)
        {
            _coLearningIterationsCompleted++;
            _lastPromotionOutcomes = promotionOutcomes
                .Select(o => o with { TimestampUtc = o.TimestampUtc == default ? now : o.TimestampUtc })
                .ToArray();

            // Accumulate cumulative totals.
            foreach (var outcome in promotionOutcomes)
            {
                if (string.Equals(outcome.Outcome, "Promoted", StringComparison.OrdinalIgnoreCase))
                {
                    _cumulativeStrategiesPromoted++;
                }
                else if (string.Equals(outcome.Outcome, "Rejected", StringComparison.OrdinalIgnoreCase))
                {
                    _cumulativeStrategiesRejected++;
                }

                // Update per-seat counters.
                if (!_perSeatPromotionCounts.TryGetValue(outcome.Seat, out var existing))
                {
                    existing = new BonesPerSeatPromotionCounts(0, 0);
                }

                if (string.Equals(outcome.Outcome, "Promoted", StringComparison.OrdinalIgnoreCase))
                {
                    _perSeatPromotionCounts[outcome.Seat] = existing with { Promoted = existing.Promoted + 1 };
                }
                else if (string.Equals(outcome.Outcome, "Rejected", StringComparison.OrdinalIgnoreCase))
                {
                    _perSeatPromotionCounts[outcome.Seat] = existing with { Rejected = existing.Rejected + 1 };
                }
            }
        }
    }
}
