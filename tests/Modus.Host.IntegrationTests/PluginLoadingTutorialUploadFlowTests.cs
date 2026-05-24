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

public sealed class PluginLoadingTutorialUploadFlowTests
{
    [Fact]
    [Trait("ChecklistItem", "Document the plugin upload flow with executable request examples to the management upload endpoint and expected success/rejection response contracts [depends on tutorial entry gate]")]
    public async Task UploadFlow_GivenValidPluginPackage_ExpectedOwnerResolvedUniquelyAndPackageAccepted()
    {
        var packageBytes = CreateArchiveWithPluginAssembly();
        using var trustedAuthorKey = RSA.Create(2048);
        var signatureBytes = trustedAuthorKey.SignData(packageBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        var options = new PluginUploadAuthorizationOptions();
        options.TrustedAuthorKeys.Add(new TrustedPluginAuthorKey("trusted-author", trustedAuthorKey.ExportRSAPublicKeyPem()));

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(options);
        builder.Services.AddModusPluginHostingRuntime();

        await using var app = builder.Build();
        app.Services.GetRequiredService<ManagementPluginUploadsEndpointMapper>().Map(app);
        await app.StartAsync();

        var client = app.GetTestClient();
        using var request = BuildUploadRequest(packageBytes, signatureBytes);
        var uploadResponse = await client.PostAsync("/management/plugins/uploads", request);
        var uploadPayload = await uploadResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.Accepted, uploadResponse.StatusCode);
        Assert.NotNull(uploadResponse.Headers.Location);
        Assert.True(uploadPayload.TryGetProperty("operationId", out var operationIdElement));
        Assert.True(Guid.TryParse(operationIdElement.GetString(), out var operationId));
        Assert.Equal("Queued", uploadPayload.GetProperty("status").GetString());
        Assert.EndsWith($"/management/plugins/uploads/{operationId}", uploadResponse.Headers.Location!.ToString(), StringComparison.Ordinal);

        var store = app.Services.GetRequiredService<PluginUploadOperationStore>();
        var operation = await WaitForTerminalStatusAsync(store, operationId);

        Assert.True(operation.IsSuccess);
        Assert.Equal(PluginUploadOperationStage.Completed, operation.Stage);

        var activatedPluginIds = ExtractActivatedPluginIds(operation.Diagnostics)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        Assert.Single(activatedPluginIds);

        var registry = app.Services.GetRequiredService<RuntimePluginRegistry>();
        var snapshot = registry.GetSnapshot();
        Assert.Contains(
            snapshot.Contracts,
            contract => string.Equals(contract.PluginId.Value, activatedPluginIds[0], StringComparison.Ordinal));
    }

    [Fact]
    [Trait("ChecklistItem", "Document the plugin upload flow with executable request examples to the management upload endpoint and expected success/rejection response contracts [depends on tutorial entry gate]")]
    public async Task UploadFlow_GivenMismatchedSignature_ExpectedRejectedContractAndNoActivationSideEffects()
    {
        var packageBytes = CreateArchiveWithPluginAssembly();
        using var trustedAuthorKey = RSA.Create(2048);
        using var untrustedAuthorKey = RSA.Create(2048);
        var signatureBytes = untrustedAuthorKey.SignData(packageBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        var options = new PluginUploadAuthorizationOptions();
        options.TrustedAuthorKeys.Add(new TrustedPluginAuthorKey("trusted-author", trustedAuthorKey.ExportRSAPublicKeyPem()));

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(options);
        builder.Services.AddModusPluginHostingRuntime();

        await using var app = builder.Build();
        app.Services.GetRequiredService<ManagementPluginUploadsEndpointMapper>().Map(app);
        await app.StartAsync();

        var registry = app.Services.GetRequiredService<RuntimePluginRegistry>();
        var preUploadContractSignature = BuildContractSnapshotSignature(registry.GetSnapshot().Contracts);

        var client = app.GetTestClient();
        using var request = BuildUploadRequest(packageBytes, signatureBytes);
        var uploadResponse = await client.PostAsync("/management/plugins/uploads", request);
        var rejectionPayload = await uploadResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.Unauthorized, uploadResponse.StatusCode);
        Assert.Equal("Rejected", rejectionPayload.GetProperty("status").GetString());
        Assert.Equal(
            "Plugin upload signature did not match any trusted author key.",
            rejectionPayload.GetProperty("error").GetString());

        var postUploadContractSignature = BuildContractSnapshotSignature(registry.GetSnapshot().Contracts);
        Assert.Equal(preUploadContractSignature, postUploadContractSignature);
    }

    [Fact]
    [Trait("ChecklistItem", "Document activation verification flow using management status and capabilities endpoints, including owner uniqueness and activated-operation visibility checks [depends on upload flow]")]
    public async Task ActivationVerification_GivenSuccessfulUpload_ExpectedStatusEndpointShowsActivatedPlugin()
    {
        var packageBytes = CreateArchiveWithPluginAssembly();
        using var trustedAuthorKey = RSA.Create(2048);
        var signatureBytes = trustedAuthorKey.SignData(packageBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        var options = new PluginUploadAuthorizationOptions();
        options.TrustedAuthorKeys.Add(new TrustedPluginAuthorKey("trusted-author", trustedAuthorKey.ExportRSAPublicKeyPem()));

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(options);
        builder.Services.AddModusPluginHostingRuntime();

        await using var app = builder.Build();
        app.Services.GetRequiredService<ManagementPluginUploadsEndpointMapper>().Map(app);
        app.Services.GetRequiredService<ManagementStatusEndpointMapper>().Map(app);
        await app.StartAsync();

        var client = app.GetTestClient();
        using var request = BuildUploadRequest(packageBytes, signatureBytes);
        var uploadResponse = await client.PostAsync("/management/plugins/uploads", request);
        var uploadPayload = await uploadResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.Accepted, uploadResponse.StatusCode);

        Assert.True(Guid.TryParse(uploadPayload.GetProperty("operationId").GetString(), out var operationId));
        var store = app.Services.GetRequiredService<PluginUploadOperationStore>();
        var operation = await WaitForTerminalStatusAsync(store, operationId);
        Assert.True(operation.IsSuccess);

        var activatedPluginId = Assert.Single(
            ExtractActivatedPluginIds(operation.Diagnostics)
                .Distinct(StringComparer.Ordinal)
                .ToArray());

        var statusResponse = await client.GetAsync("/management/status");
        var statusPayload = await statusResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);
        var loadedPlugins = statusPayload.GetProperty("loadedPlugins").EnumerateArray().ToArray();
        Assert.Contains(
            loadedPlugins,
            plugin => string.Equals(plugin.GetProperty("pluginId").GetString(), activatedPluginId, StringComparison.Ordinal)
                && string.Equals(plugin.GetProperty("lifecycleState").GetString(), "Active", StringComparison.Ordinal));

        var capabilityOwnership = statusPayload.GetProperty("capabilityOwnership").EnumerateArray().ToArray();
        Assert.Equal(
            capabilityOwnership.Length,
            capabilityOwnership
                .Select(static item => item.GetProperty("capability").GetString()!)
                .Distinct(StringComparer.Ordinal)
                .Count());
        Assert.Contains(
            capabilityOwnership,
            item => string.Equals(item.GetProperty("ownerPluginId").GetString(), activatedPluginId, StringComparison.Ordinal));
    }

    [Fact]
    [Trait("ChecklistItem", "Document activation verification flow using management status and capabilities endpoints, including owner uniqueness and activated-operation visibility checks [depends on upload flow]")]
    public async Task ActivationVerification_GivenActivatedPlugin_ExpectedCapabilitiesEndpointListsDeclaredOperations()
    {
        var packageBytes = CreateArchiveWithPluginAssembly();
        using var trustedAuthorKey = RSA.Create(2048);
        var signatureBytes = trustedAuthorKey.SignData(packageBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        var options = new PluginUploadAuthorizationOptions();
        options.TrustedAuthorKeys.Add(new TrustedPluginAuthorKey("trusted-author", trustedAuthorKey.ExportRSAPublicKeyPem()));

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(options);
        builder.Services.AddModusPluginHostingRuntime();

        await using var app = builder.Build();
        app.Services.GetRequiredService<ManagementPluginUploadsEndpointMapper>().Map(app);
        app.Services.GetRequiredService<ManagementPluginCapabilitiesEndpointMapper>().Map(app);
        await app.StartAsync();

        var client = app.GetTestClient();
        using var request = BuildUploadRequest(packageBytes, signatureBytes);
        var uploadResponse = await client.PostAsync("/management/plugins/uploads", request);
        var uploadPayload = await uploadResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.Accepted, uploadResponse.StatusCode);

        Assert.True(Guid.TryParse(uploadPayload.GetProperty("operationId").GetString(), out var operationId));
        var store = app.Services.GetRequiredService<PluginUploadOperationStore>();
        var operation = await WaitForTerminalStatusAsync(store, operationId);
        Assert.True(operation.IsSuccess);

        var activatedPluginId = Assert.Single(
            ExtractActivatedPluginIds(operation.Diagnostics)
                .Distinct(StringComparer.Ordinal)
                .ToArray());

        var expectedOperations = app.Services
            .GetRequiredService<RuntimePluginRegistry>()
            .GetSnapshot()
            .Catalogs
            .Where(catalog => catalog is IPluginContract contract
                && string.Equals(contract.PluginId.Value, activatedPluginId, StringComparison.Ordinal))
            .OfType<IPluginOperationCatalog>()
            .SelectMany(static catalog => catalog.SupportedOperations)
            .Select(static operationName => operationName.Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();
        Assert.NotEmpty(expectedOperations);

        var capabilitiesResponse = await client.GetAsync("/management/plugins/capabilities");
        var capabilitiesPayload = await capabilitiesResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, capabilitiesResponse.StatusCode);
        var pluginEntry = Assert.Single(
            capabilitiesPayload
                .GetProperty("plugins")
                .EnumerateArray()
                .Where(plugin => string.Equals(plugin.GetProperty("pluginId").GetString(), activatedPluginId, StringComparison.Ordinal))
                .ToArray());

        var operations = pluginEntry.GetProperty("operations").EnumerateArray().Select(static item => item.GetString()!).ToArray();
        Assert.Equal(expectedOperations, operations);

        var pluginCapabilities = pluginEntry.GetProperty("capabilities").EnumerateArray().Select(static item => item.GetString()!).ToArray();
        var capabilityOwners = capabilitiesPayload.GetProperty("capabilities").EnumerateArray().ToArray();

        foreach (var capability in pluginCapabilities)
        {
            var ownerEntries = capabilityOwners
                .Where(item => string.Equals(item.GetProperty("capability").GetString(), capability, StringComparison.Ordinal))
                .ToArray();
            var owner = Assert.Single(ownerEntries);
            Assert.Equal(activatedPluginId, owner.GetProperty("ownerPluginId").GetString());
        }
    }

    [Fact]
    [Trait("ChecklistItem", "Document runtime operation invocation through POST /api/{pluginId}/{operation} with payload and correlation continuity checks [depends on activation verification]")]
    public async Task OperationInvocation_GivenActivePluginOperation_ExpectedDispatchReturnsBusinessSemanticSuccessPayload()
    {
        var plugin = new InvocationTutorialResponder("Plugin.Invocation.Tutorial", "Invocation.Echo");
        var mapper = new PluginEndpointMapper([plugin], [plugin]);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddScoped<ISyncResponder>(_ => plugin);

        await using var app = builder.Build();
        mapper.Map(app);
        await app.StartAsync();

        var requestPayload = "{\"message\":\"tutorial-payload\"}";
        var client = app.GetTestClient();
        var httpResponse = await client.PostAsJsonAsync(
            "/api/Plugin.Invocation.Tutorial/Invocation.Echo",
            new PluginOperationHttpRequest
            {
                CorrelationId = "tutorial-op-success-corr",
                Payload = requestPayload
            });

        var response = await httpResponse.Content.ReadFromJsonAsync<PluginOperationHttpResponse>();

        Assert.Equal(HttpStatusCode.OK, httpResponse.StatusCode);
        Assert.NotNull(response);
        Assert.True(response!.Success);
        Assert.Equal(SyncResponseStatus.Success, response.Status);
        Assert.Equal("tutorial-op-success-corr", response.CorrelationId);
        Assert.Equal("handled:Invocation.Echo", PluginOperationPayload.AsStringValue(response.Payload));
    }

    [Fact]
    [Trait("ChecklistItem", "Document runtime operation invocation through POST /api/{pluginId}/{operation} with payload and correlation continuity checks [depends on activation verification]")]
    public async Task OperationInvocation_GivenRequestCorrelationId_ExpectedResponseCorrelationMatchesRequestOnSuccessAndRejection()
    {
        var plugin = new InvocationTutorialResponder("Plugin.Invocation.Tutorial", "Invocation.Echo");
        var mapper = new PluginEndpointMapper([plugin], [plugin]);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddScoped<ISyncResponder>(_ => plugin);

        await using var app = builder.Build();
        mapper.Map(app);
        await app.StartAsync();

        var client = app.GetTestClient();
        var successResponse = await client.PostAsJsonAsync(
            "/api/Plugin.Invocation.Tutorial/Invocation.Echo",
            new PluginOperationHttpRequest
            {
                CorrelationId = "tutorial-op-corr-success",
                Payload = "{\"mode\":\"allow\"}"
            });
        var successBody = await successResponse.Content.ReadFromJsonAsync<PluginOperationHttpResponse>();

        Assert.Equal(HttpStatusCode.OK, successResponse.StatusCode);
        Assert.NotNull(successBody);
        Assert.True(successBody!.Success);
        Assert.Equal("tutorial-op-corr-success", successBody.CorrelationId);

        var rejectionResponse = await client.PostAsJsonAsync(
            "/api/Plugin.Invocation.Tutorial/Invocation.Echo",
            new PluginOperationHttpRequest
            {
                CorrelationId = "tutorial-op-corr-rejected",
                Payload = "please-reject"
            });
        var rejectionBody = await rejectionResponse.Content.ReadFromJsonAsync<PluginOperationHttpResponse>();

        Assert.Equal(HttpStatusCode.UnprocessableEntity, rejectionResponse.StatusCode);
        Assert.NotNull(rejectionBody);
        Assert.False(rejectionBody!.Success);
        Assert.Equal(SyncResponseStatus.Rejected, rejectionBody.Status);
        Assert.Equal("tutorial-op-corr-rejected", rejectionBody.CorrelationId);
        Assert.Equal("rejected:payload-policy", PluginOperationPayload.AsStringValue(rejectionBody.Payload));
    }

    [Fact]
    [Trait("ChecklistItem", "Document deterministic failure tutorial for invalid package, owner mismatch, and unresolved responder scenarios with isolation guarantees and no side-effect execution [depends on upload flow and runtime invocation]")]
    public async Task FailureTutorial_GivenInvalidPackageUpload_ExpectedDeterministicFailureIsolationAndNoSideEffectExecution()
    {
        var packageBytes = CreateArchiveWithoutAssemblies();
        using var trustedAuthorKey = RSA.Create(2048);
        var signatureBytes = trustedAuthorKey.SignData(packageBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        var options = new PluginUploadAuthorizationOptions();
        options.TrustedAuthorKeys.Add(new TrustedPluginAuthorKey("trusted-author", trustedAuthorKey.ExportRSAPublicKeyPem()));

        var sideEffectProbe = new SideEffectProbeResponder("Plugin.Host.Telemetry", "Telemetry.Host.CollectSnapshot");
        var statusRegistry = new HostStatusRegistry();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton(statusRegistry);
        builder.Services.AddScoped<ISyncResponder>(_ => sideEffectProbe);
        builder.Services.AddModusPluginHostingRuntime();

        await using var app = builder.Build();
        app.Services.GetRequiredService<ManagementPluginUploadsEndpointMapper>().Map(app);
        app.Services.GetRequiredService<PluginEndpointMapper>().Map(app);
        await app.StartAsync();

        var registry = app.Services.GetRequiredService<RuntimePluginRegistry>();
        var preUploadContracts = BuildContractSnapshotSignature(registry.GetSnapshot().Contracts);
        var preUploadCatalogs = BuildCatalogSnapshotSignature(registry.GetSnapshot().Catalogs);

        var client = app.GetTestClient();
        using var request = BuildUploadRequest(packageBytes, signatureBytes);
        var uploadResponse = await client.PostAsync("/management/plugins/uploads", request);
        var uploadPayload = await uploadResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.Accepted, uploadResponse.StatusCode);
        Assert.True(Guid.TryParse(uploadPayload.GetProperty("operationId").GetString(), out var operationId));

        var store = app.Services.GetRequiredService<PluginUploadOperationStore>();
        var operation = await WaitForTerminalStatusAsync(store, operationId);

        Assert.False(operation.IsSuccess);
        Assert.Equal(PluginUploadOperationStage.Failed, operation.Stage);
        Assert.Equal("No plugin assemblies were found in upload package.", operation.FailureReason);
        Assert.Contains(
            operation.Diagnostics,
            diagnostic => string.Equals(
                diagnostic,
                "stage=validation outcome=failure reason=no-plugin-assemblies",
                StringComparison.Ordinal));

        var postUploadContracts = BuildContractSnapshotSignature(registry.GetSnapshot().Contracts);
        var postUploadCatalogs = BuildCatalogSnapshotSignature(registry.GetSnapshot().Catalogs);
        Assert.Equal(preUploadContracts, postUploadContracts);
        Assert.Equal(preUploadCatalogs, postUploadCatalogs);

        var dispatchResponse = await client.PostAsJsonAsync(
            "/api/Plugin.Host.Telemetry/Telemetry.Host.CollectSnapshot",
            new PluginOperationHttpRequest
            {
                CorrelationId = "tutorial-failure-invalid-package-corr",
                Payload = "{}"
            });
        var dispatchBody = await dispatchResponse.Content.ReadFromJsonAsync<PluginOperationHttpResponse>();

        Assert.Equal(HttpStatusCode.InternalServerError, dispatchResponse.StatusCode);
        Assert.NotNull(dispatchBody);
        Assert.False(dispatchBody!.Success);
        Assert.Equal(SyncResponseStatus.Failed, dispatchBody.Status);
        Assert.Equal("tutorial-failure-invalid-package-corr", dispatchBody.CorrelationId);
        Assert.True(PluginOperationPayload.Contains(dispatchBody.Payload, "No runtime plugin operation owner found", StringComparison.Ordinal));
        Assert.Equal(0, sideEffectProbe.InvocationCount);

        var diagnostics = statusRegistry.GetCurrent().Diagnostics;
        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.Contains("stage=dispatch outcome=failure reason=owner-mismatch", StringComparison.Ordinal)
                && diagnostic.Contains("plugin=Plugin.Host.Telemetry", StringComparison.Ordinal)
                && diagnostic.Contains("operation=Telemetry.Host.CollectSnapshot", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("ChecklistItem", "Document deterministic failure tutorial for invalid package, owner mismatch, and unresolved responder scenarios with isolation guarantees and no side-effect execution [depends on upload flow and runtime invocation]")]
    public async Task FailureTutorial_GivenOwnerMismatchInvocation_ExpectedDeterministicFailureAndResponderIsolation()
    {
        var ownedResponder = new SideEffectProbeResponder("Plugin.Owner.Actual", "Owner.Check");
        var mapper = new PluginEndpointMapper([ownedResponder], [ownedResponder]);
        var statusRegistry = new HostStatusRegistry();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(statusRegistry);
        builder.Services.AddScoped<ISyncResponder>(_ => ownedResponder);

        await using var app = builder.Build();
        mapper.Map(app);
        await app.StartAsync();

        var client = app.GetTestClient();
        var httpResponse = await client.PostAsJsonAsync(
            "/api/Plugin.Owner.Mismatch/Owner.Check",
            new PluginOperationHttpRequest
            {
                CorrelationId = "tutorial-failure-owner-mismatch-corr",
                Payload = "{}"
            });
        var body = await httpResponse.Content.ReadFromJsonAsync<PluginOperationHttpResponse>();

        Assert.Equal(HttpStatusCode.InternalServerError, httpResponse.StatusCode);
        Assert.NotNull(body);
        Assert.False(body!.Success);
        Assert.Equal(SyncResponseStatus.Failed, body.Status);
        Assert.Equal("tutorial-failure-owner-mismatch-corr", body.CorrelationId);
        Assert.True(PluginOperationPayload.Contains(body.Payload, "No runtime plugin operation owner found", StringComparison.Ordinal));
        Assert.Equal(0, ownedResponder.InvocationCount);

        var diagnostics = statusRegistry.GetCurrent().Diagnostics;
        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.Contains("stage=dispatch outcome=failure reason=owner-mismatch", StringComparison.Ordinal)
                && diagnostic.Contains("plugin=Plugin.Owner.Mismatch", StringComparison.Ordinal)
                && diagnostic.Contains("operation=Owner.Check", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("ChecklistItem", "Document deterministic failure tutorial for invalid package, owner mismatch, and unresolved responder scenarios with isolation guarantees and no side-effect execution [depends on upload flow and runtime invocation]")]
    public async Task FailureTutorial_GivenUnresolvedResponder_ExpectedDeterministicFailureAndNoUnrelatedResponderExecution()
    {
        var catalog = new CatalogOnlyTutorialPlugin("Plugin.Catalog.Only", "Catalog.Only.Check");
        var unrelatedResponder = new SideEffectProbeResponder("Plugin.Unrelated", "Other.Op");
        var mapper = new PluginEndpointMapper([catalog], [catalog]);
        var statusRegistry = new HostStatusRegistry();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(statusRegistry);
        builder.Services.AddScoped<ISyncResponder>(_ => unrelatedResponder);

        await using var app = builder.Build();
        mapper.Map(app);
        await app.StartAsync();

        var client = app.GetTestClient();
        var httpResponse = await client.PostAsJsonAsync(
            "/api/Plugin.Catalog.Only/Catalog.Only.Check",
            new PluginOperationHttpRequest
            {
                CorrelationId = "tutorial-failure-unresolved-responder-corr",
                Payload = "{}"
            });
        var body = await httpResponse.Content.ReadFromJsonAsync<PluginOperationHttpResponse>();

        Assert.Equal(HttpStatusCode.InternalServerError, httpResponse.StatusCode);
        Assert.NotNull(body);
        Assert.False(body!.Success);
        Assert.Equal(SyncResponseStatus.Failed, body.Status);
        Assert.Equal("tutorial-failure-unresolved-responder-corr", body.CorrelationId);
        Assert.True(PluginOperationPayload.Contains(body.Payload, "No ISyncResponder registered in request scope for plugin 'Plugin.Catalog.Only'.", StringComparison.Ordinal));
        Assert.Equal(0, unrelatedResponder.InvocationCount);

        var diagnostics = statusRegistry.GetCurrent().Diagnostics;
        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.Contains("stage=dispatch outcome=failure reason=unresolved-responder", StringComparison.Ordinal)
                && diagnostic.Contains("plugin=Plugin.Catalog.Only", StringComparison.Ordinal)
                && diagnostic.Contains("operation=Catalog.Only.Check", StringComparison.Ordinal));
    }

    private static MultipartFormDataContent BuildUploadRequest(byte[] packageBytes, byte[] signatureBytes)
    {
        var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(packageBytes), "package", "plugin.bundle.zip");
        content.Add(new ByteArrayContent(signatureBytes), "signature", "plugin.bundle.sig");
        return content;
    }

    private static byte[] CreateArchiveWithoutAssemblies()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("plugins/readme.txt", CompressionLevel.Optimal);
            using var entryWriter = new StreamWriter(entry.Open());
            entryWriter.Write("invalid package for deterministic failure tutorial");
        }

        return stream.ToArray();
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

    private static IEnumerable<string> ExtractActivatedPluginIds(IEnumerable<string> diagnostics)
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
            yield return comma >= 0 ? value[..comma].Trim() : value;
        }
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
            .OfType<IPluginContract>()
            .Select(static contract => contract.PluginId.Value)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();
    }

    private sealed class InvocationTutorialResponder : IPluginContract, IPluginOperationCatalog, ISyncResponder
    {
        private readonly IReadOnlyCollection<OperationName> _supportedOperations;
        private readonly string _expectedOperation;

        public InvocationTutorialResponder(string pluginId, string expectedOperation)
        {
            PluginId = new PluginId(pluginId);
            _expectedOperation = expectedOperation;
            _supportedOperations = [new OperationName(expectedOperation)];
        }

        public PluginId PluginId { get; }

        public ContractName ContractName => new("Tutorial.Invocation");

        public Version ContractVersion => new(1, 0, 0);

        public IReadOnlyCollection<OperationName> SupportedOperations => _supportedOperations;

        public SyncResponse Handle(SyncRequest request)
        {
            if (!string.Equals(request.Operation.Value, _expectedOperation, StringComparison.Ordinal))
            {
                return new SyncResponse(
                    Success: false,
                    Payload: "unsupported-operation",
                    Status: SyncResponseStatus.Rejected,
                    CorrelationId: request.CorrelationId);
            }

            if (string.Equals(request.CorrelationId?.Value, "tutorial-op-corr-rejected", StringComparison.Ordinal))
            {
                return new SyncResponse(
                    Success: false,
                    Payload: "rejected:payload-policy",
                    Status: SyncResponseStatus.Rejected,
                    CorrelationId: request.CorrelationId);
            }

            return new SyncResponse(
                Success: true,
                Payload: $"handled:{request.Operation.Value}",
                Status: SyncResponseStatus.Success,
                CorrelationId: request.CorrelationId);
        }
    }

    private sealed class CatalogOnlyTutorialPlugin : IPluginContract, IPluginOperationCatalog
    {
        public CatalogOnlyTutorialPlugin(string pluginId, string operation)
        {
            PluginId = new PluginId(pluginId);
            SupportedOperations = [new OperationName(operation)];
        }

        public PluginId PluginId { get; }

        public ContractName ContractName => new("Tutorial.CatalogOnly");

        public Version ContractVersion => new(1, 0, 0);

        public IReadOnlyCollection<OperationName> SupportedOperations { get; }
    }

    private sealed class SideEffectProbeResponder : IPluginContract, IPluginOperationCatalog, ISyncResponder
    {
        private readonly string _expectedOperation;
        private int _invocationCount;

        public SideEffectProbeResponder(string pluginId, string expectedOperation)
        {
            PluginId = new PluginId(pluginId);
            _expectedOperation = expectedOperation;
            SupportedOperations = [new OperationName(expectedOperation)];
        }

        public PluginId PluginId { get; }

        public ContractName ContractName => new("Tutorial.SideEffectProbe");

        public Version ContractVersion => new(1, 0, 0);

        public IReadOnlyCollection<OperationName> SupportedOperations { get; }

        public int InvocationCount => _invocationCount;

        public SyncResponse Handle(SyncRequest request)
        {
            Interlocked.Increment(ref _invocationCount);

            var success = string.Equals(request.Operation.Value, _expectedOperation, StringComparison.Ordinal);
            return new SyncResponse(
                Success: success,
                Payload: success ? "side-effect-executed" : "unexpected-operation",
                Status: success ? SyncResponseStatus.Success : SyncResponseStatus.Rejected,
                CorrelationId: request.CorrelationId);
        }
    }
}
