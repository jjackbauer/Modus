using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Modus.Core.Messaging;
using Modus.Host.Domain.WebApi;
using Modus.Host.Hosting;
using Modus.Host.Plugins.Authorization;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace Modus.Host.IntegrationTests;

public sealed class PluginLoadingTutorialRuntimeValidationTests
{
    private const string ChecklistItem = "Add integration tests that execute the tutorial steps against a running host and prove runtime behavior for each endpoint stage (upload, activation, invocation, failure) [mandatory - behavior-proof tutorial validation]";

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    [Trait("ChecklistItem", "Remove or justify the redundant package dependency path producing NU1510 while preserving host runtime composition behavior [depends on project dependency graph audit]")]
    [Trait("AuditArtifact", "iterative-implementation-tutorial-runtime-validation-stages-2026-05-22")]
    public async Task TutorialRuntimeValidation_GivenDocumentedCommandSequence_ExpectedUploadActivationInvocationAndFailurePathsAllExecutable()
    {
        var validPackage = CreateArchiveWithPluginAssembly();
        var invalidPackage = CreateArchiveWithoutAssemblies();

        using var trustedAuthorKey = RSA.Create(2048);
        var validSignature = trustedAuthorKey.SignData(validPackage, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var invalidSignature = trustedAuthorKey.SignData(invalidPackage, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        var options = new PluginUploadAuthorizationOptions();
        options.TrustedAuthorKeys.Add(new TrustedPluginAuthorKey("trusted-author", trustedAuthorKey.ExportRSAPublicKeyPem()));

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(options);
        builder.Services.AddModusPluginHostingRuntime();

        await using var app = builder.Build();
        app.Services.GetRequiredService<ManagementPluginUploadsEndpointMapper>().Map(app);
        app.Services.GetRequiredService<ManagementStatusEndpointMapper>().Map(app);
        app.Services.GetRequiredService<ManagementPluginCapabilitiesEndpointMapper>().Map(app);
        app.Services.GetRequiredService<PluginEndpointMapper>().Map(app);
        await app.StartAsync();

        var client = app.GetTestClient();

        using var validUploadRequest = BuildUploadRequest(validPackage, validSignature);
        var validUploadResponse = await client.PostAsync("/management/plugins/uploads", validUploadRequest);
        var validUploadPayload = await validUploadResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.Accepted, validUploadResponse.StatusCode);
        Assert.NotNull(validUploadResponse.Headers.Location);
        Assert.True(validUploadPayload.TryGetProperty("operationId", out _));
        Assert.Equal("Queued", validUploadPayload.GetProperty("status").GetString());

        var successfulUpload = await WaitForTerminalUploadStatusAsync(client, validUploadResponse.Headers.Location!.ToString());
        Assert.True(successfulUpload.IsSuccess);
        Assert.Equal("Completed", successfulUpload.Stage);

        var activatedPluginId = ExtractActivatedPluginId(successfulUpload.Diagnostics);
        Assert.False(string.IsNullOrWhiteSpace(activatedPluginId));

        var statusResponse = await client.GetAsync("/management/status");
        var statusPayload = await statusResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);
        Assert.Contains(
            statusPayload.GetProperty("loadedPlugins").EnumerateArray(),
            plugin => string.Equals(plugin.GetProperty("pluginId").GetString(), activatedPluginId, StringComparison.Ordinal)
                && string.Equals(plugin.GetProperty("lifecycleState").GetString(), "Active", StringComparison.Ordinal));

        var capabilitiesResponse = await client.GetAsync("/management/plugins/capabilities");
        var capabilitiesPayload = await capabilitiesResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, capabilitiesResponse.StatusCode);

        var pluginEntries = capabilitiesPayload.GetProperty("plugins").EnumerateArray().ToArray();
        var pluginCapabilities = Assert.Single(
            pluginEntries,
            plugin => string.Equals(plugin.GetProperty("pluginId").GetString(), activatedPluginId, StringComparison.Ordinal));
        var operation = Assert.Single(pluginCapabilities.GetProperty("operations").EnumerateArray().Select(static value => value.GetString()).Where(static value => !string.IsNullOrWhiteSpace(value)))!;

        var successInvocationResponse = await client.PostAsJsonAsync(
            $"/api/{activatedPluginId}/{operation}",
            new PluginOperationHttpRequest
            {
                CorrelationId = "tutorial-runtime-validation-success-corr",
                Payload = "{}"
            });
        var successInvocationPayload = await successInvocationResponse.Content.ReadFromJsonAsync<PluginOperationHttpResponse>();

        Assert.Equal(HttpStatusCode.OK, successInvocationResponse.StatusCode);
        Assert.NotNull(successInvocationPayload);
        Assert.True(successInvocationPayload!.Success);
        Assert.Equal(SyncResponseStatus.Success, successInvocationPayload.Status);
        Assert.Equal("tutorial-runtime-validation-success-corr", successInvocationPayload.CorrelationId);

        using var invalidUploadRequest = BuildUploadRequest(invalidPackage, invalidSignature);
        var invalidUploadResponse = await client.PostAsync("/management/plugins/uploads", invalidUploadRequest);
        var invalidUploadPayload = await invalidUploadResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.Accepted, invalidUploadResponse.StatusCode);
        Assert.NotNull(invalidUploadResponse.Headers.Location);
        Assert.True(invalidUploadPayload.TryGetProperty("operationId", out _));

        var failedUpload = await WaitForTerminalUploadStatusAsync(client, invalidUploadResponse.Headers.Location!.ToString());
        Assert.False(failedUpload.IsSuccess);
        Assert.Equal("Failed", failedUpload.Stage);
        Assert.Equal("No plugin assemblies were found in upload package.", failedUpload.FailureReason);
        Assert.Contains(
            failedUpload.Diagnostics,
            static diagnostic => string.Equals(diagnostic, "stage=validation outcome=failure reason=no-plugin-assemblies", StringComparison.Ordinal));

        var postFailureStatusResponse = await client.GetAsync("/management/status");
        var postFailureStatusPayload = await postFailureStatusResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, postFailureStatusResponse.StatusCode);
        Assert.Contains(
            postFailureStatusPayload.GetProperty("diagnostics").EnumerateArray().Select(static value => value.GetString()),
            diagnostic => string.Equals(diagnostic, "stage=validation outcome=failure reason=no-plugin-assemblies", StringComparison.Ordinal));
        Assert.Contains(
            postFailureStatusPayload.GetProperty("loadedPlugins").EnumerateArray(),
            plugin => string.Equals(plugin.GetProperty("pluginId").GetString(), activatedPluginId, StringComparison.Ordinal)
                && string.Equals(plugin.GetProperty("lifecycleState").GetString(), "Active", StringComparison.Ordinal));

        var failureInvocationResponse = await client.PostAsJsonAsync(
            "/api/Plugin.Owner.Mismatch/Owner.Check",
            new PluginOperationHttpRequest
            {
                CorrelationId = "tutorial-runtime-validation-failure-corr",
                Payload = "{}"
            });
        var failureInvocationPayload = await failureInvocationResponse.Content.ReadFromJsonAsync<PluginOperationHttpResponse>();

        Assert.Equal(HttpStatusCode.InternalServerError, failureInvocationResponse.StatusCode);
        Assert.NotNull(failureInvocationPayload);
        Assert.False(failureInvocationPayload!.Success);
        Assert.Equal(SyncResponseStatus.Failed, failureInvocationPayload.Status);
        Assert.Equal("tutorial-runtime-validation-failure-corr", failureInvocationPayload.CorrelationId);
        Assert.Contains("No runtime plugin operation owner found", failureInvocationPayload.Payload, StringComparison.Ordinal);
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
            var entry = archive.CreateEntry("plugins/readme.txt", CompressionLevel.Optimal);
            using var entryWriter = new StreamWriter(entry.Open());
            entryWriter.Write("invalid package for tutorial runtime failure stage");
        }

        return stream.ToArray();
    }

    private static async Task<UploadStatusSnapshot> WaitForTerminalUploadStatusAsync(HttpClient client, string statusPath)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        while (!timeout.IsCancellationRequested)
        {
            var response = await client.GetAsync(statusPath, timeout.Token);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var payload = await response.Content.ReadFromJsonAsync<JsonElement>(timeout.Token);
            var snapshot = new UploadStatusSnapshot(
                Stage: payload.GetProperty("stage").GetString() ?? string.Empty,
                IsTerminal: payload.GetProperty("isTerminal").GetBoolean(),
                IsSuccess: payload.GetProperty("isSuccess").GetBoolean(),
                FailureReason: payload.TryGetProperty("failureReason", out var failureReason) ? failureReason.GetString() : null,
                Diagnostics: payload.GetProperty("diagnostics").EnumerateArray().Select(static value => value.GetString() ?? string.Empty).ToArray());

            if (snapshot.IsTerminal)
            {
                return snapshot;
            }

            await Task.Delay(25, timeout.Token);
        }

        throw new TimeoutException($"Upload operation at '{statusPath}' did not reach terminal state.");
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

    private readonly record struct UploadStatusSnapshot(
        string Stage,
        bool IsTerminal,
        bool IsSuccess,
        string? FailureReason,
        string[] Diagnostics);
}