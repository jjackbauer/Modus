using Wip.Shell.Interactive;

namespace Wip.ShellHost.Hosting;

public interface IWipShellEngine
{
    Task<int> LoopAsync(CancellationToken cancellationToken);
}

public sealed class WipShellEngine : IWipShellEngine
{
    private readonly WipShellCommandLoop _commandLoop;

    public WipShellEngine(WipShellCommandLoop commandLoop)
    {
        _commandLoop = commandLoop ?? throw new ArgumentNullException(nameof(commandLoop));
    }

    public Task<int> LoopAsync(CancellationToken cancellationToken)
        => _commandLoop.RunAsync(cancellationToken);
}
