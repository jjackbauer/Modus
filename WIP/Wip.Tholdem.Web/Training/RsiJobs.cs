using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Wip.Tholdem.Agents.Learning;

namespace Wip.Tholdem.Web.Training;

/// <summary>The POST /api/rsi body. Omitted optional budgets use the documented defaults.</summary>
public sealed class RsiJobRequest
{
    public ulong? Seed { get; init; }

    public int? MetaIterations { get; init; }

    public int? InnerIterations { get; init; }

    public int? TrainingHands { get; init; }

    public int? EvaluationSeeds { get; init; }

    public int? EvaluationHands { get; init; }

    public double? MarginBbPer100 { get; init; }

    public int? MinSeeds { get; init; }

    public int? MetaSeeds { get; init; }

    public int? MetaHands { get; init; }

    public int? FreshSeeds { get; init; }

    public int? FreshHands { get; init; }

    public double? RequiredHitRateImprovement { get; init; }

    public int? MaxInnerIterations { get; init; }

    public int? MaxEvaluationsPerEpisode { get; init; }

    public int? MaxWallClockSeconds { get; init; }

    public string? StrategyId { get; init; }

    public bool AllowWeakSeed { get; init; }
}

/// <summary>A validated mini-RSI request.</summary>
public sealed record ValidatedRsiRequest(
    MetaRunOptions Options,
    string OptionsHash,
    string JobId,
    bool WeakSeed);

/// <summary>Bounds and the deterministic option hash for the mini-RSI endpoint.</summary>
public static class RsiJobValidator
{
    public const int MaxMetaIterations = 8;

    public const int MaxWeakSeedMetaIterations = 2;

    public static ValidatedRsiRequest? Validate(
        RsiJobRequest? request,
        string dataDirectory,
        out OptionError? error)
    {
        error = null;
        if (request is null)
        {
            error = new OptionError("missing-body", "body", "A JSON body is required.");
            return null;
        }

        if (request.Seed is null)
        {
            error = new OptionError("missing-option", "seed", "seed is required.");
            return null;
        }

        if (!SessionSeedPolicy.IsStrong(request.Seed.Value) && !request.AllowWeakSeed)
        {
            error = new OptionError(
                "weak-seed",
                "seed",
                "A seed below the minimum is guessable; opt in explicitly.",
                Min: SessionSeedPolicy.MinimumSeed);
            return null;
        }

        if (request.AllowWeakSeed && request.MetaIterations is > MaxWeakSeedMetaIterations)
        {
            error = new OptionError(
                "weak-seed-iteration-cap",
                "metaIterations",
                $"A weak-seed meta run is capped at {MaxWeakSeedMetaIterations} meta-iterations.",
                Max: MaxWeakSeedMetaIterations);
            return null;
        }

        var metaIterations = request.MetaIterations ?? 2;
        var innerIterations = request.InnerIterations ?? 1;
        var trainingHands = request.TrainingHands ?? 60;
        var evaluationSeeds = request.EvaluationSeeds ?? 4;
        var evaluationHands = request.EvaluationHands ?? 3;
        var margin = request.MarginBbPer100 ?? 0.0;
        var minSeeds = request.MinSeeds ?? 2;
        var metaSeeds = request.MetaSeeds ?? 3;
        var metaHands = request.MetaHands ?? 2;
        var freshSeeds = request.FreshSeeds ?? 3;
        var freshHands = request.FreshHands ?? 2;
        var requiredImprovement = request.RequiredHitRateImprovement ?? 0.1;
        var maxInnerIterations = request.MaxInnerIterations ?? 200;
        var maxEvaluationsPerEpisode = request.MaxEvaluationsPerEpisode ?? 100;
        var wallClockSeconds = request.MaxWallClockSeconds ?? 120;
        if (wallClockSeconds is < 5 or > 600)
        {
            error = new OptionError(
                "invalid-option",
                "maxWallClockSeconds",
                "maxWallClockSeconds must be between 5 and 600.",
                Min: 5,
                Max: 600);
            return null;
        }

        var strategyId = string.IsNullOrWhiteSpace(request.StrategyId) ? null : request.StrategyId.Trim();
        if (strategyId is not null)
        {
            try
            {
                _ = TrainingLoop.ReadPersistedStrategy(dataDirectory, strategyId);
            }
            catch (Exception)
            {
                error = new OptionError(
                    "unknown-strategy",
                    "strategyId",
                    "strategyId must name a readable, content-hash-valid strategy artifact.");
                return null;
            }
        }

        var options = new MetaRunOptions(
            request.Seed.Value,
            metaIterations,
            innerIterations,
            trainingHands,
            evaluationSeeds,
            evaluationHands,
            new EvaluationCriteria(margin, minSeeds),
            metaSeeds,
            metaHands,
            freshSeeds,
            freshHands,
            requiredImprovement,
            dataDirectory,
            strategyId,
            maxInnerIterations,
            maxEvaluationsPerEpisode,
            TimeSpan.FromSeconds(wallClockSeconds));
        var offending = MetaLoop.Validate(options);
        if (offending is not null)
        {
            var field = char.ToLowerInvariant(offending[0]) + offending[1..];
            error = new OptionError(
                "invalid-option",
                field,
                $"{field} is outside its documented bound.",
                Min: 0);
            return null;
        }

        var optionsHash = ComputeOptionsHash(options, request.AllowWeakSeed);
        return new ValidatedRsiRequest(
            options,
            optionsHash,
            "rsi-" + optionsHash[..16],
            request.AllowWeakSeed && !SessionSeedPolicy.IsStrong(request.Seed.Value));
    }

    public static string ComputeOptionsHash(MetaRunOptions options, bool allowWeakSeed)
    {
        var builder = new StringBuilder(512);
        builder.Append("{\"seed\":").Append(options.Seed.ToString(CultureInfo.InvariantCulture));
        builder.Append(",\"metaIterations\":").Append(options.MetaIterations.ToString(CultureInfo.InvariantCulture));
        builder.Append(",\"innerIterations\":").Append(options.InnerIterations.ToString(CultureInfo.InvariantCulture));
        builder.Append(",\"trainingHands\":").Append(options.TrainingHands.ToString(CultureInfo.InvariantCulture));
        builder.Append(",\"evaluationSeeds\":").Append(options.EvaluationSeeds.ToString(CultureInfo.InvariantCulture));
        builder.Append(",\"evaluationHands\":").Append(options.EvaluationHandsPerMatch.ToString(CultureInfo.InvariantCulture));
        builder.Append(",\"marginBbPer100\":").Append(options.Criteria.MarginBbPer100.ToString("R", CultureInfo.InvariantCulture));
        builder.Append(",\"minSeeds\":").Append(options.Criteria.MinSeeds.ToString(CultureInfo.InvariantCulture));
        builder.Append(",\"metaSeeds\":").Append(options.MetaSeeds.ToString(CultureInfo.InvariantCulture));
        builder.Append(",\"metaHands\":").Append(options.MetaHandsPerMatch.ToString(CultureInfo.InvariantCulture));
        builder.Append(",\"freshSeeds\":").Append(options.FreshSeeds.ToString(CultureInfo.InvariantCulture));
        builder.Append(",\"freshHands\":").Append(options.FreshHandsPerMatch.ToString(CultureInfo.InvariantCulture));
        builder.Append(",\"requiredHitRateImprovement\":").Append(options.RequiredHitRateImprovement.ToString("R", CultureInfo.InvariantCulture));
        builder.Append(",\"maxInnerIterations\":").Append(options.MaxInnerIterations.ToString(CultureInfo.InvariantCulture));
        builder.Append(",\"maxEvaluationsPerEpisode\":").Append(options.MaxEvaluationsPerEpisode.ToString(CultureInfo.InvariantCulture));
        builder.Append(",\"maxWallClockSeconds\":").Append(((int)(options.MaxWallClock ?? MetaRunOptions.DefaultMaxWallClock).TotalSeconds).ToString(CultureInfo.InvariantCulture));
        builder.Append(",\"strategyId\":");
        if (options.StartingStrategyId is null)
        {
            builder.Append("null");
        }
        else
        {
            builder.Append('"').Append(options.StartingStrategyId.Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal)).Append('"');
        }

        builder.Append(",\"allowWeakSeed\":").Append(allowWeakSeed ? "true" : "false").Append('}');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
    }
}

/// <summary>One meta-iteration as exposed by the job API.</summary>
public sealed record RsiJobEntry(
    int Iteration,
    string IncumbentProposerId,
    string CandidateProposerId,
    string Outcome,
    bool Eligible,
    string? DocumentHash,
    double? PrimaryLowerBoundBbPer100,
    double? HitRateImprovement);

/// <summary>The last evaluated meta-iteration as exposed by the job API.</summary>
public sealed record RsiReportSummary(
    double PrimaryLowerBoundBbPer100,
    double MarginBbPer100,
    int Seeds,
    string Decision,
    bool Verified,
    double HitRateImprovement,
    double HitRateLowerBound,
    double FreshLowerBoundBbPer100);

/// <summary>The GET /api/rsi/{jobId} body.</summary>
public sealed record RsiJobSnapshot(
    string JobId,
    string Status,
    string Phase,
    int Iteration,
    int TotalIterations,
    string? IncumbentProposerId,
    string? CandidateProposerId,
    RsiReportSummary? LastReport,
    IReadOnlyList<RsiJobEntry> Entries,
    string? EligibleProposerId,
    string? Error,
    string AcceptedAtUtc,
    string OptionsHash,
    bool CanPromote,
    string? PromotionStatus,
    bool WeakSeed,
    string? FrozenManifestHash);

/// <summary>The bounded recent mini-RSI jobs list.</summary>
public sealed record RsiJobList(IReadOnlyList<RsiJobSnapshot> Jobs, string? ActiveJobId);

/// <summary>The accepted/conflict/existing result of a mini-RSI submission.</summary>
public sealed record RsiSubmission(
    bool Accepted,
    bool Existing,
    RsiJobSnapshot? Job,
    object? Error,
    int StatusCode)
{
    public static RsiSubmission ExistingJob(RsiJobSnapshot snapshot) => new(true, true, snapshot, null, 202);

    public static RsiSubmission Started(RsiJobSnapshot snapshot) => new(true, false, snapshot, null, 202);

    public static RsiSubmission Conflict(object error) => new(false, false, null, error, 409);
}

/// <summary>One running or finished mini-RSI job.</summary>
public sealed class RsiJob : IQueuedJob, IMetaObserver
{
    private readonly object _gate = new();
    private readonly CancellationTokenSource _cancellation = new();
    private readonly List<RsiJobEntry> _entries = [];
    private MetaIterationResult? _lastCompleted;
    private MetaIterationResult? _lastEligible;
    private string _status = "queued";
    private string _phase = "frozen-check";
    private int _iteration;
    private string? _incumbentProposerId;
    private string? _candidateProposerId;
    private string? _error;
    private string? _promotionStatus;
    private string? _frozenManifestHash;

    public RsiJob(ValidatedRsiRequest validated, string acceptedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(validated);
        Validated = validated;
        AcceptedAtUtc = acceptedAtUtc;
    }

    public ValidatedRsiRequest Validated { get; }

    public string AcceptedAtUtc { get; }

    public string JobId => Validated.JobId;

    public CancellationToken CancellationToken => _cancellation.Token;

    public void RequestCancel() => _cancellation.Cancel();

    public async Task RunAsync()
    {
        lock (_gate)
        {
            if (!string.Equals(_status, "queued", StringComparison.Ordinal))
            {
                return;
            }

            _status = "running";
        }

        await Task.Run(RunCore, CancellationToken.None).ConfigureAwait(false);
    }

    public void OnProgress(MetaProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        lock (_gate)
        {
            _phase = PhaseName(progress.Phase);
            _iteration = progress.Iteration;
            if (progress.Completed is not null)
            {
                ApplyCompleted(progress.Completed);
            }
        }
    }

    public RsiJobSnapshot Snapshot()
    {
        lock (_gate)
        {
            var decision = _lastCompleted?.Decision;
            var document = _lastCompleted?.Document;
            return new RsiJobSnapshot(
                JobId,
                _status,
                _phase,
                _iteration,
                Validated.Options.MetaIterations,
                _incumbentProposerId,
                _candidateProposerId,
                decision is null || document is null
                    ? null
                    : new RsiReportSummary(
                        decision.PrimaryLowerBound,
                        decision.MarginBbPer100,
                        decision.PrimarySeeds,
                        decision.Reason,
                        decision.PrimaryVerified,
                        decision.HitRate.Improvement,
                        decision.HitRate.LowerBoundImprovement,
                        decision.FreshLowerBound),
                _entries.ToArray(),
                _lastEligible?.Candidate.Id,
                _error,
                AcceptedAtUtc,
                Validated.OptionsHash,
                CanPromoteUnlocked(),
                _promotionStatus,
                Validated.WeakSeed,
                _frozenManifestHash);
        }
    }

    public RsiJobEntry? LastEligibleEntry { get; private set; }

    public MetaIterationResult? LastEligible
    {
        get
        {
            lock (_gate)
            {
                return _lastEligible;
            }
        }
    }

    public bool IsComplete
    {
        get
        {
            lock (_gate)
            {
                return string.Equals(_status, "complete", StringComparison.Ordinal);
            }
        }
    }

    public string Status
    {
        get
        {
            lock (_gate)
            {
                return _status;
            }
        }
    }

    public void MarkPromoted(string status)
    {
        lock (_gate)
        {
            _promotionStatus = status;
        }
    }

    private void RunCore()
    {
        try
        {
            var result = MetaLoop.Run(Validated.Options, CancellationToken, this);
            lock (_gate)
            {
                _status = "complete";
                _phase = "persist";
                _frozenManifestHash = result.FrozenManifest.ManifestHash;
                _promotionStatus = CanPromoteUnlocked() ? "eligible-not-applied" : null;
            }
        }
        catch (OperationCanceledException)
        {
            lock (_gate)
            {
                _status = "cancelled";
                _promotionStatus = null;
            }
        }
        catch (FrozenManifestMismatchException exception)
        {
            lock (_gate)
            {
                _status = "failed";
                _error = "frozen-manifest-mismatch: " + exception.Reason;
            }
        }
        catch (MetaBudgetExceededException exception)
        {
            lock (_gate)
            {
                _status = "failed";
                _error = "budget-exceeded: " + exception.Budget;
            }
        }
        catch (Exception exception)
        {
            lock (_gate)
            {
                _status = "failed";
                _error = "rsi-failed: " + SafeMessage(exception);
            }
        }
    }

    private void ApplyCompleted(MetaIterationResult completed)
    {
        _lastCompleted = completed;
        _incumbentProposerId = completed.Incumbent.Id;
        _candidateProposerId = completed.Candidate.Id;
        var entry = new RsiJobEntry(
            completed.Iteration,
            completed.Incumbent.Id,
            completed.Candidate.Id,
            completed.Outcome,
            completed.Eligible,
            completed.Document?.DocumentHash,
            completed.Decision?.PrimaryLowerBound,
            completed.Decision?.HitRate.Improvement);
        for (var index = 0; index < _entries.Count; index++)
        {
            if (_entries[index].Iteration == completed.Iteration)
            {
                _entries[index] = entry;
                break;
            }
        }

        if (_entries.Count == 0 || _entries[^1].Iteration != completed.Iteration)
        {
            _entries.Add(entry);
        }

        if (completed.Eligible)
        {
            _lastEligible = completed;
            LastEligibleEntry = entry;
        }
    }

    private bool CanPromoteUnlocked() =>
        string.Equals(_status, "complete", StringComparison.Ordinal) && _lastEligible is not null;

    private static string PhaseName(MetaPhase phase) => phase switch
    {
        MetaPhase.FrozenCheck => "frozen-check",
        MetaPhase.Propose => "propose",
        MetaPhase.Episodes => "episodes",
        MetaPhase.Evaluate => "evaluate",
        MetaPhase.Gate => "gate",
        _ => "persist",
    };

    private static string SafeMessage(Exception exception)
    {
        var message = exception.GetType().Name + ": " + exception.Message;
        message = message.Replace('\r', ' ').Replace('\n', ' ');
        return message.Length <= 300 ? message : message[..300];
    }
}

/// <summary>The mini-RSI job registry, sharing the process-wide one-at-a-time scheduler.</summary>
public sealed class RsiJobRegistry
{
    public const int MaxRecentJobs = 20;

    private readonly object _gate = new();
    private readonly Dictionary<string, RsiJob> _jobs = new(StringComparer.Ordinal);
    private readonly List<string> _order = [];
    private readonly JobScheduler _scheduler;
    private readonly TrainingSeedRegistry _seeds;

    public RsiJobRegistry(JobScheduler scheduler, TrainingSeedRegistry seeds)
    {
        _scheduler = scheduler;
        _seeds = seeds;
    }

    public RsiSubmission Submit(ValidatedRsiRequest validated, string acceptedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(validated);
        lock (_gate)
        {
            if (_jobs.TryGetValue(validated.JobId, out var existing))
            {
                return RsiSubmission.ExistingJob(existing.Snapshot());
            }

            var active = _scheduler.ActiveJobId;
            if (active is not null)
            {
                return RsiSubmission.Conflict(new { error = "job-already-running", activeJobId = active });
            }

            if (!_seeds.TryReserve(
                    validated.Options.Seed,
                    validated.OptionsHash,
                    validated.JobId,
                    acceptedAtUtc,
                    out var existingOptionsHash,
                    out var reason))
            {
                return RsiSubmission.Conflict(new
                {
                    error = reason,
                    seed = validated.Options.Seed,
                    existingOptionsHash,
                });
            }

            var job = new RsiJob(validated, acceptedAtUtc);
            _jobs[validated.JobId] = job;
            _order.Add(validated.JobId);
            while (_order.Count > MaxRecentJobs)
            {
                var oldest = _order[0];
                if (string.Equals(_scheduler.ActiveJobId, oldest, StringComparison.Ordinal))
                {
                    break;
                }

                _order.RemoveAt(0);
                _jobs.Remove(oldest);
            }

            if (!_scheduler.TryStart(job))
            {
                _jobs.Remove(validated.JobId);
                _order.Remove(validated.JobId);
                _seeds.Release(validated.Options.Seed, validated.JobId);
                return RsiSubmission.Conflict(new { error = "job-already-running", activeJobId = _scheduler.ActiveJobId });
            }

            return RsiSubmission.Started(job.Snapshot());
        }
    }

    public RsiJob? TryGet(string jobId)
    {
        lock (_gate)
        {
            return _jobs.TryGetValue(jobId, out var job) ? job : null;
        }
    }

    public RsiJobList Recent()
    {
        lock (_gate)
        {
            var jobs = new List<RsiJobSnapshot>(Math.Min(_order.Count, MaxRecentJobs));
            for (var index = _order.Count - 1; index >= 0 && jobs.Count < MaxRecentJobs; index--)
            {
                jobs.Add(_jobs[_order[index]].Snapshot());
            }

            return new RsiJobList(jobs, _scheduler.ActiveJobId);
        }
    }

    public bool TryRequestCancel(string jobId, out string reason)
    {
        lock (_gate)
        {
            reason = "cancelling";
            if (!_jobs.TryGetValue(jobId, out var job))
            {
                reason = "unknown-job";
                return false;
            }

            var status = job.Status;
            if (status is not ("queued" or "running"))
            {
                reason = "job-not-running";
                return false;
            }

            job.RequestCancel();
            return true;
        }
    }
}
