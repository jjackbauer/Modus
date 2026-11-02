using System.Threading;

namespace Wip.Bones.Host;

public sealed record BonesLoopIterationError(
    string Message,
    string? FailureStage);

public sealed record BonesLoopIterationStatus(
    long IterationNumber,
    string StageName,
    string? SessionId,
    string? MatchId,
    bool IsComplete,
    string? FailureStage = null);

public sealed class BonesLoopState
{
    private readonly object _gate = new();
    private BonesLoopIterationStatus _currentIteration = new(0, "Idle", null, null, true);
    private BonesLoopIterationError? _lastIterationError;

    private bool _isRunning;
    private long _iterationCount;
    private bool _stopRequested;
    private bool _capReached;
    private string? _stopReason;

    public bool IsRunning
    {
        get => Volatile.Read(ref _isRunning);
        internal set => Volatile.Write(ref _isRunning, value);
    }

    public long IterationCount
    {
        get => Volatile.Read(ref _iterationCount);
        internal set => Volatile.Write(ref _iterationCount, value);
    }

    public bool StopRequested
    {
        get => Volatile.Read(ref _stopRequested);
        internal set => Volatile.Write(ref _stopRequested, value);
    }

    public bool CapReached
    {
        get => Volatile.Read(ref _capReached);
        internal set => Volatile.Write(ref _capReached, value);
    }

    public string? StopReason
    {
        get { lock (_gate) return _stopReason; }
        internal set { lock (_gate) _stopReason = value; }
    }

    public string HostSessionId { get; internal set; } = string.Empty;

    public string? LastPromotionOutcome { get; internal set; }

    public BonesCoLearningMetrics CoLearningMetrics { get; } = new();

    /// <summary>
    /// Total number of individual co-learning sessions completed across all batches.
    /// Delegates to <see cref="BonesCoLearningMetrics.CoLearningIterationsCompleted"/> which is
    /// incremented once per session (not per batch) when a session records its promotion outcomes.
    /// This is distinct from <see cref="IterationCount"/> which counts completed batches.
    /// </summary>
    public long SessionsCompleted => CoLearningMetrics.CoLearningIterationsCompleted;

    public BonesLoopIterationStatus CurrentIteration
    {
        get
        {
            lock (_gate)
                return _currentIteration;
        }
    }

    public BonesLoopIterationError? LastIterationError
    {
        get
        {
            lock (_gate)
                return _lastIterationError;
        }
    }

    internal void UpdateCurrentIteration(BonesLoopIterationStatus status)
    {
        lock (_gate)
            _currentIteration = status;
    }

    internal void SetLastIterationError(BonesLoopIterationError? error)
    {
        lock (_gate)
            _lastIterationError = error;
    }

    internal void ClearLastIterationError()
    {
        lock (_gate)
            _lastIterationError = null;
    }
}
