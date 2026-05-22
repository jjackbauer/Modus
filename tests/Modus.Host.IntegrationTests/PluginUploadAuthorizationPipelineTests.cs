using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Modus.Host.Hosting;
using Modus.Host.Plugins.Authorization;
using System.Security.Cryptography;
using Xunit;

namespace Modus.Host.IntegrationTests;

public sealed class PluginUploadAuthorizationPipelineTests
{
    [Fact]
    [Trait("ChecklistItem", "Implement asymmetric signature verification pipeline for plugin upload authorization [mandatory - authorized plugin author]")]
    public async Task VerifyPluginUploadSignature_GivenTrustedPublicKeyAndValidSignature_ReturnsAuthorized()
    {
        var packageBytes = "signed-plugin-payload"u8.ToArray();
        using var signingKey = RSA.Create(2048);
        var signatureBytes = signingKey.SignData(packageBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        var options = new PluginUploadAuthorizationOptions();
        options.TrustedAuthorKeys.Add(
            new TrustedPluginAuthorKey(
                keyId: "trusted-author",
                publicKeyPem: signingKey.ExportRSAPublicKeyPem()));

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(options);
        builder.Services.AddModusPluginHostingRuntime();

        await using var app = builder.Build();
        var pipeline = app.Services.GetRequiredService<PluginUploadAuthorizationPipeline>();
        var request = new PluginUploadSignatureVerificationRequest("plugin.bundle.zip", packageBytes, signatureBytes);
        var extractionStarted = false;

        var result = await pipeline.AuthorizeAndContinueAsync(
            request,
            _ =>
            {
                extractionStarted = true;
                return ValueTask.CompletedTask;
            });

        Assert.True(result.IsAuthorized);
        Assert.True(result.CanProceedToExtraction);
        Assert.Equal("trusted-author", result.TrustedKeyId);
        Assert.Null(result.FailureReason);
        Assert.True(extractionStarted);
    }

    [Fact]
    [Trait("ChecklistItem", "Implement asymmetric signature verification pipeline for plugin upload authorization [mandatory - authorized plugin author]")]
    public async Task VerifyPluginUploadSignature_GivenInvalidSignature_ReturnsUnauthorized()
    {
        var packageBytes = "signed-plugin-payload"u8.ToArray();
        using var trustedAuthorKey = RSA.Create(2048);
        using var untrustedSigner = RSA.Create(2048);
        var signatureBytes = untrustedSigner.SignData(packageBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        var options = new PluginUploadAuthorizationOptions();
        options.TrustedAuthorKeys.Add(
            new TrustedPluginAuthorKey(
                keyId: "trusted-author",
                publicKeyPem: trustedAuthorKey.ExportRSAPublicKeyPem()));

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(options);
        builder.Services.AddModusPluginHostingRuntime();

        await using var app = builder.Build();
        var pipeline = app.Services.GetRequiredService<PluginUploadAuthorizationPipeline>();
        var request = new PluginUploadSignatureVerificationRequest("plugin.bundle.zip", packageBytes, signatureBytes);
        var extractionStarted = false;

        var result = await pipeline.AuthorizeAndContinueAsync(
            request,
            _ =>
            {
                extractionStarted = true;
                return ValueTask.CompletedTask;
            });

        Assert.False(result.IsAuthorized);
        Assert.False(result.CanProceedToExtraction);
        Assert.Null(result.TrustedKeyId);
        Assert.Equal("Plugin upload signature did not match any trusted author key.", result.FailureReason);
        Assert.False(extractionStarted);
    }
}