using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
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
    [Trait("ChecklistItem", "Implement POST /management/plugins/uploads as async upload endpoint to extract, validate, load, and run plugin packages [mandatory - plugin upload endpoint]")]
    public async Task StartPluginUpload_GivenValidSignedPackage_QueuesAsyncOperationAndReturnsAccepted()
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
        var operation = await WaitForTerminalStatusAsync(store, operationId);
        Assert.True(operation.IsSuccess);
        Assert.Equal(PluginUploadOperationStage.Completed, operation.Stage);
        Assert.Null(operation.FailureReason);
    }

    [Fact]
    [Trait("ChecklistItem", "Implement POST /management/plugins/uploads as async upload endpoint to extract, validate, load, and run plugin packages [mandatory - plugin upload endpoint]")]
    public async Task StartPluginUpload_GivenPackageValidationFailure_MarksOperationFailedWithReason()
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
        await app.StartAsync();

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
    }

    [Fact]
    [Trait("ChecklistItem", "Implement GET /management/plugins/uploads/{operationId} for upload progress and final result status [depends on async upload pipeline]")]
    public async Task GetPluginUploadOperationStatus_GivenActiveOperation_ReturnsProgressState()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddModusPluginHostingRuntime();

        await using var app = builder.Build();
        app.Services.GetRequiredService<ManagementPluginUploadsEndpointMapper>().Map(app);
        await app.StartAsync();

        var store = app.Services.GetRequiredService<PluginUploadOperationStore>();
        var operation = store.CreateQueued("plugin.bundle.zip");
        store.MarkStage(operation.OperationId, PluginUploadOperationStage.Extracting, 30, "stage=extract outcome=running");

        var client = app.GetTestClient();
        var httpResponse = await client.GetAsync($"/management/plugins/uploads/{operation.OperationId}");
        var payload = await httpResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, httpResponse.StatusCode);
        Assert.Equal(operation.OperationId, payload.GetProperty("operationId").GetGuid());
        Assert.Equal("Extracting", payload.GetProperty("stage").GetString());
        Assert.Equal(30, payload.GetProperty("progressPercent").GetInt32());
        Assert.False(payload.GetProperty("isTerminal").GetBoolean());
        Assert.False(payload.GetProperty("isSuccess").GetBoolean());
        Assert.True(payload.GetProperty("diagnostics").EnumerateArray().Any());
    }

    [Fact]
    [Trait("ChecklistItem", "Implement GET /management/plugins/uploads/{operationId} for upload progress and final result status [depends on async upload pipeline]")]
    public async Task GetPluginUploadOperationStatus_GivenCompletedOperation_ReturnsTerminalResult()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddModusPluginHostingRuntime();

        await using var app = builder.Build();
        app.Services.GetRequiredService<ManagementPluginUploadsEndpointMapper>().Map(app);
        await app.StartAsync();

        var store = app.Services.GetRequiredService<PluginUploadOperationStore>();
        var operation = store.CreateQueued("plugin.bundle.zip");
        store.MarkStage(operation.OperationId, PluginUploadOperationStage.Running, 90, "stage=run outcome=running");
        store.Complete(operation.OperationId, ["stage=run outcome=success activated=Plugin.Host.Telemetry"]);

        var client = app.GetTestClient();
        var httpResponse = await client.GetAsync($"/management/plugins/uploads/{operation.OperationId}");
        var payload = await httpResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, httpResponse.StatusCode);
        Assert.Equal(operation.OperationId, payload.GetProperty("operationId").GetGuid());
        Assert.Equal("Completed", payload.GetProperty("stage").GetString());
        Assert.Equal(100, payload.GetProperty("progressPercent").GetInt32());
        Assert.True(payload.GetProperty("isTerminal").GetBoolean());
        Assert.True(payload.GetProperty("isSuccess").GetBoolean());
        Assert.True(payload.GetProperty("failureReason").ValueKind is JsonValueKind.Null or JsonValueKind.Undefined);
    }

    private static MultipartFormDataContent BuildUploadRequest(byte[] packageBytes, byte[] signatureBytes)
    {
        var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(packageBytes), "package", "plugin.bundle.zip");
        content.Add(new ByteArrayContent(signatureBytes), "signature", "plugin.bundle.sig");
        return content;
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
}
