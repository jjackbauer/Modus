using Microsoft.Extensions.Configuration;
using Wip.Bones.Host;
using Wip.Bones.Identifiers;
using Xunit;

namespace Wip.Bones.Host.Tests;

public sealed class BonesHostOptionsPerStageModelTests
{
    private const string PonderPropertyItem =
        "Add `PonderModelId` (nullable string), `PlayModelId` (nullable string), and `EnhanceModelId` (nullable string) properties to `BonesHostOptions` — each defaults to `null` so the global model is used when not overridden [foundation]";

    private const string PonderEnvItem =
        "Resolve `PonderModelId` from `BONES_PONDER_MODEL` env var and `BonesHost:PonderModelId` config path in `Bind()` [depends on properties]";

    private const string PlayEnvItem =
        "Resolve `PlayModelId` from `BONES_PLAY_MODEL` env var and `BonesHost:PlayModelId` config path in `Bind()` [depends on properties]";

    private const string EnhanceEnvItem =
        "Resolve `EnhanceModelId` from `BONES_ENHANCE_MODEL` env var and `BonesHost:EnhanceModelId` config path in `Bind()` [depends on properties]";

    private const string DefaultParamsWiringItem =
        "Wire per-stage model IDs into `DefaultParameters` in `Bind()` so they flow into session task descriptions [depends on resolution]";

    private const string DiagnosticsItem =
        "Add per-stage model diagnostics to `GetStartupDiagnostics()` — show `ponderModel=<value>` when `PonderModelId` is set, same for `playModel` and `enhanceModel` [depends on properties]";

    public BonesHostOptionsPerStageModelTests() => ClearBonesHostEnvironment();

    private static void ClearBonesHostEnvironment()
    {
        Environment.SetEnvironmentVariable("BONES_PONDER_MODEL", null);
        Environment.SetEnvironmentVariable("BONES_PLAY_MODEL", null);
        Environment.SetEnvironmentVariable("BONES_ENHANCE_MODEL", null);
        Environment.SetEnvironmentVariable("BonesHost__ModelProvider", null);
        Environment.SetEnvironmentVariable("BonesHost__DeepSeek__BaseUrl", null);
        Environment.SetEnvironmentVariable("BonesHost__DeepSeek__Model", null);
        Environment.SetEnvironmentVariable("BonesHost__DeepSeek__TimeoutSeconds", null);
        Environment.SetEnvironmentVariable("BonesHost__DeepSeek__ApiKeySource", null);
    }

    [Fact]
    [Trait("ChecklistItem", PonderPropertyItem)]
    public void BonesHostOptions_GivenBonesPonderModelEnv_ExpectedBindsPonderModelId()
    {
        ClearBonesHostEnvironment();
        try
        {
            Environment.SetEnvironmentVariable("BONES_PONDER_MODEL", "deepseek-v4-pro");

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["BonesHost:ModelProvider"] = "stub",
                })
                .Build();

            var options = BonesHostOptions.Bind(configuration);

            Assert.Equal("deepseek-v4-pro", options.PonderModelId);
            Assert.Null(options.PlayModelId);
            Assert.Null(options.EnhanceModelId);
        }
        finally
        {
            ClearBonesHostEnvironment();
        }
    }

    [Fact]
    [Trait("ChecklistItem", PlayEnvItem)]
    public void BonesHostOptions_GivenBonesPlayModelEnv_ExpectedBindsPlayModelId()
    {
        ClearBonesHostEnvironment();
        try
        {
            Environment.SetEnvironmentVariable("BONES_PLAY_MODEL", "deepseek-v4-flash");

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["BonesHost:ModelProvider"] = "stub",
                })
                .Build();

            var options = BonesHostOptions.Bind(configuration);

            Assert.Equal("deepseek-v4-flash", options.PlayModelId);
        }
        finally
        {
            ClearBonesHostEnvironment();
        }
    }

    [Fact]
    [Trait("ChecklistItem", EnhanceEnvItem)]
    public void BonesHostOptions_GivenBonesEnhanceModelEnv_ExpectedBindsEnhanceModelId()
    {
        ClearBonesHostEnvironment();
        try
        {
            Environment.SetEnvironmentVariable("BONES_ENHANCE_MODEL", "deepseek-v4-pro");

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["BonesHost:ModelProvider"] = "stub",
                })
                .Build();

            var options = BonesHostOptions.Bind(configuration);

            Assert.Equal("deepseek-v4-pro", options.EnhanceModelId);
        }
        finally
        {
            ClearBonesHostEnvironment();
        }
    }

    [Fact]
    [Trait("ChecklistItem", PonderEnvItem)]
    public void BonesHostOptions_GivenConfigPathPonderModelId_ExpectedBindsPonderModelId()
    {
        ClearBonesHostEnvironment();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BonesHost:ModelProvider"] = "stub",
                ["BonesHost:PonderModelId"] = "deepseek-v4-pro",
            })
            .Build();

        var options = BonesHostOptions.Bind(configuration);

        Assert.Equal("deepseek-v4-pro", options.PonderModelId);
    }

    [Fact]
    [Trait("ChecklistItem", PonderPropertyItem)]
    public void BonesHostOptions_GivenNoPerStageModels_ExpectedAllThreeNull()
    {
        ClearBonesHostEnvironment();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BonesHost:ModelProvider"] = "stub",
            })
            .Build();

        var options = BonesHostOptions.Bind(configuration);

        Assert.Null(options.PonderModelId);
        Assert.Null(options.PlayModelId);
        Assert.Null(options.EnhanceModelId);
    }

    [Fact]
    [Trait("ChecklistItem", DefaultParamsWiringItem)]
    public void BonesHostOptions_GivenPerStageModels_ExpectedWiredIntoDefaultParameters()
    {
        ClearBonesHostEnvironment();
        try
        {
            Environment.SetEnvironmentVariable("BONES_PONDER_MODEL", "deepseek-v4-pro");
            Environment.SetEnvironmentVariable("BONES_PLAY_MODEL", "deepseek-v4-flash");

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["BonesHost:ModelProvider"] = "stub",
                    ["BonesHost:EnhanceModelId"] = "deepseek-v4-pro",
                })
                .Build();

            var options = BonesHostOptions.Bind(configuration);

            Assert.Equal("deepseek-v4-pro", options.DefaultParameters.PonderModelId);
            Assert.Equal("deepseek-v4-flash", options.DefaultParameters.PlayModelId);
            Assert.Equal("deepseek-v4-pro", options.DefaultParameters.EnhanceModelId);
        }
        finally
        {
            ClearBonesHostEnvironment();
        }
    }

    [Fact]
    [Trait("ChecklistItem", DiagnosticsItem)]
    public void BonesHostOptions_GivenPonderModelSet_ExpectedStartupDiagnosticsIncludesPonderModel()
    {
        ClearBonesHostEnvironment();
        try
        {
            Environment.SetEnvironmentVariable("BONES_PONDER_MODEL", "deepseek-v4-pro");

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["BonesHost:ModelProvider"] = "stub",
                })
                .Build();

            var options = BonesHostOptions.Bind(configuration);
            var diagnostics = options.GetStartupDiagnostics();

            Assert.Contains(diagnostics, d => d == "ponderModel=deepseek-v4-pro");
            Assert.DoesNotContain(diagnostics, d => d.StartsWith("playModel=", StringComparison.Ordinal));
            Assert.DoesNotContain(diagnostics, d => d.StartsWith("enhanceModel=", StringComparison.Ordinal));
        }
        finally
        {
            ClearBonesHostEnvironment();
        }
    }
}
