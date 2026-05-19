namespace Modus.Core.Plugins;

using System.Globalization;
using Modus.Core.Messaging;

public sealed class FiveSecondIntervalsTimerPrint : IScheduledTimerTaskExtension
{
    private const string OperationName = "Timer.WriteCurrentTime";
    private const string RecurringJobName = "Timer.WriteCurrentTime.Every5Seconds";
    private readonly Func<DateTimeOffset> _utcNowProvider;
    private readonly Action<string> _writeLine;
    private readonly TimeSpan _recurringInterval;

    public FiveSecondIntervalsTimerPrint(
        Func<DateTimeOffset> utcNowProvider,
        Action<string> writeLine,
        TimeSpan recurringInterval)
    {
        _utcNowProvider = utcNowProvider ?? throw new ArgumentNullException(nameof(utcNowProvider));
        _writeLine = writeLine ?? throw new ArgumentNullException(nameof(writeLine));
        if (recurringInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(recurringInterval), "Recurring interval must be greater than zero.");
        }

        _recurringInterval = recurringInterval;
    }

    public IReadOnlyCollection<string> SupportedOperations => [OperationName];

    public void RegisterSchedules(IPluginScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        scheduler.ScheduleRecurring(RecurringJobName, _recurringInterval, OperationName);
    }

    public SyncResponse Handle(SyncRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!string.Equals(request.Operation, OperationName, StringComparison.Ordinal))
        {
            return new SyncResponse(
                Success: false,
                Payload: "unsupported-operation",
                Status: SyncResponseStatus.Rejected,
                ServedFromFallback: false,
                CorrelationId: request.CorrelationId);
        }

        var timestamp = _utcNowProvider().ToString("O", CultureInfo.InvariantCulture);
        _writeLine(timestamp);

        return new SyncResponse(
            Success: true,
            Payload: timestamp,
            Status: SyncResponseStatus.Success,
            ServedFromFallback: false,
            CorrelationId: request.CorrelationId);
    }
}
