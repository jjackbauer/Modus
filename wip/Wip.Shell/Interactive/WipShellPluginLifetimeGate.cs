namespace Wip.Shell.Interactive;

public sealed class WipShellPluginLifetimeGate
{
    private int _loadReserved;

    public bool HasReservedLoadInContainerLifetime
        => Volatile.Read(ref _loadReserved) == 1;

    public bool TryReserveLoad()
        => Interlocked.CompareExchange(ref _loadReserved, 1, 0) == 0;
}