using Modus.Core.Hosting;
using Modus.Host.Domain.WebApi;

namespace Modus.Host.Domain.Hosting;

internal sealed class HostStatusRegistry
{
    private readonly object _gate = new();
    private HostStatusSnapshot _snapshot = new(
        State: HostRuntimeState.Failed,
        LoadedPlugins: [],
        CapabilityOwnership: []);
    private IReadOnlyList<string> _diagnostics = [];

    public void Update(HostStatusSnapshot? snapshot, IReadOnlyCollection<string>? diagnostics)
    {
        var normalizedSnapshot = snapshot ?? new HostStatusSnapshot(
            State: HostRuntimeState.Failed,
            LoadedPlugins: [],
            CapabilityOwnership: []);
        var normalizedDiagnostics = diagnostics?.ToArray() ?? [];

        lock (_gate)
        {
            _snapshot = normalizedSnapshot;
            _diagnostics = normalizedDiagnostics;
        }
    }

    public void AppendDiagnostics(IEnumerable<string> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        var normalizedDiagnostics = diagnostics
            .Where(static diagnostic => !string.IsNullOrWhiteSpace(diagnostic))
            .ToArray();

        if (normalizedDiagnostics.Length == 0)
        {
            return;
        }

        lock (_gate)
        {
            _diagnostics = [.. _diagnostics, .. normalizedDiagnostics];
        }
    }

    public ManagementStatusEndpointResponse GetCurrent()
    {
        lock (_gate)
        {
            return ManagementStatusEndpointResponse.FromSnapshot(_snapshot, _diagnostics);
        }
    }
}