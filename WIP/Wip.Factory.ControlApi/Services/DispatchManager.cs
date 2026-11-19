using System.Globalization;
using Wip.Factory.ControlApi.Contracts;
using Wip.Factory.ControlApi.Infrastructure;
using Wip.Factory.Core.Fanout;
using Wip.Factory.Host.Factory;

namespace Wip.Factory.ControlApi.Services;

/// <summary>
/// Owns the adapter-side dispatch cycle. The factory's exclusive run lock remains the authority: this manager only
/// avoids launching two adapter-held cycles at once and keeps the operator-visible job state.
/// </summary>
public sealed class DispatchManager
{
    private readonly FactoryService _service;
    private readonly SemaphoreSlim _stateGate = new(1, 1);
    private readonly List<DispatchJobDto> _recent = [];
    private DispatchJobDto? _current;

    public DispatchManager(FactoryService service) => _service = service;

    public async Task<DispatchJobDto> StartAsync(int maxPasses, string actor, ILogger logger)
    {
        await _stateGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_current is { State: "running" })
            {
                throw ApiException.Conflict("dispatch-already-running", "A dispatch cycle is already running for this factory instance.");
            }

            var job = new DispatchJobDto(
                "dispatch-" + Guid.NewGuid().ToString("N"),
                "running",
                "dispatch-cycle",
                null,
                DateTimeOffset.UtcNow,
                null,
                0, 0, 0, 0, 0,
                null,
                actor,
                ExclusiveLockHeld: false);

            _current = job;
            _ = Task.Run(() => RunAsync(job.JobId, Math.Clamp(maxPasses, 1, 64), logger));
            return job;
        }
        finally
        {
            _stateGate.Release();
        }
    }

    private async Task RunAsync(string jobId, int maxPasses, ILogger logger)
    {
        var started = DateTimeOffset.UtcNow;
        try
        {
            var (reports, error) = await _service.Runtime
                .RunExclusiveAsync(maxPasses, _service.ShutdownToken)
                .ConfigureAwait(false);

            var completed = DateTimeOffset.UtcNow;
            var claimed = reports.Sum(static report => report.Claimed);
            var dispatched = reports.Sum(static report => report.Dispatched);
            var blocked = reports.Sum(static report => report.Blocked);
            var failed = reports.Sum(static report => report.Failed);
            var state = error is null ? "completed" : error == "host-lock-held" ? "refused" : "failed";

            await _stateGate.WaitAsync().ConfigureAwait(false);
            try
            {
                var job = _current?.JobId == jobId
                    ? _current
                    : _recent.FirstOrDefault(candidate => candidate.JobId == jobId);

                if (job is not null)
                {
                    var updated = job with
                    {
                        State = state,
                        FinishedAtUtc = completed,
                        Claimed = claimed,
                        Dispatched = dispatched,
                        Blocked = blocked,
                        Failed = failed,
                        Passes = reports.Count,
                        Error = error,
                        ExclusiveLockHeld = error is null
                    };

                    if (_current?.JobId == jobId)
                    {
                        _current = updated;
                    }

                    var index = _recent.FindIndex(candidate => candidate.JobId == jobId);
                    if (index >= 0)
                    {
                        _recent[index] = updated;
                    }
                }
            }
            finally
            {
                _stateGate.Release();
            }

            if (error is not null)
            {
                logger.LogWarning("Dispatch cycle {JobId} ended with {Error}", jobId, error);
            }
            else
            {
                logger.LogInformation(
                    "Dispatch cycle {JobId}: claimed={Claimed} dispatched={Dispatched} blocked={Blocked} failed={Failed} in {Elapsed}ms",
                    jobId, claimed, dispatched, blocked, failed, (completed - started).TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture));
            }
        }
        catch (OperationCanceledException)
        {
            await CompleteAsync(jobId, "cancelled", "adapter-shutdown").ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Dispatch cycle {JobId} failed", jobId);
            await CompleteAsync(jobId, "failed", "dispatch-error:" + exception.Message).ConfigureAwait(false);
        }
    }

    private async Task CompleteAsync(string jobId, string state, string error)
    {
        await _stateGate.WaitAsync().ConfigureAwait(false);
        try
        {
            _current = _current?.JobId == jobId
                ? _current with { State = state, FinishedAtUtc = DateTimeOffset.UtcNow, Error = error }
                : _current;
        }
        finally
        {
            _stateGate.Release();
        }
    }

    public async Task<DispatchStatusDto> StatusAsync()
    {
        await _stateGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_current is { State: not "running" } finished)
            {
                _recent.Insert(0, finished);
                _current = null;
            }

            while (_recent.Count > 20)
            {
                _recent.RemoveAt(_recent.Count - 1);
            }

            return new DispatchStatusDto(_current is { State: "running" }, _current, _recent.ToArray());
        }
        finally
        {
            _stateGate.Release();
        }
    }

    public async Task<DispatchJobDto?> GetAsync(string jobId)
    {
        await _stateGate.WaitAsync().ConfigureAwait(false);
        try
        {
            return _current?.JobId == jobId
                ? _current
                : _recent.FirstOrDefault(job => job.JobId == jobId);
        }
        finally
        {
            _stateGate.Release();
        }
    }
}
