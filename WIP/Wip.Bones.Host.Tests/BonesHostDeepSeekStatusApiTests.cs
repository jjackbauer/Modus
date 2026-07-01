using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Wip.Bones.Host;
using Xunit;

namespace Wip.Bones.Host.Tests;

public sealed class BonesHostDeepSeekStatusApiTests
{
    private const string DiagnosticsItem = BonesDeepSeekHostRequirementsChecklistItems.BonesHostModelProviderDiagnostics;

    [Fact]
    [Trait("ChecklistItem", DiagnosticsItem)]
    public async Task BonesStatusApi_GivenDeepSeekProvider_ExpectedReportsProviderModelBaseUrlAndKeySourceWithoutSecret()
    {
        Environment.SetEnvironmentVariable("WIP_BONES_HOST_TEST_API_KEY", "super-secret-value");
        try
        {
            await using var factory = new BonesHostWebApplicationFactory(
                httpMessageHandler: new StubDeepSeekHttpMessageHandler(),
                configureConfiguration: settings =>
                {
                    settings["BonesHost:ModelProvider"] = "deepseek";
                    settings["BonesHost:DeepSeek:BaseUrl"] = "https://api.deepseek.com";
                    settings["BonesHost:DeepSeek:Model"] = "deepseek-chat";
                    settings["BonesHost:DeepSeek:TimeoutSeconds"] = "30";
                    settings["BonesHost:DeepSeek:ApiKeySource"] = "env:WIP_BONES_HOST_TEST_API_KEY";
                });

            using var client = factory.CreateClient();
            var response = await client.GetAsync("/api/bones/status");
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(json);
            var modelProvider = document.RootElement.GetProperty("modelProvider");

            Assert.Equal("deepseek", modelProvider.GetProperty("provider").GetString());
            Assert.Equal("deepseek-chat", modelProvider.GetProperty("model").GetString());
            Assert.Equal("https://api.deepseek.com/", modelProvider.GetProperty("baseUrl").GetString());
            Assert.Equal(30, modelProvider.GetProperty("timeoutSeconds").GetInt32());
            Assert.Equal(
                "environment:WIP_BONES_HOST_TEST_API_KEY",
                modelProvider.GetProperty("apiKeySource").GetString());
            Assert.DoesNotContain("super-secret-value", json, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("WIP_BONES_HOST_TEST_API_KEY", null);
        }
    }

    [Fact]
    [Trait("ChecklistItem", DiagnosticsItem)]
    public async Task BonesStatusApi_GivenStubProvider_ExpectedReportsProviderStub()
    {
        await using var factory = new BonesHostWebApplicationFactory();

        using var client = factory.CreateClient();
        var response = await client.GetAsync("/api/bones/status");
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var modelProvider = document.RootElement.GetProperty("modelProvider");

        Assert.Equal("stub", modelProvider.GetProperty("provider").GetString());
        Assert.False(modelProvider.TryGetProperty("model", out _));
        Assert.False(modelProvider.TryGetProperty("baseUrl", out _));
        Assert.False(modelProvider.TryGetProperty("timeoutSeconds", out _));
        Assert.False(modelProvider.TryGetProperty("apiKeySource", out _));
    }

    [Fact]
    [Trait("ChecklistItem", DiagnosticsItem)]
    public void BonesHostOptions_GivenDeepSeekConfiguration_ExpectedStartupDiagnosticsExcludeSecrets()
    {
        Environment.SetEnvironmentVariable("WIP_BONES_HOST_TEST_API_KEY", "super-secret-value");
        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["BonesHost:ModelProvider"] = "deepseek",
                    ["BonesHost:DeepSeek:BaseUrl"] = "https://api.deepseek.com",
                    ["BonesHost:DeepSeek:Model"] = "deepseek-chat",
                    ["BonesHost:DeepSeek:TimeoutSeconds"] = "30",
                    ["BonesHost:DeepSeek:ApiKeySource"] = "env:WIP_BONES_HOST_TEST_API_KEY",
                })
                .Build();

            var options = BonesHostOptions.Bind(configuration);
            var diagnostics = options.GetStartupDiagnostics();

            Assert.Contains(diagnostics, line => line == "modelProvider=deepseek");
            Assert.Contains(diagnostics, line => line == "modelProvider.deepseek.model=deepseek-chat");
            Assert.Contains(
                diagnostics,
                line => line == "modelProvider.deepseek.apiKeySource=environment:WIP_BONES_HOST_TEST_API_KEY");
            Assert.DoesNotContain(diagnostics, line => line.Contains("super-secret-value", StringComparison.Ordinal));
        }
        finally
        {
            Environment.SetEnvironmentVariable("WIP_BONES_HOST_TEST_API_KEY", null);
        }
    }

    private sealed class StubDeepSeekHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "id": "chatcmpl-status-test",
                      "object": "chat.completion",
                      "model": "deepseek-chat",
                      "choices": [
                        {
                          "index": 0,
                          "message": { "role": "assistant", "content": "# Rules\n- status test" },
                          "finish_reason": "stop"
                        }
                      ],
                      "usage": { "prompt_tokens": 1, "completion_tokens": 1, "total_tokens": 2 }
                    }
                    """,
                    System.Text.Encoding.UTF8,
                    "application/json"),
            });
    }
}