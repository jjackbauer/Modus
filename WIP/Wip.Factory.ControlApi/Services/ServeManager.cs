using System.Collections.Concurrent;
using Wip.Factory.Contracts.Identifiers;
using Wip.Factory.Contracts.Work;
using Wip.Factory.ControlApi.Contracts;
using Wip.Factory.ControlApi.Infrastructure;
using Wip.Factory.Host.Factory;

namespace Wip.Factory.ControlApi.Services;

/// <summary>
/// Owns adapter-side supervised serve windows. The factory's supervisor lease, identity proof, bounded restarts and
/// status journal remain authoritative. A browser tab never owns this process.
/// </summary>
public sealed class ServeManager
{
    private readonly FactoryService _service;
    private readonly ConcurrentDictionary<string, ServeRun> _runs = new(StringComparer.Ordinal);

    public ServeManager(FactoryService service) => _service = service;

    public async Task<ServeJobDto> StartAsync(string workItemId, int durationSeconds, string actor, ILogger logger)
    {
        if (_runs.TryGetValue(workItemId, out var existing) && existing.Job.State is "starting" or "running" or "stopping")
        {
            throw ApiException.Conflict("serve-already-running", "A supervised serve window is already active for this work item.");
        }

        var snapshot = await _service.Runtime.Store
            .LoadAsync(new WorkItemId(workItemId))
            .ConfigureAwait(false);
        if (snapshot is null)
        {
            throw ApiException.NotFound("work-item-not-found", "No work item with that identifier exists.");
        }

        if (snapshot.State is not (FactoryWorkState.Released or FactoryWorkState.OperationallyValidated))
        {
            throw ApiException.Conflict("not-released", "Only a verified release can be supervised. The work item is in state " + snapshot.State + ".");
        }

        var duration = Math.Clamp(durationSeconds, 10, _service.Options.MaxServeSeconds);
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_service.ShutdownToken);
        var job = new ServeJobDto(
            "serve-" + Guid.NewGuid().ToString("N"),
            workItemId,
            "starting",
            DateTimeOffset.UtcNow,
            null,
            null,
            null,
            0,
            null,
            null,
            actor);

        var run = new ServeRun(job, cts, duration);
        _runs[workItemId] = run;
        _ = Task.Run(() => RunAsync(run, logger));
        return job;
    }

    private async Task RunAsync(ServeRun run, ILogger logger)
    {
        try
        {
            await UpdateAsync(run, static job => job with { State = "running" }).ConfigureAwait(false);
            var outcome = await _service.Runtime
                .ServeAsync(run.Job.WorkItemId, TimeSpan.FromSeconds(run.DurationSeconds), run.Cancellation.Token)
                .ConfigureAwait(false);

            await UpdateAsync(run, job => job with
            {
                State = outcome.Ok ? "completed" : "failed",
                FinishedAtUtc = DateTimeOffset.UtcNow,
                ProcessId = outcome.ProcessId,
                Url = outcome.Url,
                Restarts = outcome.RestartCount,
                Error = outcome.Error,
                MergedSha = outcome.MergedSha
            }).ConfigureAwait(false);

            if (!outcome.Ok)
            {
                logger.LogWarning("Serve window {JobId} for {WorkItem} failed: {Error}", run.Job.JobId, run.Job.WorkItemId, outcome.Error);
            }
        }
        catch (OperationCanceledException)
        {
            await UpdateAsync(run, job => job with
            {
                State = "stopped",
                FinishedAtUtc = DateTimeOffset.UtcNow,
                Error = "cancelled-by-operator"
            }).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Serve window {JobId} failed", run.Job.JobId);
            await UpdateAsync(run, job => job with
            {
                State = "failed",
                FinishedAtUtc = DateTimeOffset.UtcNow,
                Error = "serve-error:" + exception.Message
            }).ConfigureAwait(false);
        }
        finally
        {
            run.Cancellation.Dispose();
        }
    }

    private static Task UpdateAsync(ServeRun run, Func<ServeJobDto, ServeJobDto> update)
    {
        lock (run)
        {
            run.Job = update(run.Job);
            return Task.CompletedTask;
        }
    }

    public async Task<ServeJobDto> StopAsync(string workItemId, string actor, ILogger logger)
    {
        if (!_runs.TryGetValue(workItemId, out var run))
        {
            throw ApiException.NotFound("serve-not-running", "No supervised serve window is active for this work item.");
        }

        ServeJobDto current;
        lock (run)
        {
            current = run.Job;
        }

        if (current.State is "completed" or "failed" or "stopped")
        {
            throw ApiException.Conflict("serve-not-running", "The last serve window has already finished.");
        }

        run.Cancellation.Cancel();
        await UpdateAsync(run, job => job with { State = "stopping" }).ConfigureAwait(false);

        // Give the supervisor a short chance to stop the process it owns before reporting the cancellation.
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
        }
        catch (TaskCanceledException)
        {
        }

        lock (run)
        {
            current = run.Job;
        }

        logger.LogInformation("Serve stop requested by {Actor} for {WorkItem}: {State}", actor, workItemId, current.State);
        return current;
    }

    public ServingDto GetServing(string workItemId, string mergedSha)
    {
        var journal = new ServeStatusJournal(Path.Combine(
            _service.Options.Factory.Root, "serve", SafeName(workItemId) + ".status.log"));
        var latest = journal.Latest();

        _runs.TryGetValue(workItemId, out var run);
        ServeJobDto? job = null;
        if (run is not null)
        {
            lock (run)
            {
                job = run.Job;
            }
        }

        var active = job is { State: "starting" or "running" };

        // The journal is historical evidence, not process liveness. If this adapter holds no
        // tracked run, a live-looking journal row may predate an adapter restart. Reporting it as
        // "serving" would claim a running application nobody can verify, so reconcile it here,
        // once, for every reader (release list, release detail, work detail, overview).
        string? status;
        string? detail;
        if (job is not null)
        {
            status = job.State switch
            {
                "running" => "serving",
                "starting" => "starting",
                "stopping" => "stopping",
                "completed" or "stopped" => "stopped",
                "failed" => "failed",
                _ => job.State,
            };
            detail = job.Error ?? latest?.Detail;
        }
        else
        {
            var journalStatus = latest?.Status;
            if (journalStatus is "serving" or "running" or "starting")
            {
                status = "unknown";
                detail = "The last serve journal record reports '" + journalStatus + "' at "
                    + (latest is null ? "an unrecorded time" : latest.RecordedAtUtc.ToString("O"))
                    + ", but this adapter tracks no supervised process (it has restarted since that record). "
                    + "Treat the release as not serving until a new serve window proves a running process.";
            }
            else
            {
                status = journalStatus;
                detail = latest?.Detail;
            }
        }

        return new ServingDto(
            latest?.WorkItemId ?? workItemId,
            latest?.MergedSha ?? mergedSha,
            status,
            latest?.ProcessId ?? job?.ProcessId ?? 0,
            latest?.RestartCount ?? job?.Restarts ?? 0,
            detail,
            latest?.RecordedAtUtc,
            active,
            active ? job?.Url ?? latest?.ServedUrl : null,
            job?.JobId,
            active && run is not null
                ? job!.StartedAtUtc.AddSeconds(run.DurationSeconds).ToString("O")
                : null);
    }

    public ServeJobDto? GetJob(string workItemId)
    {
        if (!_runs.TryGetValue(workItemId, out var run))
        {
            return null;
        }

        lock (run)
        {
            return run.Job;
        }
    }

    public void StopAll()
    {
        foreach (var run in _runs.Values)
        {
            try
            {
                run.Cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    private static string SafeName(string value)
        => new(value.Select(character =>
            char.IsLetterOrDigit(character) || character is '-' or '_' or '.' ? character : '_').ToArray());

    private sealed class ServeRun(ServeJobDto job, CancellationTokenSource cancellation, int durationSeconds)
    {
        public ServeJobDto Job { get; set; } = job;
        public CancellationTokenSource Cancellation { get; } = cancellation;
        public int DurationSeconds { get; } = durationSeconds;
    }
}
