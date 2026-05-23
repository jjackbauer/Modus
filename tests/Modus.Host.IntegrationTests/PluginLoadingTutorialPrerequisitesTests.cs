using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
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

public sealed class PluginLoadingTutorialPrerequisitesTests
{
    [Fact]
    [Trait("ChecklistItem", "Document endpoint-first prerequisites for plugin loading tutorial (artifact shape, auth, upload contract, and required request correlation fields) [mandatory - tutorial entry gate]")]
    [Trait("ChecklistItem", "Prove management API runtime contracts remain stable after OpenAPI mapping refactor [depends on management endpoint integration behavior proof]")]
    public async Task TutorialPrerequisites_GivenMissingUploadAuthorization_ExpectedUploadRejectedWithDeterministicContract()
    {
        var packageBytes = CreateArchiveWithPluginAssembly();
        using var authorKey = RSA.Create(2048);
        var signatureBytes = authorKey.SignData(packageBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddModusPluginHostingRuntime();

        await using var app = builder.Build();
        app.Services.GetRequiredService<ManagementPluginUploadsEndpointMapper>().Map(app);
        await app.StartAsync();

        var client = app.GetTestClient();
        using var request = BuildUploadRequest(packageBytes, signatureBytes);
        var uploadResponse = await client.PostAsync("/management/plugins/uploads", request);
        var errorPayload = await uploadResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.Unauthorized, uploadResponse.StatusCode);
        Assert.Equal("No trusted plugin author keys are configured.", errorPayload.GetProperty("error").GetString());
    }

    [Fact]
    [Trait("ChecklistItem", "Document endpoint-first prerequisites for plugin loading tutorial (artifact shape, auth, upload contract, and required request correlation fields) [mandatory - tutorial entry gate]")]
    public async Task TutorialPrerequisites_GivenRequiredRequestFields_ExpectedUploadRequestAcceptedForProcessing()
    {
        var packageBytes = CreateArchiveWithPluginAssembly();
        using var trustedKey = RSA.Create(2048);
        var signatureBytes = trustedKey.SignData(packageBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        var options = new PluginUploadAuthorizationOptions();
        options.TrustedAuthorKeys.Add(new TrustedPluginAuthorKey("trusted-author", trustedKey.ExportRSAPublicKeyPem()));

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(options);
        builder.Services.AddModusPluginHostingRuntime();

        await using var app = builder.Build();
        app.Services.GetRequiredService<ManagementPluginUploadsEndpointMapper>().Map(app);
        app.Services.GetRequiredService<PluginEndpointMapper>().Map(app);
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

        var store = app.Services.GetRequiredService<PluginUploadOperationStore>();
        var operation = await WaitForTerminalStatusAsync(store, operationId);
        Assert.True(operation.IsSuccess);

        var activatedPluginId = ExtractActivatedPluginId(operation.Diagnostics);
        Assert.False(string.IsNullOrWhiteSpace(activatedPluginId));

        var registry = app.Services.GetRequiredService<RuntimePluginRegistry>();
        var operationName = registry
            .GetSnapshot()
            .Catalogs
            .OfType<IPluginContract>()
            .Where(contract => string.Equals(contract.PluginId.Value, activatedPluginId, StringComparison.Ordinal))
            .OfType<IPluginOperationCatalog>()
            .SelectMany(static catalog => catalog.SupportedOperations)
            .Select(static operation => operation.Value)
            .First();

        const string correlationId = "tutorial-entry-gate-correlation";
        var dispatchResponseMessage = await client.PostAsJsonAsync(
            $"/api/{activatedPluginId}/{operationName}",
            new PluginOperationHttpRequest
            {
                CorrelationId = correlationId,
                Payload = "{}"
            });
        var dispatchResponse = await dispatchResponseMessage.Content.ReadFromJsonAsync<PluginOperationHttpResponse>();

        Assert.Equal(HttpStatusCode.OK, dispatchResponseMessage.StatusCode);
        Assert.NotNull(dispatchResponse);
        Assert.True(dispatchResponse!.Success);
        Assert.Equal(correlationId, dispatchResponse.CorrelationId);
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
}
