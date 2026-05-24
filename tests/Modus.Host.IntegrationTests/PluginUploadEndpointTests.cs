using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Modus.Core.Messaging;
using Modus.Core.Plugins;
using Modus.Host.Domain.Hosting;
using Modus.Host.Domain.WebApi;
using Modus.Host.Hosting;
using Modus.Host.Plugins.Authorization;
using Modus.Host.Plugins.Uploads;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace Modus.Host.IntegrationTests;

public sealed class PluginUploadEndpointTests
{
    [Fact]
    [Trait("ChecklistItem", "Verify diagnostics include deterministic stage/outcome tokens for discovery, validation, load, activation, run, unload, and registry-update [mandatory - deterministic assertions]")]
    [Trait("AuditArtifact", "iterative-implementation-diagnostics-stage-outcome-determinism-2026-05-22")]
    public async Task RuntimeLifecycleDiagnostics_GivenHotLoadRunAndUnloadFlows_ExpectedDeterministicStageOutcomeTokensForMandatoryLifecycleStages()
    {
        var root = Path.Combine(Path.GetTempPath(), $"modus-diagnostics-stage-token-{Guid.NewGuid():N}");
        var pluginsPath = Path.Combine(root, "plugins");
        Directory.CreateDirectory(pluginsPath);

        try
        {
            var packageBytes = CreateArchiveWithPluginAssembly();
            using var key = RSA.Create(2048);
            var signatureBytes = key.SignData(packageBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

            var options = new PluginUploadAuthorizationOptions();
            options.TrustedAuthorKeys.Add(new TrustedPluginAuthorKey("trusted-author", key.ExportRSAPublicKeyPem()));

            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddSingleton(options);
            builder.Services.AddModusPluginHostingRuntime();

            await using var app = builder.Build();
            app.Services.GetRequiredService<ManagementPluginUploadsEndpointMapper>().Map(app);
            await app.StartAsync();

            var watcher = app.Services.GetRequiredService<PluginFolderWatcher>();
            watcher.Start(pluginsPath);

            var projectPath = Path.Combine(pluginsPath, "Plugin.Runtime.Diagnostics.csproj");
            File.WriteAllText(
                projectPath,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><ModusOperations>Orders.Diagnostics</ModusOperations></PropertyGroup></Project>");

            var onboarding = watcher.OnProjectCreated(projectPath);
            Assert.True(onboarding.HostHealthy);
            Assert.True(onboarding.EventAccepted);
            Assert.True(onboarding.PluginActivated);

            AssertStageOutcomeOrder(
                onboarding.Diagnostics,
                [
                    "stage=discovery sequence=0001 outcome=accepted",
                    "stage=validation plugin=Plugin.Runtime.Diagnostics outcome=success",
                    "stage=load plugin=Plugin.Runtime.Diagnostics outcome=success",
                    "stage=activation plugin=Plugin.Runtime.Diagnostics outcome=success",
                    "stage=registry-update outcome=success",
                ]);

            File.Delete(projectPath);
            var offboarding = watcher.OnProjectDeleted(projectPath);
            Assert.True(offboarding.HostHealthy);
            Assert.True(offboarding.EventAccepted);

            AssertStageOutcomeOrder(
                offboarding.Diagnostics,
                [
                    "stage=unload sequence=0002 plugin=Plugin.Runtime.Diagnostics outcome=success",
                    "stage=registry-update outcome=success",
                ]);

            var client = app.GetTestClient();
            using var uploadContent = BuildUploadRequest(packageBytes, signatureBytes);
            var uploadResponse = await client.PostAsync("/management/plugins/uploads", uploadContent);
            var uploadPayload = await uploadResponse.Content.ReadFromJsonAsync<JsonElement>();

            Assert.Equal(HttpStatusCode.Accepted, uploadResponse.StatusCode);
            Assert.True(uploadPayload.TryGetProperty("operationId", out var operationIdElement));
            Assert.True(Guid.TryParse(operationIdElement.GetString(), out var operationId));

            var store = app.Services.GetRequiredService<PluginUploadOperationStore>();
            var operation = await WaitForTerminalStatusAsync(store, operationId);
            Assert.True(operation.IsSuccess);
            Assert.Equal(PluginUploadOperationStage.Completed, operation.Stage);

            AssertStageOutcomeOrder(
                operation.Diagnostics,
                [
                    "stage=validation outcome=running",
                    "stage=run outcome=running",
                    "stage=registry-update outcome=success",
                    "stage=run outcome=success",
                ]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    [Trait("ChecklistItem", "Verify successful `POST /management/plugins/uploads` integration flow mutates runtime registry with uploaded plugin metadata before status reaches Completed [mandatory - upload hot-load assurance]")]
    [Trait("AuditArtifact", "iterative-implementation-upload-registry-mutation-before-completed-2026-05-22")]
    public async Task StartPluginUpload_GivenValidSignedPackage_ExpectedRegistryMutationBeforeCompletedStatus()
    {
        var packageBytes = CreateArchiveWithPluginAssembly();
        using var key = RSA.Create(2048);
        var signatureBytes = key.SignData(packageBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        var options = new PluginUploadAuthorizationOptions();
        options.TrustedAuthorKeys.Add(new TrustedPluginAuthorKey("trusted-author", key.ExportRSAPublicKeyPem()));

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(options);
        builder.Services.AddModusPluginHostingRuntime();

        await using var app = builder.Build();
        app.Services.GetRequiredService<ManagementPluginUploadsEndpointMapper>().Map(app);
        await app.StartAsync();

        var client = app.GetTestClient();
        using var content = BuildUploadRequest(packageBytes, signatureBytes);
        var httpResponse = await client.PostAsync("/management/plugins/uploads", content);
        var payload = await httpResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.Accepted, httpResponse.StatusCode);
        Assert.True(payload.TryGetProperty("operationId", out var operationIdElement));
        Assert.True(Guid.TryParse(operationIdElement.GetString(), out var operationId));

        var store = app.Services.GetRequiredService<PluginUploadOperationStore>();
        var registry = app.Services.GetRequiredService<RuntimePluginRegistry>();
        var operation = await WaitForTerminalStatusAsync(store, operationId);
        Assert.True(operation.IsSuccess);
        Assert.Equal(PluginUploadOperationStage.Completed, operation.Stage);
        Assert.Null(operation.FailureReason);

        var activatedPluginId = ExtractActivatedPluginId(operation.Diagnostics);
        Assert.False(string.IsNullOrWhiteSpace(activatedPluginId));

        var snapshot = registry.GetSnapshot();
        Assert.Contains(snapshot.Contracts, contract => string.Equals(contract.PluginId.Value, activatedPluginId, StringComparison.Ordinal));
        Assert.Contains(
            snapshot.Catalogs,
            catalog => catalog is IPluginContract contract
            && string.Equals(contract.PluginId.Value, activatedPluginId, StringComparison.Ordinal)
            && catalog.SupportedOperations.Count > 0);

        var diagnostics = operation.Diagnostics.ToArray();
        var runRunningIndex = Array.FindIndex(diagnostics, diagnostic => diagnostic.Contains("stage=run outcome=running", StringComparison.Ordinal));
        var registryUpdateIndex = Array.FindIndex(diagnostics, diagnostic => diagnostic.Contains("stage=registry-update outcome=success", StringComparison.Ordinal));
        var runSuccessIndex = Array.FindIndex(diagnostics, diagnostic => diagnostic.Contains("stage=run outcome=success", StringComparison.Ordinal));
        Assert.True(runRunningIndex >= 0);
        Assert.True(registryUpdateIndex >= 0);
        Assert.True(runSuccessIndex >= 0);
        Assert.True(runRunningIndex < registryUpdateIndex);
        Assert.True(registryUpdateIndex < runSuccessIndex);
    }

    [Fact]
    [Trait("ChecklistItem", "Verify successful upload integration flow makes uploaded plugin operation callable through `/api/{pluginId}/{operation}` with correlation propagation [mandatory - observable outcomes]")]
    [Trait("AuditArtifact", "iterative-implementation-upload-callability-correlation-2026-05-22")]
    public async Task StartPluginUpload_GivenValidSignedPackage_ExpectedUploadedOperationDispatchWithCorrelationAndPayloadContract()
    {
        var packageBytes = CreateArchiveWithPluginAssembly();
        using var key = RSA.Create(2048);
        var signatureBytes = key.SignData(packageBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        var options = new PluginUploadAuthorizationOptions();
        options.TrustedAuthorKeys.Add(new TrustedPluginAuthorKey("trusted-author", key.ExportRSAPublicKeyPem()));

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(options);
        builder.Services.AddModusPluginHostingRuntime();

        await using var app = builder.Build();
        app.Services.GetRequiredService<ManagementPluginUploadsEndpointMapper>().Map(app);
        app.Services.GetRequiredService<PluginEndpointMapper>().Map(app);
        await app.StartAsync();

        var client = app.GetTestClient();
        using var uploadContent = BuildUploadRequest(packageBytes, signatureBytes);
        var uploadHttpResponse = await client.PostAsync("/management/plugins/uploads", uploadContent);
        var uploadPayload = await uploadHttpResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.Accepted, uploadHttpResponse.StatusCode);
        Assert.True(uploadPayload.TryGetProperty("operationId", out var operationIdElement));
        Assert.True(Guid.TryParse(operationIdElement.GetString(), out var operationId));

        var store = app.Services.GetRequiredService<PluginUploadOperationStore>();
        var registry = app.Services.GetRequiredService<RuntimePluginRegistry>();
        var uploadOperation = await WaitForTerminalStatusAsync(store, operationId);

        Assert.True(uploadOperation.IsSuccess);
        Assert.Equal(PluginUploadOperationStage.Completed, uploadOperation.Stage);

        var activatedPluginId = ExtractActivatedPluginId(uploadOperation.Diagnostics);
        Assert.False(string.IsNullOrWhiteSpace(activatedPluginId));

        var snapshot = registry.GetSnapshot();
        var uploadedCatalog = snapshot.Catalogs
            .OfType<IPluginContract>()
            .Where(contract => string.Equals(contract.PluginId.Value, activatedPluginId, StringComparison.Ordinal))
            .OfType<IPluginOperationCatalog>()
            .Single();
        var operationNames = uploadedCatalog.SupportedOperations
            .Select(static operation => operation.Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();
        Assert.NotEmpty(operationNames);

        var correlationId = $"upload-dispatch-{Guid.NewGuid():N}";
        HttpResponseMessage? dispatchHttpResponse = null;
        PluginOperationHttpResponse? dispatchResponse = null;
        string? executedOperationName = null;

        foreach (var candidateOperation in operationNames)
        {
            dispatchHttpResponse = await client.PostAsJsonAsync(
                $"/api/{activatedPluginId}/{candidateOperation}",
                new PluginOperationHttpRequest
                {
                    CorrelationId = correlationId,
                    Payload = "{}"
                });

            dispatchResponse = await dispatchHttpResponse.Content.ReadFromJsonAsync<PluginOperationHttpResponse>();
            if (dispatchHttpResponse.StatusCode == HttpStatusCode.OK
                && dispatchResponse?.Status == SyncResponseStatus.Success
                && dispatchResponse.Success)
            {
                executedOperationName = candidateOperation;
                break;
            }
        }

        Assert.NotNull(dispatchHttpResponse);
        Assert.NotNull(dispatchResponse);
        Assert.False(string.IsNullOrWhiteSpace(executedOperationName));

        Assert.Equal(HttpStatusCode.OK, dispatchHttpResponse!.StatusCode);
        Assert.True(dispatchResponse!.Success);
        Assert.Equal(SyncResponseStatus.Success, dispatchResponse.Status);
        Assert.Equal(correlationId, dispatchResponse.CorrelationId);

        var payloadText = PluginOperationPayload.AsRawText(dispatchResponse.Payload);
        var payloadObject = PluginOperationPayload.AsJsonElement(dispatchResponse.Payload);
        Assert.Equal(JsonValueKind.Object, payloadObject.ValueKind);
        Assert.Contains(activatedPluginId, payloadText, StringComparison.Ordinal);
        Assert.Contains(executedOperationName, payloadText, StringComparison.Ordinal);
        Assert.Contains("measurements", payloadText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("ChecklistItem", "Verify failed upload integration flow preserves pre-upload runtime registry snapshot and prevents dispatch of uploaded operation [mandatory - negative-path isolation]")]
    [Trait("AuditArtifact", "iterative-implementation-upload-failure-negative-isolation-2026-05-22")]
    public async Task StartPluginUpload_GivenArchiveWithoutAssemblies_ExpectedRegistrySnapshotUnchangedAndUploadedOperationNonDispatchable()
    {
        var packageBytes = CreateArchiveWithoutAssemblies();
        using var key = RSA.Create(2048);
        var signatureBytes = key.SignData(packageBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        var options = new PluginUploadAuthorizationOptions();
        options.TrustedAuthorKeys.Add(new TrustedPluginAuthorKey("trusted-author", key.ExportRSAPublicKeyPem()));

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(options);
        builder.Services.AddModusPluginHostingRuntime();

        await using var app = builder.Build();
        app.Services.GetRequiredService<ManagementPluginUploadsEndpointMapper>().Map(app);
    app.Services.GetRequiredService<PluginEndpointMapper>().Map(app);
        await app.StartAsync();

    var registry = app.Services.GetRequiredService<RuntimePluginRegistry>();
    var preUploadSnapshot = registry.GetSnapshot();
    var preUploadContracts = BuildContractSnapshotSignature(preUploadSnapshot.Contracts);
    var preUploadCatalogs = BuildCatalogSnapshotSignature(preUploadSnapshot.Catalogs);

        var client = app.GetTestClient();
        using var content = BuildUploadRequest(packageBytes, signatureBytes);
        var httpResponse = await client.PostAsync("/management/plugins/uploads", content);
        var payload = await httpResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.Accepted, httpResponse.StatusCode);
        Assert.True(payload.TryGetProperty("operationId", out var operationIdElement));
        Assert.True(Guid.TryParse(operationIdElement.GetString(), out var operationId));

        var store = app.Services.GetRequiredService<PluginUploadOperationStore>();
        var operation = await WaitForTerminalStatusAsync(store, operationId);
        Assert.False(operation.IsSuccess);
        Assert.Equal(PluginUploadOperationStage.Failed, operation.Stage);
        Assert.Equal("No plugin assemblies were found in upload package.", operation.FailureReason);

        var postUploadSnapshot = registry.GetSnapshot();
        var postUploadContracts = BuildContractSnapshotSignature(postUploadSnapshot.Contracts);
        var postUploadCatalogs = BuildCatalogSnapshotSignature(postUploadSnapshot.Catalogs);
        Assert.Equal(preUploadContracts, postUploadContracts);
        Assert.Equal(preUploadCatalogs, postUploadCatalogs);

        const string attemptedUploadedPluginId = "Plugin.Host.Telemetry";
        const string attemptedUploadedOperation = "Telemetry.Host.CollectSnapshot";
        var correlationId = $"failed-upload-dispatch-{Guid.NewGuid():N}";

        var dispatchHttpResponse = await client.PostAsJsonAsync(
            $"/api/{attemptedUploadedPluginId}/{attemptedUploadedOperation}",
            new PluginOperationHttpRequest
            {
                CorrelationId = correlationId,
                Payload = "{}"
            });
        var dispatchResponse = await dispatchHttpResponse.Content.ReadFromJsonAsync<PluginOperationHttpResponse>();

        Assert.Equal(HttpStatusCode.InternalServerError, dispatchHttpResponse.StatusCode);
        Assert.NotNull(dispatchResponse);
        Assert.False(dispatchResponse!.Success);
        Assert.Equal(SyncResponseStatus.Failed, dispatchResponse.Status);
        Assert.Equal(correlationId, dispatchResponse.CorrelationId);
        Assert.True(PluginOperationPayload.Contains(dispatchResponse.Payload, "No runtime plugin operation owner found", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("ChecklistItem", "Verify `GET /management/plugins/uploads/{operationId}` integration polling reports deterministic monotonic stage/progress transitions through terminal state [depends on async upload pipeline]")]
    [Trait("ChecklistItem", "Prove management API runtime contracts remain stable after OpenAPI mapping refactor [depends on management endpoint integration behavior proof]")]
    [Trait("AuditArtifact", "iterative-implementation-upload-status-polling-monotonic-2026-05-22")]
    public async Task GetPluginUploadOperationStatus_GivenAsyncUploadPipeline_ExpectedMonotonicPollingTransitionsThroughTerminalState()
    {
        var packageBytes = CreateArchiveWithPluginAssembly();
        using var key = RSA.Create(2048);
        var signatureBytes = key.SignData(packageBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        var options = new PluginUploadAuthorizationOptions();
        options.TrustedAuthorKeys.Add(new TrustedPluginAuthorKey("trusted-author", key.ExportRSAPublicKeyPem()));

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(options);
        builder.Services.AddModusPluginHostingRuntime();

        await using var app = builder.Build();
        app.Services.GetRequiredService<ManagementPluginUploadsEndpointMapper>().Map(app);
        await app.StartAsync();

        var client = app.GetTestClient();
        using var content = BuildUploadRequest(packageBytes, signatureBytes);
        var uploadResponse = await client.PostAsync("/management/plugins/uploads", content);
        var uploadPayload = await uploadResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.Accepted, uploadResponse.StatusCode);
        Assert.True(uploadPayload.TryGetProperty("operationId", out var operationIdElement));
        Assert.True(Guid.TryParse(operationIdElement.GetString(), out var operationId));

        var snapshots = await PollUploadStatusUntilTerminalAsync(client, operationId, TimeSpan.FromSeconds(10));
        Assert.NotEmpty(snapshots);

        AssertMonotonicStageAndProgressTransitions(snapshots);

        var terminal = snapshots[^1];
        Assert.True(terminal.IsTerminal);
        Assert.Equal(100, terminal.ProgressPercent);
        Assert.True(
            terminal.Stage is PluginUploadOperationStage.Completed or PluginUploadOperationStage.Failed,
            $"Expected terminal stage Completed or Failed but got '{terminal.Stage}'.");
    }

    [Fact]
    [Trait("ChecklistItem", "Verify `GET /management/plugins/uploads/{operationId}` integration polling reports deterministic monotonic stage/progress transitions through terminal state [depends on async upload pipeline]")]
    [Trait("AuditArtifact", "iterative-implementation-upload-status-out-of-order-monotonic-2026-05-22")]
    public async Task GetPluginUploadOperationStatus_GivenOutOfOrderStoreUpdates_ExpectedEndpointSequenceRemainsMonotonicAndTerminalStateSticks()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddModusPluginHostingRuntime();

        await using var app = builder.Build();
        app.Services.GetRequiredService<ManagementPluginUploadsEndpointMapper>().Map(app);
        await app.StartAsync();

        var store = app.Services.GetRequiredService<PluginUploadOperationStore>();
        var operation = store.CreateQueued("plugin.bundle.zip");

        var client = app.GetTestClient();
        var snapshots = new List<UploadStatusSnapshot>
        {
            await GetUploadStatusSnapshotAsync(client, operation.OperationId)
        };

        store.MarkStage(operation.OperationId, PluginUploadOperationStage.Extracting, 30, "stage=extract outcome=running");
        snapshots.Add(await GetUploadStatusSnapshotAsync(client, operation.OperationId));

        store.MarkStage(operation.OperationId, PluginUploadOperationStage.Validating, 55, "stage=validate outcome=running");
        snapshots.Add(await GetUploadStatusSnapshotAsync(client, operation.OperationId));

        // Attempt regressions that should be ignored by the operation store.
        store.MarkStage(operation.OperationId, PluginUploadOperationStage.Extracting, 40, "stage=extract outcome=running-regressed");
        snapshots.Add(await GetUploadStatusSnapshotAsync(client, operation.OperationId));

        store.MarkStage(operation.OperationId, PluginUploadOperationStage.Running, 90, "stage=run outcome=running");
        snapshots.Add(await GetUploadStatusSnapshotAsync(client, operation.OperationId));

        store.Complete(operation.OperationId, ["stage=run outcome=success activated=Plugin.Host.Telemetry"]);
        snapshots.Add(await GetUploadStatusSnapshotAsync(client, operation.OperationId));

        store.MarkStage(operation.OperationId, PluginUploadOperationStage.Authorizing, 5, "stage=authorization outcome=running-regressed");
        store.Fail(operation.OperationId, "must-not-overwrite-terminal");
        snapshots.Add(await GetUploadStatusSnapshotAsync(client, operation.OperationId));

        AssertMonotonicStageAndProgressTransitions(snapshots);

        var terminal = snapshots[^1];
        Assert.True(terminal.IsTerminal);
        Assert.True(terminal.IsSuccess);
        Assert.Equal(PluginUploadOperationStage.Completed, terminal.Stage);
        Assert.Equal(100, terminal.ProgressPercent);
        Assert.True(string.IsNullOrWhiteSpace(terminal.FailureReason));
    }

    [Fact]
    [Trait("ChecklistItem", "Verify runtime-added plugin operations requiring resolver-backed DI type lookup execute successfully with expected service lifetime behavior [mandatory - DI resolver assurance]")]
    [Trait("AuditArtifact", "iterative-implementation-runtime-added-di-resolver-lifetime-2026-05-22")]
    public async Task RuntimeResolver_GivenRuntimeAddedDispatchTargets_ExpectedApiDispatchHonorsSingletonScopedAndTransientLifetimes()
    {
        var registry = new RuntimePluginRegistry();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(registry);
        builder.Services.AddSingleton(static services => new PluginEndpointMapper(services.GetRequiredService<RuntimePluginRegistry>()));
        builder.Services.AddSingleton<RuntimeAddedSingletonResponder>();
        builder.Services.AddScoped<RuntimeAddedScopedResponder>();
        builder.Services.AddTransient<RuntimeAddedTransientResponder>();

        await using var app = builder.Build();
        app.Services.GetRequiredService<PluginEndpointMapper>().Map(app);
        await app.StartAsync();

        Assert.DoesNotContain(app.Services.GetServices<ISyncResponder>(), responder => responder is RuntimeAddedSingletonResponder);

        var singletonProjection = new RuntimeDispatchProjection(
            pluginId: "Plugin.Runtime.Added.Singleton",
            operationName: "Runtime.Singleton.Execute",
            pluginTypeFullName: typeof(RuntimeAddedSingletonResponder).FullName!,
            serviceLifetime: PluginServiceLifetime.Singleton);
        var scopedProjection = new RuntimeDispatchProjection(
            pluginId: "Plugin.Runtime.Added.Scoped",
            operationName: "Runtime.Scoped.Execute",
            pluginTypeFullName: typeof(RuntimeAddedScopedResponder).FullName!,
            serviceLifetime: PluginServiceLifetime.Scoped);
        var transientProjection = new RuntimeDispatchProjection(
            pluginId: "Plugin.Runtime.Added.Transient",
            operationName: "Runtime.Transient.Execute",
            pluginTypeFullName: typeof(RuntimeAddedTransientResponder).FullName!,
            serviceLifetime: PluginServiceLifetime.Transient);

        registry.Update(
            [singletonProjection, scopedProjection, transientProjection],
            [singletonProjection, scopedProjection, transientProjection]);

        var client = app.GetTestClient();

        var singletonA = await DispatchAndReadInstanceIdAsync(client, "Plugin.Runtime.Added.Singleton", "Runtime.Singleton.Execute", "singleton-a");
        var singletonB = await DispatchAndReadInstanceIdAsync(client, "Plugin.Runtime.Added.Singleton", "Runtime.Singleton.Execute", "singleton-b");
        var scopedA = await DispatchAndReadInstanceIdAsync(client, "Plugin.Runtime.Added.Scoped", "Runtime.Scoped.Execute", "scoped-a");
        var scopedB = await DispatchAndReadInstanceIdAsync(client, "Plugin.Runtime.Added.Scoped", "Runtime.Scoped.Execute", "scoped-b");
        var transientA = await DispatchAndReadInstanceIdAsync(client, "Plugin.Runtime.Added.Transient", "Runtime.Transient.Execute", "transient-a");
        var transientB = await DispatchAndReadInstanceIdAsync(client, "Plugin.Runtime.Added.Transient", "Runtime.Transient.Execute", "transient-b");

        Assert.Equal(singletonA, singletonB);
        Assert.NotEqual(scopedA, scopedB);
        Assert.NotEqual(transientA, transientB);
    }

    private static MultipartFormDataContent BuildUploadRequest(byte[] packageBytes, byte[] signatureBytes)
    {
        var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(packageBytes), "package", "plugin.bundle.zip");
        content.Add(new ByteArrayContent(signatureBytes), "signature", "plugin.bundle.sig");
        return content;
    }

    private static void AssertStageOutcomeOrder(IEnumerable<string> diagnostics, IReadOnlyList<string> expectedTokensInOrder)
    {
        var ordered = diagnostics.ToArray();
        var searchFrom = 0;

        foreach (var token in expectedTokensInOrder)
        {
            var index = Array.FindIndex(
                ordered,
                searchFrom,
                ordered.Length - searchFrom,
                diagnostic => diagnostic.Contains(token, StringComparison.Ordinal));

            Assert.True(index >= 0, $"Expected diagnostics to contain token '{token}'. Diagnostics: {string.Join(" | ", ordered)}");
            searchFrom = index + 1;
        }
    }

    private static byte[] CreateArchiveWithPluginAssembly()
    {
        var assemblyPath = Path.Combine(AppContext.BaseDirectory, "Plugin.Host.Telemetry.dll");
        Assert.True(File.Exists(assemblyPath), $"Expected plugin assembly at '{assemblyPath}'.");

        var assemblyBytes = File.ReadAllBytes(assemblyPath);
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("plugins/Plugin.Host.Telemetry.dll", CompressionLevel.Optimal);
            using var entryStream = entry.Open();
            entryStream.Write(assemblyBytes, 0, assemblyBytes.Length);
        }

        return stream.ToArray();
    }

    private static byte[] CreateArchiveWithoutAssemblies()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("README.txt", CompressionLevel.Optimal);
            using var writer = new StreamWriter(entry.Open());
            writer.Write("No plugin assemblies in this package.");
        }

        return stream.ToArray();
    }

    private static async Task<PluginUploadOperationStatus> WaitForTerminalStatusAsync(
        PluginUploadOperationStore store,
        Guid operationId)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        while (!timeout.IsCancellationRequested)
        {
            if (store.TryGet(operationId, out var status) && status is not null && status.IsTerminal)
            {
                return status;
            }

            await Task.Delay(25, timeout.Token);
        }

        throw new TimeoutException($"Upload operation '{operationId}' did not reach terminal state.");
    }

    private static async Task<IReadOnlyList<UploadStatusSnapshot>> PollUploadStatusUntilTerminalAsync(
        HttpClient client,
        Guid operationId,
        TimeSpan timeout)
    {
        var samples = new List<UploadStatusSnapshot>();
        using var timeoutCts = new CancellationTokenSource(timeout);

        while (!timeoutCts.IsCancellationRequested)
        {
            var snapshot = await GetUploadStatusSnapshotAsync(client, operationId);
            samples.Add(snapshot);

            if (snapshot.IsTerminal)
            {
                return samples;
            }

            await Task.Delay(25, timeoutCts.Token);
        }

        throw new TimeoutException($"Upload operation '{operationId}' did not reach terminal state through status polling.");
    }

    private static async Task<UploadStatusSnapshot> GetUploadStatusSnapshotAsync(HttpClient client, Guid operationId)
    {
        var response = await client.GetAsync($"/management/plugins/uploads/{operationId}");
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(operationId, payload.GetProperty("operationId").GetGuid());

        var stageText = payload.GetProperty("stage").GetString();
        Assert.False(string.IsNullOrWhiteSpace(stageText));
        Assert.True(Enum.TryParse<PluginUploadOperationStage>(stageText, ignoreCase: false, out var stage));

        return new UploadStatusSnapshot(
            Stage: stage,
            ProgressPercent: payload.GetProperty("progressPercent").GetInt32(),
            IsTerminal: payload.GetProperty("isTerminal").GetBoolean(),
            IsSuccess: payload.GetProperty("isSuccess").GetBoolean(),
            FailureReason: payload.GetProperty("failureReason").ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
                ? null
                : payload.GetProperty("failureReason").GetString());
    }

    private static void AssertMonotonicStageAndProgressTransitions(IReadOnlyList<UploadStatusSnapshot> snapshots)
    {
        Assert.NotEmpty(snapshots);

        for (var index = 1; index < snapshots.Count; index++)
        {
            var previous = snapshots[index - 1];
            var current = snapshots[index];

            Assert.True(
                current.Stage >= previous.Stage,
                $"Stage regressed from '{previous.Stage}' to '{current.Stage}' at sample index {index}.");
            Assert.True(
                current.ProgressPercent >= previous.ProgressPercent,
                $"Progress regressed from {previous.ProgressPercent} to {current.ProgressPercent} at sample index {index}.");
        }
    }

    private static string? ExtractActivatedPluginId(IEnumerable<string> diagnostics)
    {
        foreach (var diagnostic in diagnostics)
        {
            const string token = "activated=";
            var start = diagnostic.IndexOf(token, StringComparison.Ordinal);
            if (start < 0)
            {
                continue;
            }

            var value = diagnostic[(start + token.Length)..].Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var comma = value.IndexOf(',', StringComparison.Ordinal);
            return comma >= 0 ? value[..comma].Trim() : value;
        }

        return null;
    }

    private static string[] BuildContractSnapshotSignature(IEnumerable<IPluginContract> contracts)
    {
        return contracts
            .Select(static contract => $"{contract.PluginId.Value}|{contract.ContractName.Value}|{contract.ContractVersion}")
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();
    }

    private static string[] BuildCatalogSnapshotSignature(IEnumerable<IPluginOperationCatalog> catalogs)
    {
        return catalogs
            .Select(static catalog =>
            {
                var pluginId = catalog is IPluginContract contract ? contract.PluginId.Value : "<no-plugin-contract>";
                var operations = string.Join(",", catalog.SupportedOperations
                    .Select(static operation => operation.Value)
                    .OrderBy(static value => value, StringComparer.Ordinal));
                return $"{pluginId}|{operations}";
            })
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();
    }

    private static async Task<string> DispatchAndReadInstanceIdAsync(
        HttpClient client,
        string pluginId,
        string operation,
        string correlationId)
    {
        var dispatchHttpResponse = await client.PostAsJsonAsync(
            $"/api/{pluginId}/{operation}",
            new PluginOperationHttpRequest
            {
                CorrelationId = correlationId,
                Payload = "{}"
            });

        var dispatchResponse = await dispatchHttpResponse.Content.ReadFromJsonAsync<PluginOperationHttpResponse>();
        Assert.Equal(HttpStatusCode.OK, dispatchHttpResponse.StatusCode);
        Assert.NotNull(dispatchResponse);
        Assert.True(dispatchResponse!.Success);
        Assert.Equal(SyncResponseStatus.Success, dispatchResponse.Status);
        Assert.Equal(correlationId, dispatchResponse.CorrelationId);

        var payloadObject = PluginOperationPayload.AsJsonElement(dispatchResponse.Payload);
        Assert.Equal(JsonValueKind.Object, payloadObject.ValueKind);
        var instanceId = payloadObject.GetProperty("instanceId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(instanceId));
        return instanceId!;
    }

    private sealed class RuntimeDispatchProjection : IRuntimePluginDispatchTarget
    {
        public RuntimeDispatchProjection(
            string pluginId,
            string operationName,
            string pluginTypeFullName,
            PluginServiceLifetime serviceLifetime)
        {
            PluginId = new PluginId(pluginId);
            ContractName = new ContractName($"Contract.{pluginId}");
            ContractVersion = new Version(1, 0, 0);
            SupportedOperations = [new OperationName(operationName)];
            PluginTypeFullName = pluginTypeFullName;
            ServiceLifetime = serviceLifetime;
        }

        public PluginId PluginId { get; }

        public ContractName ContractName { get; }

        public Version ContractVersion { get; }

        public IReadOnlyCollection<OperationName> SupportedOperations { get; }

        public string? PluginTypeFullName { get; }

        public PluginServiceLifetime? ServiceLifetime { get; }
    }

    private sealed class RuntimeAddedSingletonResponder : RuntimeAddedResponderBase
    {
        public override PluginId PluginId => new("Plugin.Runtime.Added.Singleton");

        protected override string OperationName => "Runtime.Singleton.Execute";
    }

    private sealed class RuntimeAddedScopedResponder : RuntimeAddedResponderBase
    {
        public override PluginId PluginId => new("Plugin.Runtime.Added.Scoped");

        protected override string OperationName => "Runtime.Scoped.Execute";
    }

    private sealed class RuntimeAddedTransientResponder : RuntimeAddedResponderBase
    {
        public override PluginId PluginId => new("Plugin.Runtime.Added.Transient");

        protected override string OperationName => "Runtime.Transient.Execute";
    }

    private abstract class RuntimeAddedResponderBase : IPluginContract, IPluginOperationCatalog, ISyncResponder
    {
        private readonly Guid _instanceId = Guid.NewGuid();

        public ContractName ContractName => new("Modus.RuntimeAdded.Lifetime");

        public Version ContractVersion => new(1, 0, 0);

        public IReadOnlyCollection<OperationName> SupportedOperations => [new OperationName(OperationName)];

        public abstract PluginId PluginId { get; }

        protected abstract string OperationName { get; }

        public SyncResponse Handle(SyncRequest request)
        {
            var payload = new
            {
                pluginId = PluginId.Value,
                operation = request.Operation.Value,
                instanceId = _instanceId.ToString("N")
            };

            return new SyncResponse(
                Success: true,
                Payload: payload,
                CorrelationId: request.CorrelationId);
        }
    }

    private sealed record UploadStatusSnapshot(
        PluginUploadOperationStage Stage,
        int ProgressPercent,
        bool IsTerminal,
        bool IsSuccess,
        string? FailureReason);
}
