using System.IO.Compression;
using Modus.Core.Plugins;
using Modus.Host.Domain.Hosting;
using Modus.Host.Plugins.Authorization;
using Modus.Host.Plugins.Scanning;
using Modus.Host.Plugins.Validation;
using Modus.Host.Plugins.Lifecycle;

namespace Modus.Host.Plugins.Uploads;

internal sealed class PluginUploadPipeline
{
    private readonly PluginUploadAuthorizationPipeline _authorization;
    private readonly PluginUploadOperationStore _operationStore;
    private readonly PluginLoader _pluginLoader;
    private readonly PluginValidationService _validationService;
    private readonly InMemoryHostRuntime _runtime;
    private readonly RuntimePluginRegistry _runtimePluginRegistry;
    private readonly HostStatusSnapshotBuilder _statusSnapshotBuilder;
    private readonly HostStatusRegistry _hostStatusRegistry;

    public PluginUploadPipeline(
        PluginUploadAuthorizationPipeline authorization,
        PluginUploadOperationStore operationStore,
        PluginLoader pluginLoader,
        PluginValidationService validationService,
        InMemoryHostRuntime runtime,
        RuntimePluginRegistry runtimePluginRegistry,
        HostStatusSnapshotBuilder statusSnapshotBuilder,
        HostStatusRegistry hostStatusRegistry)
    {
        _authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
        _operationStore = operationStore ?? throw new ArgumentNullException(nameof(operationStore));
        _pluginLoader = pluginLoader ?? throw new ArgumentNullException(nameof(pluginLoader));
        _validationService = validationService ?? throw new ArgumentNullException(nameof(validationService));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _runtimePluginRegistry = runtimePluginRegistry ?? throw new ArgumentNullException(nameof(runtimePluginRegistry));
        _statusSnapshotBuilder = statusSnapshotBuilder ?? throw new ArgumentNullException(nameof(statusSnapshotBuilder));
        _hostStatusRegistry = hostStatusRegistry ?? throw new ArgumentNullException(nameof(hostStatusRegistry));
    }

    public void StartUpload(Guid operationId, string packageName, byte[] packageBytes, byte[] signatureBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageName);
        ArgumentNullException.ThrowIfNull(packageBytes);
        ArgumentNullException.ThrowIfNull(signatureBytes);

        _ = Task.Run(() => ProcessUploadAsync(operationId, packageName, packageBytes, signatureBytes));
    }

    private Task ProcessUploadAsync(Guid operationId, string packageName, byte[] packageBytes, byte[] signatureBytes)
    {
        string? extractionRoot = null;

        try
        {
            _operationStore.MarkStage(operationId, PluginUploadOperationStage.Authorizing, 10, "stage=authorization outcome=running");

            var verificationRequest = new PluginUploadSignatureVerificationRequest(packageName, packageBytes, signatureBytes);
            var authorizationResult = _authorization.VerifyPluginUploadSignature(verificationRequest);
            if (!authorizationResult.IsAuthorized)
            {
                FailOperation(
                    operationId,
                    authorizationResult.FailureReason ?? "Plugin upload authorization failed.",
                    ["stage=authorization outcome=failure"]);
                return Task.CompletedTask;
            }

            _operationStore.MarkStage(operationId, PluginUploadOperationStage.Extracting, 30, "stage=extract outcome=running");
            extractionRoot = Path.Combine(Path.GetTempPath(), $"modus-upload-{operationId:N}");
            Directory.CreateDirectory(extractionRoot);
            using (var archiveStream = new MemoryStream(packageBytes, writable: false))
            using (var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read, leaveOpen: false))
            {
                archive.ExtractToDirectory(extractionRoot, overwriteFiles: true);
            }

            _operationStore.MarkStage(operationId, PluginUploadOperationStage.Validating, 55, "stage=validation outcome=running");
            var scan = _pluginLoader.ScanRuntimeAssemblies(extractionRoot);
            if (scan.Descriptors.Count == 0)
            {
                FailOperation(
                    operationId,
                    "No plugin assemblies were found in upload package.",
                    [
                        .. scan.Diagnostics,
                        "stage=validation outcome=failure reason=no-plugin-assemblies"
                    ]);
                return Task.CompletedTask;
            }

            foreach (var descriptor in scan.Descriptors)
            {
                var validation = _validationService.Validate(descriptor);
                if (!validation.IsValid)
                {
                    FailOperation(
                        operationId,
                        validation.FailureReason ?? "Plugin validation failed.",
                        [
                            .. scan.Diagnostics,
                            $"stage=validation plugin={descriptor.PluginId} outcome=failure",
                            $"stage=validation plugin={descriptor.PluginId} outcome=failure reason={FormatDiagnosticToken(validation.FailureReason)}"
                        ]);
                    return Task.CompletedTask;
                }
            }

            _operationStore.MarkStage(operationId, PluginUploadOperationStage.Loading, 75, "stage=load outcome=running");
            var runtimeStart = _runtime.Start(scan.Descriptors);
            if (runtimeStart.ActivatedPluginIds.Count == 0)
            {
                FailOperation(
                    operationId,
                    "No plugin assemblies were activated from upload package.",
                    [.. scan.Diagnostics, .. runtimeStart.Diagnostics]);
                return Task.CompletedTask;
            }

            _operationStore.MarkStage(operationId, PluginUploadOperationStage.Running, 90, "stage=run outcome=running");
            var registryDiagnostics = PublishRuntimeRegistrySnapshot(scan.Descriptors, runtimeStart.ActivatedPluginIds);
            foreach (var registryDiagnostic in registryDiagnostics)
            {
                _operationStore.MarkStage(operationId, PluginUploadOperationStage.Running, 95, registryDiagnostic);
            }

            var statusSnapshot = _statusSnapshotBuilder.Build(
                hostHealthy: runtimeStart.Started,
                descriptors: scan.Descriptors,
                activatedPluginIds: runtimeStart.ActivatedPluginIds,
                failedPluginIds: runtimeStart.FailedPluginIds,
                capabilityOwners: runtimeStart.CapabilityOwners);

            _hostStatusRegistry.Update(
                statusSnapshot,
                [
                    .. scan.Diagnostics,
                    .. runtimeStart.Diagnostics,
                    .. registryDiagnostics,
                ]);

            _operationStore.Complete(
                operationId,
                [
                    .. scan.Diagnostics,
                    .. runtimeStart.Diagnostics,
                    $"stage=run outcome=success activated={string.Join(',', runtimeStart.ActivatedPluginIds)}",
                ]);
        }
        catch (Exception ex)
        {
            FailOperation(
                operationId,
                $"Plugin upload pipeline failed: {ex.Message}",
                ["stage=pipeline outcome=failure"]);
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(extractionRoot) && Directory.Exists(extractionRoot))
            {
                try
                {
                    Directory.Delete(extractionRoot, recursive: true);
                }
                catch
                {
                }
            }
        }

        return Task.CompletedTask;
    }

    private void FailOperation(Guid operationId, string failureReason, IReadOnlyCollection<string> diagnostics)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureReason);
        ArgumentNullException.ThrowIfNull(diagnostics);

        var normalizedDiagnostics = diagnostics
            .Where(static diagnostic => !string.IsNullOrWhiteSpace(diagnostic))
            .ToArray();

        _operationStore.Fail(operationId, failureReason, normalizedDiagnostics);

        if (normalizedDiagnostics.Length > 0)
        {
            _hostStatusRegistry.AppendDiagnostics(normalizedDiagnostics);
        }
    }

    private static string FormatDiagnosticToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unspecified";
        }

        return value.Trim().Replace(' ', '-').ToLowerInvariant();
    }

    private IReadOnlyList<string> PublishRuntimeRegistrySnapshot(
        IReadOnlyCollection<PluginDescriptor> descriptors,
        IReadOnlyCollection<string> activatedPluginIds)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        ArgumentNullException.ThrowIfNull(activatedPluginIds);

        var activatedSet = activatedPluginIds.ToHashSet(StringComparer.Ordinal);
        var projections = descriptors
            .Where(descriptor => activatedSet.Contains(descriptor.PluginId.Value))
            .Select(RuntimeRegistryPluginProjection.FromDescriptor)
            .OfType<RuntimeRegistryPluginProjection>()
            .OrderBy(static projection => projection.PluginId.Value, StringComparer.Ordinal)
            .ToArray();

        var snapshot = _runtimePluginRegistry.GetSnapshot();
        var projectionPluginIds = projections
            .Select(static projection => projection.PluginId.Value)
            .ToHashSet(StringComparer.Ordinal);

        var baselineContracts = snapshot.Contracts
            .Where(contract => !projectionPluginIds.Contains(contract.PluginId.Value))
            .ToArray();
        var baselineCatalogs = snapshot.Catalogs
            .Where(catalog => catalog is not IPluginContract contract || !projectionPluginIds.Contains(contract.PluginId.Value))
            .ToArray();

        var updatedContracts = baselineContracts.Concat<IPluginContract>(projections).ToArray();
        var updatedCatalogs = baselineCatalogs.Concat<IPluginOperationCatalog>(projections).ToArray();

        _runtimePluginRegistry.Update(updatedContracts, updatedCatalogs);

        var previousPluginIds = snapshot.Contracts
            .Select(static contract => contract.PluginId.Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static id => id, StringComparer.Ordinal)
            .ToArray();
        var currentPluginIds = updatedContracts
            .Select(static contract => contract.PluginId.Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static id => id, StringComparer.Ordinal)
            .ToArray();

        var addedPluginIds = currentPluginIds.Except(previousPluginIds, StringComparer.Ordinal).ToArray();
        var removedPluginIds = previousPluginIds.Except(currentPluginIds, StringComparer.Ordinal).ToArray();

        return
        [
            $"stage=registry-update outcome=success added={string.Join(',', addedPluginIds)} removed={string.Join(',', removedPluginIds)} total={currentPluginIds.Length}",
        ];
    }

    private sealed class RuntimeRegistryPluginProjection : IRuntimePluginDispatchTarget
    {
        private RuntimeRegistryPluginProjection(
            PluginId pluginId,
            ContractName contractName,
            Version contractVersion,
            IReadOnlyCollection<OperationName> supportedOperations,
            string? pluginTypeFullName,
            PluginServiceLifetime? serviceLifetime)
        {
            PluginId = pluginId;
            ContractName = contractName;
            ContractVersion = contractVersion;
            SupportedOperations = supportedOperations;
            PluginTypeFullName = pluginTypeFullName;
            ServiceLifetime = serviceLifetime;
        }

        public PluginId PluginId { get; }

        public ContractName ContractName { get; }

        public Version ContractVersion { get; }

        public IReadOnlyCollection<OperationName> SupportedOperations { get; }

        public string? PluginTypeFullName { get; }

        public PluginServiceLifetime? ServiceLifetime { get; }

        public static RuntimeRegistryPluginProjection? FromDescriptor(PluginDescriptor descriptor)
        {
            var operations = (descriptor.DeclaredOperations ?? Array.Empty<OperationName>())
                .Where(static operation => !string.IsNullOrWhiteSpace(operation.Value))
                .DistinctBy(static operation => operation.Value, StringComparer.Ordinal)
                .OrderBy(static operation => operation.Value, StringComparer.Ordinal)
                .ToArray();

            if (operations.Length == 0)
            {
                operations = TryResolveOperationsFromRuntimeType(descriptor.RuntimePluginTypeFullName)
                    ?? [new OperationName($"Op.{descriptor.PluginId.Value}.HealthCheck")];
            }

            return new RuntimeRegistryPluginProjection(
                pluginId: descriptor.PluginId,
                contractName: new ContractName(descriptor.AssemblyName),
                contractVersion: descriptor.Version,
                supportedOperations: operations,
                pluginTypeFullName: descriptor.RuntimePluginTypeFullName,
                serviceLifetime: descriptor.DeclaredServiceLifetime);
        }

        private static OperationName[]? TryResolveOperationsFromRuntimeType(string? runtimePluginTypeFullName)
        {
            if (string.IsNullOrWhiteSpace(runtimePluginTypeFullName))
            {
                return null;
            }

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var pluginType = assembly.GetType(runtimePluginTypeFullName, throwOnError: false, ignoreCase: false);
                if (pluginType is null || !typeof(IPluginOperationCatalog).IsAssignableFrom(pluginType))
                {
                    continue;
                }

                try
                {
                    if (Activator.CreateInstance(pluginType) is not IPluginOperationCatalog catalog)
                    {
                        return null;
                    }

                    var resolved = catalog.SupportedOperations
                        .Where(static operation => !string.IsNullOrWhiteSpace(operation.Value))
                        .DistinctBy(static operation => operation.Value, StringComparer.Ordinal)
                        .OrderBy(static operation => operation.Value, StringComparer.Ordinal)
                        .ToArray();

                    return resolved.Length > 0 ? resolved : null;
                }
                catch
                {
                    return null;
                }
            }

            return null;
        }
    }
}
