namespace Modus.Core.Plugins;

using Modus.Core.Messaging;

public sealed class TimerPlugin : PluginBase, ISyncResponder
{
    private readonly IReadOnlyList<IScheduledTimerTaskExtension> _scheduledTaskExtensions;
    private readonly IReadOnlyDictionary<string, IScheduledTimerTaskExtension> _operationOwners;
    private readonly IReadOnlyCollection<OperationName> _supportedOperations;
    private readonly string _autonomousOperation;
    private readonly TimeSpan _recurringInterval;
    private readonly object _lifecycleLock = new();
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;

    public TimerPlugin()
        : this(
            static () => DateTimeOffset.UtcNow,
            static timestamp => Console.WriteLine(timestamp),
            TimeSpan.FromSeconds(5))
    {
    }

    public TimerPlugin(Func<DateTimeOffset> utcNowProvider, Action<string> writeLine)
        : this(utcNowProvider, writeLine, TimeSpan.FromSeconds(5))
    {
    }

    public TimerPlugin(IEnumerable<IScheduledTimerTaskExtension> scheduledTaskExtensions)
        : this(
            scheduledTaskExtensions,
            static () => DateTimeOffset.UtcNow,
            static timestamp => Console.WriteLine(timestamp),
            TimeSpan.FromSeconds(5))
    {
    }

    public TimerPlugin(params IScheduledTimerTaskExtension[] scheduledTaskExtensions)
        : this((IEnumerable<IScheduledTimerTaskExtension>)scheduledTaskExtensions)
    {
    }

    public TimerPlugin(Func<DateTimeOffset> utcNowProvider, Action<string> writeLine, TimeSpan recurringInterval)
        : this(
            CreateDefaultScheduledTaskExtensions(utcNowProvider, writeLine, recurringInterval),
            utcNowProvider,
            writeLine,
            recurringInterval)
    {
    }

    private TimerPlugin(
        IEnumerable<IScheduledTimerTaskExtension> scheduledTaskExtensions,
        Func<DateTimeOffset> utcNowProvider,
        Action<string> writeLine,
        TimeSpan recurringInterval)
    {
        _ = utcNowProvider ?? throw new ArgumentNullException(nameof(utcNowProvider));
        _ = writeLine ?? throw new ArgumentNullException(nameof(writeLine));
        if (recurringInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(recurringInterval), "Recurring interval must be greater than zero.");
        }

        _scheduledTaskExtensions = ValidateScheduledTaskExtensions(scheduledTaskExtensions);
        _operationOwners = BuildOperationOwners(_scheduledTaskExtensions, nameof(scheduledTaskExtensions));
        _supportedOperations = _operationOwners.Keys
            .OrderBy(static operation => operation, StringComparer.Ordinal)
            .Select(k => new OperationName(k))
            .ToArray();
        _autonomousOperation = ResolveAutonomousOperation(_scheduledTaskExtensions, _operationOwners, nameof(scheduledTaskExtensions));
        _recurringInterval = recurringInterval;
    }

    public override PluginId PluginId => new PluginId("Plugin.Timer");

    public override ContractName ContractName => new ContractName("Modus.PluginContract");

    public override Version ContractVersion => new(1, 0, 0);

    public override IReadOnlyCollection<OperationName> SupportedOperations => _supportedOperations;

    public override void Load(PluginLoadContext context)
    {
        base.Load(context);
    }

    public override void Start(PluginStartContext context)
    {
        base.Start(context);

        lock (_lifecycleLock)
        {
            if (_loopTask is { IsCompleted: false })
            {
                return;
            }

            _loopCts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
            var token = _loopCts.Token;
            _loopTask = Task.Run(() => RunAutonomousLoopAsync(token), CancellationToken.None);
        }
    }

    public override void Stop(PluginStopContext context)
    {
        base.Stop(context);
        StopLoop();
    }

    public override void Unload(PluginUnloadContext context)
    {
        base.Unload(context);
        StopLoop();
    }

    public override void RegisterSchedules(IPluginScheduler scheduler)
    {
        base.RegisterSchedules(scheduler);

        foreach (var extension in _scheduledTaskExtensions)
        {
            extension.RegisterSchedules(scheduler);
        }
    }

    public SyncResponse Handle(SyncRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_operationOwners.TryGetValue(request.Operation.Value, out var owner))
        {
            return new SyncResponse(
                Success: false,
                Payload: "unsupported-operation",
                Status: SyncResponseStatus.Rejected,
                ServedFromFallback: false,
                CorrelationId: request.CorrelationId);
        }

        return owner.Handle(request);
    }

    private async Task RunAutonomousLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_recurringInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            _ = Handle(SyncRequest.ForStandardPath(new OperationName(_autonomousOperation)));
        }
    }

    private static IReadOnlyList<IScheduledTimerTaskExtension> ValidateScheduledTaskExtensions(
        IEnumerable<IScheduledTimerTaskExtension> scheduledTaskExtensions)
    {
        ArgumentNullException.ThrowIfNull(scheduledTaskExtensions);

        var extensions = scheduledTaskExtensions.ToArray();
        if (extensions.Length == 0)
        {
            throw new ArgumentException("At least one scheduled task extension must be provided.", nameof(scheduledTaskExtensions));
        }

        if (extensions.Any(static extension => extension is null))
        {
            throw new ArgumentException("Scheduled task extensions cannot contain null entries.", nameof(scheduledTaskExtensions));
        }

        return extensions;
    }

    private static IScheduledTimerTaskExtension[] CreateDefaultScheduledTaskExtensions(
        Func<DateTimeOffset> utcNowProvider,
        Action<string> writeLine,
        TimeSpan recurringInterval)
    {
        ArgumentNullException.ThrowIfNull(utcNowProvider);
        ArgumentNullException.ThrowIfNull(writeLine);
        if (recurringInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(recurringInterval), "Recurring interval must be greater than zero.");
        }

        return [new FiveSecondIntervalsTimerPrint(utcNowProvider, writeLine, recurringInterval)];
    }

    private static IReadOnlyDictionary<string, IScheduledTimerTaskExtension> BuildOperationOwners(
        IEnumerable<IScheduledTimerTaskExtension> scheduledTaskExtensions,
        string parameterName)
    {
        var owners = new Dictionary<string, IScheduledTimerTaskExtension>(StringComparer.Ordinal);

        foreach (var extension in scheduledTaskExtensions)
        {
            var extensionOperations = extension.SupportedOperations
                ?? throw new ArgumentException("Scheduled task extensions must expose a non-null SupportedOperations collection.", parameterName);

            foreach (var operation in extensionOperations)
            {
                if (!owners.ContainsKey(operation.Value))
                {
                    owners.Add(operation.Value, extension);
                }
            }
        }

        if (owners.Count == 0)
        {
            throw new ArgumentException("At least one scheduled task extension operation must be provided.", parameterName);
        }

        return owners;
    }

    private static string ResolveAutonomousOperation(
        IReadOnlyList<IScheduledTimerTaskExtension> scheduledTaskExtensions,
        IReadOnlyDictionary<string, IScheduledTimerTaskExtension> operationOwners,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(scheduledTaskExtensions);
        ArgumentNullException.ThrowIfNull(operationOwners);

        var defaultExtension = scheduledTaskExtensions[0];
        var defaultOperations = defaultExtension.SupportedOperations
            ?? throw new ArgumentException("Scheduled task extensions must expose a non-null SupportedOperations collection.", parameterName);

        foreach (var operation in defaultOperations)
        {
            if (operationOwners.ContainsKey(operation.Value))
            {
                return operation.Value;
            }
        }

        throw new ArgumentException("Default scheduled task extension must expose at least one valid operation.", parameterName);
    }

    private void StopLoop()
    {
        CancellationTokenSource? cts;
        Task? loopTask;

        lock (_lifecycleLock)
        {
            cts = _loopCts;
            loopTask = _loopTask;
            _loopCts = null;
            _loopTask = null;
        }

        if (cts is null)
        {
            return;
        }

        cts.Cancel();
        try
        {
            loopTask?.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            cts.Dispose();
        }
    }

}
