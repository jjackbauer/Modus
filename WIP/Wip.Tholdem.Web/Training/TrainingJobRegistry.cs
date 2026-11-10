using System.Threading.Channels;
using Wip.Tholdem.Agents.Learning;

namespace Wip.Tholdem.Web.Training;

/// <summary>A job the host's single background worker can execute.</summary>
public interface IQueuedJob
{
    string JobId { get; }

    Task RunAsync();
}

/// <summary>
/// A single-consumer work queue. The active job id is the process-wide one-at-a-time authority for
/// both training and mini-RSI jobs; it is set atomically before the item is queued and released by
/// the worker when the job reaches a terminal state.
/// </summary>
public sealed class JobScheduler
{
    private readonly Channel<IQueuedJob> _queue = Channel.CreateUnbounded<IQueuedJob>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private readonly object _gate = new();
    private string? _activeJobId;

    public string? ActiveJobId
    {
        get
        {
            lock (_gate)
            {
                return _activeJobId;
            }
        }
    }

    public bool TryStart(IQueuedJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        lock (_gate)
        {
            if (_activeJobId is not null)
            {
                return false;
            }

            _activeJobId = job.JobId;
        }

        if (_queue.Writer.TryWrite(job))
        {
            return true;
        }

        lock (_gate)
        {
            _activeJobId = null;
        }

        return false;
    }

    public void Release(string jobId)
    {
        lock (_gate)
        {
            if (string.Equals(_activeJobId, jobId, StringComparison.Ordinal))
            {
                _activeJobId = null;
            }
        }
    }

    internal ValueTask<IQueuedJob> ReadAsync(CancellationToken cancellationToken) =>
        _queue.Reader.ReadAsync(cancellationToken);
}

/// <summary>The host's single background queue consumer.</summary>
public sealed class BackgroundJobWorker : Microsoft.Extensions.Hosting.BackgroundService
{
    private readonly JobScheduler _scheduler;

    public BackgroundJobWorker(JobScheduler scheduler)
    {
        _scheduler = scheduler;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            IQueuedJob job;
            try
            {
                job = await _scheduler.ReadAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                await job.RunAsync();
            }
            catch (Exception)
            {
                // A job maps its own failure into its status; an unexpected exception must never kill
                // the queue consumer or leave the active slot stuck.
            }
            finally
            {
                _scheduler.Release(job.JobId);
            }
        }
    }
}

/// <summary>
/// The append-only seed reservation register. A base seed may be reused only with the exact same
/// options (idempotent rerun); reusing it with different options — a new iteration count, margin or
/// seed stream choice — would let an operator fish for a favourable verdict over the same deals, so
/// it is refused (A2). The register is persisted in the data directory and survives a restart.
/// </summary>
public sealed class TrainingSeedRegistry
{
    public const string FileName = "training-seeds.json";

    private readonly string _path;
    private readonly object _gate = new();
    private Dictionary<string, SeedEntry>? _entries;

    public TrainingSeedRegistry(TholdemViewerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _path = Path.Combine(options.DataDirectory, FileName);
    }

    public bool TryReserve(
        ulong seed,
        string optionsHash,
        string jobId,
        string acceptedAtUtc,
        out string? existingOptionsHash,
        out string reason)
    {
        lock (_gate)
        {
            existingOptionsHash = null;
            reason = "reserved";
            var entries = Load();
            if (entries is null)
            {
                reason = "seed-registry-unreadable";
                return false;
            }

            var key = seed.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (entries.TryGetValue(key, out var existing))
            {
                existingOptionsHash = existing.OptionsHash;
                if (!string.Equals(existing.OptionsHash, optionsHash, StringComparison.Ordinal))
                {
                    reason = "seed-reuse";
                    return false;
                }

                reason = "idempotent";
                return true;
            }

            entries[key] = new SeedEntry(optionsHash, jobId, acceptedAtUtc);
            Save(entries);
            return true;
        }
    }

    public void Release(ulong seed, string jobId)
    {
        lock (_gate)
        {
            var entries = Load();
            if (entries is null)
            {
                return;
            }

            var key = seed.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (entries.TryGetValue(key, out var existing)
                && string.Equals(existing.JobId, jobId, StringComparison.Ordinal))
            {
                entries.Remove(key);
                Save(entries);
            }
        }
    }

    private Dictionary<string, SeedEntry>? Load()
    {
        if (_entries is not null)
        {
            return _entries;
        }

        if (!File.Exists(_path))
        {
            _entries = new Dictionary<string, SeedEntry>(StringComparer.Ordinal);
            return _entries;
        }

        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(_path));
            var entries = new Dictionary<string, SeedEntry>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.GetProperty("seeds").EnumerateObject())
            {
                entries[property.Name] = new SeedEntry(
                    property.Value.GetProperty("optionsHash").GetString() ?? string.Empty,
                    property.Value.GetProperty("jobId").GetString() ?? string.Empty,
                    property.Value.GetProperty("acceptedAtUtc").GetString() ?? string.Empty);
            }

            _entries = entries;
            return _entries;
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException
            or KeyNotFoundException or InvalidOperationException or IOException)
        {
            return null;
        }
    }

    private void Save(IReadOnlyDictionary<string, SeedEntry> entries)
    {
        var builder = new System.Text.StringBuilder(128 + (entries.Count * 160));
        builder.Append("{\"seeds\":{");
        var first = true;
        foreach (var key in entries.Keys.OrderBy(static value => value, StringComparer.Ordinal))
        {
            if (!first)
            {
                builder.Append(',');
            }

            first = false;
            builder.Append('"').Append(key).Append("\":{\"optionsHash\":\"")
                .Append(entries[key].OptionsHash)
                .Append("\",\"jobId\":\"")
                .Append(entries[key].JobId)
                .Append("\",\"acceptedAtUtc\":\"")
                .Append(entries[key].AcceptedAtUtc)
                .Append("\"}");
        }

        builder.Append("}}");
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(
            _path,
            builder.ToString(),
            new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private sealed record SeedEntry(string OptionsHash, string JobId, string AcceptedAtUtc);
}

/// <summary>One running or finished training job. Every read and write of its state takes one lock.</summary>
public sealed class TrainingJob : IQueuedJob, ITrainingObserver
{
    private readonly object _gate = new();
    private readonly CancellationTokenSource _cancellation = new();
    private readonly List<TrainingJobEntry> _entries = [];
    private TrainingIteration? _lastCompleted;
    private string _status = "queued";
    private string _phase = "self-play";
    private int _iteration;
    private string? _championId;
    private string? _challengerId;
    private string? _error;
    private string? _promotionStatus;
    private string? _frozenManifestHash;

    public TrainingJob(ValidatedTrainingRequest validated, string acceptedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(validated);
        Validated = validated;
        AcceptedAtUtc = acceptedAtUtc;
    }

    public ValidatedTrainingRequest Validated { get; }

    public string AcceptedAtUtc { get; }

    public string JobId => Validated.JobId;

    public string DataDirectory => Validated.Options.DataDirectory ?? string.Empty;

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

    public void OnProgress(TrainingProgress progress)
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

    public TrainingJobSnapshot Snapshot()
    {
        lock (_gate)
        {
            return new TrainingJobSnapshot(
                JobId,
                _status,
                _phase,
                _iteration,
                Validated.Options.Iterations,
                _championId,
                _challengerId,
                _lastCompleted?.Report is null
                    ? null
                    : new TrainingReportSummary(
                        _lastCompleted.Report.MeanBbPer100,
                        _lastCompleted.Report.LowerBoundBbPer100,
                        _lastCompleted.Decision.MarginBbPer100,
                        _lastCompleted.Report.Seeds,
                        _lastCompleted.Decision.Reason,
                        _lastCompleted.Verified),
                _entries.ToArray(),
                _error,
                AcceptedAtUtc,
                Validated.OptionsHash,
                CanPromoteUnlocked(),
                _promotionStatus,
                Validated.WeakSeed,
                _frozenManifestHash);
        }
    }

    public void MarkPromoted(string promotionStatus)
    {
        lock (_gate)
        {
            _promotionStatus = promotionStatus;
        }
    }

    public TrainingIteration? LastCompleted
    {
        get
        {
            lock (_gate)
            {
                return _lastCompleted;
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

    public void SetFrozenManifestHash(string? hash)
    {
        lock (_gate)
        {
            _frozenManifestHash = hash;
        }
    }

    private void RunCore()
    {
        try
        {
            var run = TrainingLoop.Run(
                Validated.Options,
                policy: null,
                CancellationToken,
                proposer: null,
                observer: this,
                proposalGuard: ProposalGuards.RejectBehaviorallyInert);
            lock (_gate)
            {
                _status = "complete";
                _phase = "persist";
                _championId = run.FinalChampionId;
                _promotionStatus = CanPromoteUnlocked() ? "applied-by-loop" : null;
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
        catch (Exception exception)
        {
            lock (_gate)
            {
                _status = "failed";
                _error = "training-failed: " + SafeMessage(exception);
            }
        }
    }

    private void ApplyCompleted(TrainingIteration completed)
    {
        _lastCompleted = completed;
        _championId = completed.Outcome == TrainingLoop.PromotedOutcome
            ? completed.ChallengerId
            : completed.ChampionId;
        _challengerId = completed.ChallengerId;

        var entry = new TrainingJobEntry(
            completed.Iteration,
            completed.ChampionId,
            completed.ChallengerId,
            completed.Report?.MeanBbPer100,
            completed.Report?.LowerBoundBbPer100,
            completed.Decision.MarginBbPer100,
            completed.Report?.Seeds,
            completed.Decision.Reason,
            completed.Verified,
            completed.Outcome);
        for (var index = 0; index < _entries.Count; index++)
        {
            if (_entries[index].Iteration == completed.Iteration)
            {
                _entries[index] = entry;
                return;
            }
        }

        _entries.Add(entry);
    }

    private bool CanPromoteUnlocked() =>
        string.Equals(_status, "complete", StringComparison.Ordinal)
        && _lastCompleted is not null
        && _lastCompleted.Report is not null
        && _lastCompleted.Verified
        && _lastCompleted.Outcome == TrainingLoop.PromotedOutcome;

    private static string PhaseName(TrainingPhase phase) => phase switch
    {
        TrainingPhase.SelfPlay => "self-play",
        TrainingPhase.Analyze => "analyze",
        TrainingPhase.Propose => "propose",
        TrainingPhase.Evaluate => "evaluate",
        TrainingPhase.Gate => "gate",
        TrainingPhase.Verify => "verify",
        _ => "persist",
    };

    private static string SafeMessage(Exception exception)
    {
        var message = exception.GetType().Name + ": " + exception.Message;
        message = message.Replace('\r', ' ').Replace('\n', ' ');
        return message.Length <= 300 ? message : message[..300];
    }
}

/// <summary>The accepted/conflict/existing result of a training submission.</summary>
public sealed record TrainingSubmission(
    bool Accepted,
    bool Existing,
    TrainingJobSnapshot? Job,
    object? Error,
    int StatusCode)
{
    public static TrainingSubmission ExistingJob(TrainingJobSnapshot snapshot) =>
        new(true, true, snapshot, null, 202);

    public static TrainingSubmission Started(TrainingJobSnapshot snapshot) =>
        new(true, false, snapshot, null, 202);

    public static TrainingSubmission Conflict(object error) =>
        new(false, false, null, error, 409);
}

/// <summary>The bounded training-job registry and its one-at-a-time admission rule.</summary>
public sealed class TrainingJobRegistry
{
    public const int MaxRecentJobs = 50;

    private readonly object _gate = new();
    private readonly Dictionary<string, TrainingJob> _jobs = new(StringComparer.Ordinal);
    private readonly List<string> _order = [];
    private readonly JobScheduler _scheduler;
    private readonly TrainingSeedRegistry _seeds;

    public TrainingJobRegistry(JobScheduler scheduler, TrainingSeedRegistry seeds)
    {
        _scheduler = scheduler;
        _seeds = seeds;
    }

    public TrainingSubmission Submit(ValidatedTrainingRequest validated, string acceptedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(validated);

        lock (_gate)
        {
            if (_jobs.TryGetValue(validated.JobId, out var existing))
            {
                return TrainingSubmission.ExistingJob(existing.Snapshot());
            }

            var active = _scheduler.ActiveJobId;
            if (active is not null)
            {
                return TrainingSubmission.Conflict(new
                {
                    error = "job-already-running",
                    activeJobId = active,
                });
            }

            if (!_seeds.TryReserve(
                    validated.Options.Seed,
                    validated.OptionsHash,
                    validated.JobId,
                    acceptedAtUtc,
                    out var existingOptionsHash,
                    out var reason))
            {
                return TrainingSubmission.Conflict(new
                {
                    error = reason,
                    seed = validated.Options.Seed,
                    existingOptionsHash,
                    message = reason == "seed-reuse"
                        ? "This seed already ran with different options; reuse would enable seed mining."
                        : "The persisted seed register could not be read.",
                });
            }

            var job = new TrainingJob(validated, acceptedAtUtc);
            _jobs[validated.JobId] = job;
            _order.Add(validated.JobId);
            Evict();
            if (!_scheduler.TryStart(job))
            {
                _jobs.Remove(validated.JobId);
                _order.Remove(validated.JobId);
                _seeds.Release(validated.Options.Seed, validated.JobId);
                return TrainingSubmission.Conflict(new
                {
                    error = "job-already-running",
                    activeJobId = _scheduler.ActiveJobId,
                });
            }

            return TrainingSubmission.Started(job.Snapshot());
        }
    }

    public TrainingJob? TryGet(string jobId)
    {
        lock (_gate)
        {
            return _jobs.TryGetValue(jobId, out var job) ? job : null;
        }
    }

    public TrainingJobList Recent()
    {
        lock (_gate)
        {
            var jobs = new List<TrainingJobSnapshot>(Math.Min(_order.Count, MaxRecentJobs));
            for (var index = _order.Count - 1; index >= 0 && jobs.Count < MaxRecentJobs; index--)
            {
                jobs.Add(_jobs[_order[index]].Snapshot());
            }

            return new TrainingJobList(jobs, _scheduler.ActiveJobId);
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

    private void Evict()
    {
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
    }
}
