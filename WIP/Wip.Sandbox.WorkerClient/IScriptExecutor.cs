using Wip.Sandbox.Protocol;

namespace Wip.Sandbox.WorkerClient;

/// <summary>Abstraction for a strategy executor; the worker client is the OS-contained implementation.</summary>
public interface IScriptExecutor : IAsyncDisposable
{
    bool IsRunning { get; }

    Task HandshakeAsync(CancellationToken cancellationToken = default);

    Task CompileAsync(string strategyId, string source, CancellationToken cancellationToken = default);

    Task<int> ChooseMoveAsync(string strategyId, WorkerStrategyState state, CancellationToken cancellationToken = default);
}
