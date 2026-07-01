using Microsoft.Extensions.Configuration;
using Wip.Bones.Host;
using Wip.Bones.Identifiers;
using Xunit;

namespace Wip.Bones.Host.Tests;

public sealed class BonesHostOptionsDeepSeekTests
{
    public BonesHostOptionsDeepSeekTests() => ClearBonesHostEnvironment();

    private static void ClearBonesHostEnvironment()
    {
        Environment.SetEnvironmentVariable("BonesHost__ModelProvider", null);
        Environment.SetEnvironmentVariable("BonesHost__DeepSeek__BaseUrl", null);
        Environment.SetEnvironmentVariable("BonesHost__DeepSeek__Model", null);
        Environment.SetEnvironmentVariable("BonesHost__DeepSeek__TimeoutSeconds", null);
        Environment.SetEnvironmentVariable("BonesHost__DeepSeek__ApiKeySource", null);
        Environment.SetEnvironmentVariable("BONES_MAX_GAMES_PER_RUN", null);
        Environment.SetEnvironmentVariable("BonesHost__MaxGamesPerRun", null);
        Environment.SetEnvironmentVariable("BONES_RESUME_FROM_BEST", null);
        Environment.SetEnvironmentVariable("BonesHost__ResumeFromBest", null);
    }

    private const string OptionsBindingItem = BonesDeepSeekHostRequirementsChecklistItems.BonesHostOptionsModelProvider;

    [Fact]
    [Trait("ChecklistItem", OptionsBindingItem)]
    public void BonesHostOptions_GivenDeepSeekConfiguration_ExpectedBindsModelProviderKindAndNestedDeepSeekOptions()
    {
        ClearBonesHostEnvironment();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BonesHost:ModelProvider"] = "deepseek",
                ["BonesHost:DeepSeek:BaseUrl"] = "https://api.deepseek.com",
                ["BonesHost:DeepSeek:Model"] = "deepseek-reasoner",
                ["BonesHost:DeepSeek:TimeoutSeconds"] = "45",
                ["BonesHost:DeepSeek:ApiKeySource"] = "env:DEEPSEEK_API_KEY",
            })
            .Build();

        var options = BonesHostOptions.Bind(configuration);

        Assert.Equal(BonesModelProviderKind.DeepSeek, options.ModelProvider);
        Assert.NotNull(options.DeepSeekProviderOptions);
        Assert.Equal("https://api.deepseek.com/", options.DeepSeekProviderOptions.BaseUrl.ToString());
        Assert.Equal("deepseek-reasoner", options.DeepSeekProviderOptions.Model.Value);
        Assert.Equal(TimeSpan.FromSeconds(45), options.DeepSeekProviderOptions.Timeout);
        Assert.Equal("DEEPSEEK_API_KEY", options.DeepSeekProviderOptions.ApiKeySourceReference);
        Assert.Equal("deepseek-reasoner", options.DefaultParameters.ModelId);
    }

    [Fact]
    [Trait("ChecklistItem", OptionsBindingItem)]
    public void BonesHostOptions_GivenMissingDeepSeekSectionWhenProviderIsDeepSeek_ExpectedDeterministicStartupValidationFailure()
    {
        ClearBonesHostEnvironment();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BonesHost:ModelProvider"] = "deepseek",
            })
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(() => BonesHostOptions.Bind(configuration));

        Assert.Contains("DeepSeek settings are required", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", OptionsBindingItem)]
    public void BonesHostOptions_GivenStubConfiguration_ExpectedDoesNotRequireDeepSeekSection()
    {
        ClearBonesHostEnvironment();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BonesHost:ModelProvider"] = "stub",
            })
            .Build();

        var options = BonesHostOptions.Bind(configuration);

        Assert.Equal(BonesModelProviderKind.Stub, options.ModelProvider);
        Assert.Null(options.DeepSeekProviderOptions);
        Assert.Null(options.DefaultParameters.ModelId);
    }

    [Fact]
    [Trait("ChecklistItem", BonesHostRequirementsChecklistItems.MaxGamesPerRunOption)]
    public void BonesHostOptions_GivenBonesMaxGamesPerRunEnv_ExpectedBindsMaxGamesPerRun()
    {
        ClearBonesHostEnvironment();
        try
        {
            Environment.SetEnvironmentVariable("BONES_MAX_GAMES_PER_RUN", "25");

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["BonesHost:ModelProvider"] = "stub",
                    ["BonesHost:MaxGamesPerRun"] = "5",
                })
                .Build();

            var options = BonesHostOptions.Bind(configuration);

            Assert.Equal(25, options.MaxGamesPerRun);
        }
        finally
        {
            ClearBonesHostEnvironment();
        }
    }

    [Fact]
    [Trait("ChecklistItem", BonesHostRequirementsChecklistItems.MaxGamesPerRunOption)]
    public void BonesHostOptions_GivenZeroMaxGamesPerRun_ExpectedTreatsAsUnlimited()
    {
        ClearBonesHostEnvironment();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BonesHost:ModelProvider"] = "stub",
                ["BonesHost:MaxGamesPerRun"] = "0",
            })
            .Build();

        var options = BonesHostOptions.Bind(configuration);

        Assert.Equal(0, options.MaxGamesPerRun);
    }

    [Fact]
    [Trait("ChecklistItem", BonesHostRequirementsChecklistItems.MaxGamesPerRunOption)]
    public void BonesHostOptions_GivenNegativeMaxGamesPerRun_ExpectedDeterministicValidationFailure()
    {
        ClearBonesHostEnvironment();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BonesHost:ModelProvider"] = "stub",
                ["BonesHost:MaxGamesPerRun"] = "-1",
            })
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(() => BonesHostOptions.Bind(configuration));

        Assert.Contains("MaxGamesPerRun", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", BonesHostRequirementsChecklistItems.ResumeFromBestOption)]
    public void BonesHostOptions_GivenBonesResumeFromBestEnvFalse_ExpectedResumeFromBestDisabled()
    {
        ClearBonesHostEnvironment();
        try
        {
            Environment.SetEnvironmentVariable("BONES_RESUME_FROM_BEST", "false");

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["BonesHost:ModelProvider"] = "stub",
                    ["BonesHost:ResumeFromBest"] = "true",
                })
                .Build();

            var options = BonesHostOptions.Bind(configuration);

            Assert.False(options.ResumeFromBest);
        }
        finally
        {
            ClearBonesHostEnvironment();
        }
    }
}