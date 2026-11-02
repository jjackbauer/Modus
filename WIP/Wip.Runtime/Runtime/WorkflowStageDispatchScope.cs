namespace Wip.Runtime.Runtime;

public static class WorkflowStageDispatchScope
{
    private static readonly AsyncLocal<IReadOnlySet<WorkflowStageKind>?> DispatchableStages = new();

    private static readonly HashSet<WorkflowStageKind> DefaultDispatchableStages =
    [
        WorkflowStageKind.Plan,
        WorkflowStageKind.Run
    ];

    public static IDisposable Exclude(params WorkflowStageKind[] excludedStages)
    {
        ArgumentNullException.ThrowIfNull(excludedStages);

        var dispatchableStages = new HashSet<WorkflowStageKind>(DefaultDispatchableStages);
        foreach (var excludedStage in excludedStages)
        {
            dispatchableStages.Remove(excludedStage);
        }

        return Begin(dispatchableStages);
    }

    public static IDisposable Begin(IReadOnlySet<WorkflowStageKind> dispatchableStages)
    {
        ArgumentNullException.ThrowIfNull(dispatchableStages);

        var previous = DispatchableStages.Value;
        DispatchableStages.Value = dispatchableStages;
        return new Scope(previous);
    }

    public static bool IsDispatchable(WorkflowStageKind stage)
        => DispatchableStages.Value?.Contains(stage) ?? DefaultDispatchableStages.Contains(stage);

    private sealed class Scope(IReadOnlySet<WorkflowStageKind>? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            DispatchableStages.Value = previous;
            _disposed = true;
        }
    }
}