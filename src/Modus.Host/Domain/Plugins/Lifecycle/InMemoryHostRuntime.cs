using Modus.Host.Plugins.Descriptors;
using Modus.Host.Plugins.Host;
using Modus.Host.Plugins.Scanning;
using Modus.Host.Diagnostics;
using Modus.Host.Plugins.Validation;

namespace Modus.Host.Plugins.Lifecycle;

public sealed class InMemoryHostRuntime
{
    private readonly PluginDiscoveryService _discovery;
    private readonly PluginValidationService _validation;
    private readonly PluginIsolationBoundary _isolationBoundary;

    public InMemoryHostRuntime()
        : this(
            new PluginDiscoveryService(),
            new PluginValidationService(),
            new PluginIsolationBoundary(new PluginFailureReporter()))
    {
    }

    internal InMemoryHostRuntime(PluginDiscoveryService discovery, PluginValidationService validation)
        : this(
            discovery,
            validation,
            new PluginIsolationBoundary(new PluginFailureReporter()))
    {
    }

    internal InMemoryHostRuntime(
        PluginDiscoveryService discovery,
        PluginValidationService validation,
        PluginIsolationBoundary isolationBoundary)
    {
        _discovery = discovery;
        _validation = validation;
        _isolationBoundary = isolationBoundary;
    }

    public HostStartResult Start(IEnumerable<PluginDescriptor> input)
    {
        var discovered = _discovery.Discover(input);
        var diagnostics = new List<string>();
        var failed = new HashSet<string>(StringComparer.Ordinal);
        var loadable = new List<PluginDescriptor>();
        var seenPluginIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var descriptor in discovered)
        {
            diagnostics.Add($"stage=discovery plugin={descriptor.PluginId} outcome=success");

            if (!seenPluginIds.Add(descriptor.PluginId))
            {
                diagnostics.Add($"duplicate plugin ignored: {descriptor.PluginId}");
                continue;
            }

            var validation = _validation.Validate(descriptor);
            if (!validation.IsValid)
            {
                _isolationBoundary.IsolateFailure(
                    pluginId: descriptor.PluginId,
                    failedStage: "validation",
                    failureReasons: [validation.FailureReason ?? "unspecified validation failure"],
                    failedPluginIds: failed,
                    diagnostics: diagnostics);
                continue;
            }

            diagnostics.Add($"stage=validation plugin={descriptor.PluginId} outcome=success");

            var load = InMemoryPluginLoader.Load(descriptor);
            if (!load.IsLoaded)
            {
                var failureReasons = load.Diagnostics.Count == 0
                    ? ["policy violation"]
                    : load.Diagnostics;

                _isolationBoundary.IsolateFailure(
                    pluginId: descriptor.PluginId,
                    failedStage: "load",
                    failureReasons: failureReasons,
                    failedPluginIds: failed,
                    diagnostics: diagnostics);

                continue;
            }

            diagnostics.Add($"stage=load plugin={descriptor.PluginId} outcome=success");

            loadable.Add(descriptor);
        }

        var activationOrder = TopologicalOrder(loadable);
        var activated = new List<string>();
        var activatedDescriptors = new List<PluginDescriptor>();

        foreach (var descriptor in activationOrder)
        {
            var unavailableDependencies = descriptor.DependsOn
                .Where(dependency => !activated.Contains(dependency, StringComparer.Ordinal))
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToArray();

            if (unavailableDependencies.Length > 0)
            {
                _isolationBoundary.IsolateFailure(
                    pluginId: descriptor.PluginId,
                    failedStage: "activation",
                    failureReasons: [$"dependency unavailable: {string.Join(", ", unavailableDependencies)}"],
                    failedPluginIds: failed,
                    diagnostics: diagnostics);
                continue;
            }

            var registrationTransaction = new RegistrationTransactionLog();
            diagnostics.Add($"stage=registration plugin={descriptor.PluginId} outcome=success");
            activatedDescriptors.Add(descriptor);
            registrationTransaction.Record(
                "registration",
                () => RemovePluginDescriptor(activatedDescriptors, descriptor.PluginId));

            activated.Add(descriptor.PluginId);
            registrationTransaction.Record(
                "activation",
                () => RemovePluginId(activated, descriptor.PluginId));

            if (descriptor.FailOnActivation)
            {
                _isolationBoundary.IsolateFailure(
                    pluginId: descriptor.PluginId,
                    failedStage: "activation",
                    failureReasons: ["activation exception"],
                    failedPluginIds: failed,
                    diagnostics: diagnostics,
                    transactionLog: registrationTransaction);
                continue;
            }

            diagnostics.Add($"stage=activation plugin={descriptor.PluginId} outcome=success");

            var operation = SelectDeterministicOperation(descriptor);
            if (descriptor.FailingOperations?.Contains(operation, StringComparer.Ordinal) == true)
            {
                _isolationBoundary.IsolateOperationFailure(
                    pluginId: descriptor.PluginId,
                    operation: operation,
                    reason: "operation exception",
                    failedPluginIds: failed,
                    diagnostics: diagnostics,
                    transactionLog: registrationTransaction);
                continue;
            }

            diagnostics.Add($"stage=operation plugin={descriptor.PluginId} operation={operation} outcome=success");
        }

        var activePluginIds = activated.ToHashSet(StringComparer.Ordinal);
        var capabilityOwnerCandidates = activatedDescriptors
            .Where(descriptor => activePluginIds.Contains(descriptor.PluginId))
            .ToList();

        var capabilityOwners = ResolveCapabilityOwners(capabilityOwnerCandidates);

        return new HostStartResult(
            Started: true,
            ActivatedPluginIds: activated,
            FailedPluginIds: failed.OrderBy(x => x, StringComparer.Ordinal).ToList(),
            CapabilityOwners: capabilityOwners.ToDictionary(x => x.Key, x => x.Value.PluginId, StringComparer.Ordinal),
            Diagnostics: diagnostics);
    }

    private static string SelectDeterministicOperation(PluginDescriptor descriptor)
    {
        var declared = descriptor.DeclaredOperations
            ?.Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .FirstOrDefault();

        return declared ?? $"Op.{descriptor.PluginId}.HealthCheck";
    }

    private static PluginDescriptor PickCapabilityOwner(PluginDescriptor currentOwner, PluginDescriptor challenger)
    {
        var versionComparison = challenger.Version.CompareTo(currentOwner.Version);
        if (versionComparison > 0)
        {
            return challenger;
        }

        if (versionComparison < 0)
        {
            return currentOwner;
        }

        return string.CompareOrdinal(challenger.PluginId, currentOwner.PluginId) < 0
            ? challenger
            : currentOwner;
    }

    private static Dictionary<string, PluginDescriptor> ResolveCapabilityOwners(IReadOnlyList<PluginDescriptor> descriptors)
    {
        var capabilityOwners = new Dictionary<string, PluginDescriptor>(StringComparer.Ordinal);

        foreach (var descriptor in descriptors)
        {
            foreach (var capability in descriptor.Capabilities)
            {
                if (!capabilityOwners.TryGetValue(capability, out var currentOwner))
                {
                    capabilityOwners[capability] = descriptor;
                    continue;
                }

                capabilityOwners[capability] = PickCapabilityOwner(currentOwner, descriptor);
            }
        }

        return capabilityOwners;
    }

    private static void RemovePluginId(List<string> activatedPluginIds, string pluginId)
    {
        var index = activatedPluginIds.FindIndex(x => string.Equals(x, pluginId, StringComparison.Ordinal));
        if (index >= 0)
        {
            activatedPluginIds.RemoveAt(index);
        }
    }

    private static void RemovePluginDescriptor(List<PluginDescriptor> activatedDescriptors, string pluginId)
    {
        var index = activatedDescriptors.FindIndex(x => string.Equals(x.PluginId, pluginId, StringComparison.Ordinal));
        if (index >= 0)
        {
            activatedDescriptors.RemoveAt(index);
        }
    }

    private static List<PluginDescriptor> TopologicalOrder(IReadOnlyList<PluginDescriptor> descriptors)
    {
        var byId = descriptors.ToDictionary(x => x.PluginId, StringComparer.Ordinal);
        var visited = new Dictionary<string, int>(StringComparer.Ordinal);
        var ordered = new List<PluginDescriptor>();

        foreach (var descriptor in descriptors.OrderBy(x => x.PluginId, StringComparer.Ordinal))
        {
            Visit(descriptor.PluginId);
        }

        return ordered;

        void Visit(string pluginId)
        {
            if (!byId.ContainsKey(pluginId))
            {
                return;
            }

            if (visited.TryGetValue(pluginId, out var state))
            {
                if (state == 2)
                {
                    return;
                }

                if (state == 1)
                {
                    return;
                }
            }

            visited[pluginId] = 1;
            var descriptor = byId[pluginId];
            foreach (var dependency in descriptor.DependsOn.OrderBy(x => x, StringComparer.Ordinal))
            {
                Visit(dependency);
            }

            visited[pluginId] = 2;
            ordered.Add(descriptor);
        }
    }
}
