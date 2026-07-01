using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Wip.Bones.Host.Tests;

public sealed class BonesPerStageModelRoutingStatusApiTests
{
    private const string StatusApiItem =
        "Extend `/api/bones/status` response `modelProvider` object (or add a new `perStageModels` field) with `ponderModelId`, `playModelId`, `enhanceModelId` — each null when using the fallback [depends on BonesHostOptions properties] [mandatory - observability]";

    [Fact]
    [Trait("ChecklistItem", StatusApiItem)]
    public async Task BonesStatusApi_GivenPerStageModelsSet_ExpectedResponseIncludesPerStageModels()
    {
        await using var factory = new BonesHostWebApplicationFactory(configureConfiguration: settings =>
        {
            settings["BonesHost:ModelProvider"] = "stub";
            settings["BonesHost:PonderModelId"] = "deepseek-v4-pro";
            settings["BonesHost:PlayModelId"] = "deepseek-v4-flash";
            // EnhanceModelId not set — should be null (omitted) in response
        });

        using var client = factory.CreateClient();

        var status = await BonesHostStatusPolling.WaitForStatusAsync(
            client,
            response => response.IterationCount >= 1,
            TimeSpan.FromSeconds(120));

        var rawResponse = await client.GetStringAsync("/api/bones/status");
        using var doc = JsonDocument.Parse(rawResponse);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("perStageModels", out var perStageModels),
            "Status response must include perStageModels field");

        // ponderModelId should be present and set
        Assert.True(perStageModels.TryGetProperty("ponderModelId", out var ponderProp),
            "perStageModels.ponderModelId must be present when configured");
        Assert.Equal("deepseek-v4-pro", ponderProp.GetString());

        // playModelId should be present and set
        Assert.True(perStageModels.TryGetProperty("playModelId", out var playProp),
            "perStageModels.playModelId must be present when configured");
        Assert.Equal("deepseek-v4-flash", playProp.GetString());

        // enhanceModelId should be absent (omitted due to WhenWritingNull) when not configured
        Assert.False(perStageModels.TryGetProperty("enhanceModelId", out _),
            "perStageModels.enhanceModelId must be absent when not configured");
    }

    [Fact]
    [Trait("ChecklistItem", StatusApiItem)]
    public async Task BonesStatusApi_GivenNoPerStageModels_ExpectedPerStageModelsEmpty()
    {
        await using var factory = new BonesHostWebApplicationFactory(configureConfiguration: settings =>
        {
            settings["BonesHost:ModelProvider"] = "stub";
        });

        using var client = factory.CreateClient();

        var status = await BonesHostStatusPolling.WaitForStatusAsync(
            client,
            response => response.IterationCount >= 1,
            TimeSpan.FromSeconds(120));

        var rawResponse = await client.GetStringAsync("/api/bones/status");
        using var doc = JsonDocument.Parse(rawResponse);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("perStageModels", out var perStageModels),
            "Status response must include perStageModels field");

        // When no per-stage models are configured, all should be absent
        // (omitted by WhenWritingNull since they are null)
        Assert.False(perStageModels.TryGetProperty("ponderModelId", out _),
            "ponderModelId must be absent when null");
        Assert.False(perStageModels.TryGetProperty("playModelId", out _),
            "playModelId must be absent when null");
        Assert.False(perStageModels.TryGetProperty("enhanceModelId", out _),
            "enhanceModelId must be absent when null");
    }
}
