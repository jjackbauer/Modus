using Wip.Runtime.Runtime;
using Wip.Modus.Hosting;
using Wip.Shell.Interactive;

namespace Wip.ShellHost.Hosting;

public sealed class WipShellHostContainer : IAsyncDisposable
{
    public WipShellHostContainer(
        WipRuntimeOrchestrator orchestrator,
        IWipShellEngine shellEngine,
        IModusWipBridge bridge,
        WipShellPluginLifetimeGate? pluginLifetimeGate = null)
    {
        Orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        ShellEngine = shellEngine ?? throw new ArgumentNullException(nameof(shellEngine));
        Bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
        PluginLifetimeGate = pluginLifetimeGate ?? new WipShellPluginLifetimeGate();
    }

    public WipRuntimeOrchestrator Orchestrator { get; }

    public IWipShellEngine ShellEngine { get; }

    public IModusWipBridge Bridge { get; }

    public WipShellPluginLifetimeGate PluginLifetimeGate { get; }

    public async ValueTask DisposeAsync()
    {
        if (Bridge is IAsyncDisposable asyncDisposable)
            await asyncDisposable.DisposeAsync();
        else if (Bridge is IDisposable disposable)
            disposable.Dispose();
    }
}
