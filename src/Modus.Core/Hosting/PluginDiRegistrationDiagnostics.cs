using Microsoft.Extensions.DependencyInjection;

namespace Modus.Core.Hosting;

public enum PluginRegistrationOutcome
{
    Success,
    Skipped,
    Failure
}

public sealed record PluginDiRegistrationEntry(
    string RegisterTypeName,
    string PluginId,
    PluginRegistrationOutcome Outcome,
    ServiceLifetime? SelectedLifetime,
    string? Reason);

public sealed class PluginDiRegistrationDiagnostics
{
    private readonly List<PluginDiRegistrationEntry> _entries = [];

    public IReadOnlyList<PluginDiRegistrationEntry> Entries => _entries;

    internal void RecordSuccess(string registerTypeName, string pluginId, ServiceLifetime? selectedLifetime)
        => _entries.Add(new(registerTypeName, pluginId, PluginRegistrationOutcome.Success, selectedLifetime, null));

    internal void RecordSkipped(string registerTypeName, string pluginId, string reason)
        => _entries.Add(new(registerTypeName, pluginId, PluginRegistrationOutcome.Skipped, null, reason));

    internal void RecordFailure(string registerTypeName, string pluginId, string reason)
        => _entries.Add(new(registerTypeName, pluginId, PluginRegistrationOutcome.Failure, null, reason));
}
