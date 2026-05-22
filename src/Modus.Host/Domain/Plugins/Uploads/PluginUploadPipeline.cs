using System.IO.Compression;
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

    public PluginUploadPipeline(
        PluginUploadAuthorizationPipeline authorization,
        PluginUploadOperationStore operationStore,
        PluginLoader pluginLoader,
        PluginValidationService validationService,
        InMemoryHostRuntime runtime)
    {
        _authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
        _operationStore = operationStore ?? throw new ArgumentNullException(nameof(operationStore));
        _pluginLoader = pluginLoader ?? throw new ArgumentNullException(nameof(pluginLoader));
        _validationService = validationService ?? throw new ArgumentNullException(nameof(validationService));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
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
                _operationStore.Fail(
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

            _operationStore.MarkStage(operationId, PluginUploadOperationStage.Validating, 55, "stage=validate outcome=running");
            var scan = _pluginLoader.ScanRuntimeAssemblies(extractionRoot);
            if (scan.Descriptors.Count == 0)
            {
                _operationStore.Fail(
                    operationId,
                    "No plugin assemblies were found in upload package.",
                    scan.Diagnostics);
                return Task.CompletedTask;
            }

            foreach (var descriptor in scan.Descriptors)
            {
                var validation = _validationService.Validate(descriptor);
                if (!validation.IsValid)
                {
                    _operationStore.Fail(
                        operationId,
                        validation.FailureReason ?? "Plugin validation failed.",
                        [.. scan.Diagnostics, $"stage=validation plugin={descriptor.PluginId} outcome=failure"]);
                    return Task.CompletedTask;
                }
            }

            _operationStore.MarkStage(operationId, PluginUploadOperationStage.Loading, 75, "stage=load outcome=running");
            var runtimeStart = _runtime.Start(scan.Descriptors);
            if (runtimeStart.ActivatedPluginIds.Count == 0)
            {
                _operationStore.Fail(
                    operationId,
                    "No plugin assemblies were activated from upload package.",
                    [.. scan.Diagnostics, .. runtimeStart.Diagnostics]);
                return Task.CompletedTask;
            }

            _operationStore.MarkStage(operationId, PluginUploadOperationStage.Running, 90, "stage=run outcome=running");
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
            _operationStore.Fail(
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
}
