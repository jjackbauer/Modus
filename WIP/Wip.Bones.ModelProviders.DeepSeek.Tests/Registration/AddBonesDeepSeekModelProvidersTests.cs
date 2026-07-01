using Microsoft.Extensions.DependencyInjection;
using Wip.Abstractions.Capabilities;
using Wip.Bones.Agents.Enhance;
using Wip.Bones.Agents.Play;
using Wip.Bones.Agents.Ponder;
using Wip.Bones.ModelProviders.DeepSeek;
using Wip.Runtime.Runtime;
using Xunit;

namespace Wip.Bones.ModelProviders.DeepSeek.Tests.Registration;

public sealed class AddBonesDeepSeekModelProvidersTests
{
    private const string ChecklistItem =
        "Add `AddBonesDeepSeekModelProviders(IServiceCollection, BonesDeepSeekProviderOptions)` extension registering `DeepSeekModelProviderOptions`, `HttpClient`, inner `DeepSeekModelProvider`, and all three Bones adapter singletons [depends on all three adapters]";

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void AddBonesDeepSeekModelProviders_GivenServiceCollection_ExpectedResolvesAllThreeBonesModelProviderInterfaces()
    {
        var services = new ServiceCollection();
        var options = CreateTestOptions();

        services.AddBonesDeepSeekModelProviders(options);

        using var provider = services.BuildServiceProvider();

        var strategyProvider = provider.GetRequiredService<IModelProvider<BonesStrategyAuthoringRequest, BonesStrategyAuthoringResult>>();
        var playTurnProvider = provider.GetRequiredService<IModelProvider<BonesPlayTurnRequest, BonesPlayTurnResult>>();
        var enhancementProvider = provider.GetRequiredService<IModelProvider<BonesStrategyEnhancementRequest, BonesStrategyEnhancementResult>>();
        var deepSeekModelProvider = provider.GetRequiredService<DeepSeekModelProvider>();
        var deepSeekOptions = provider.GetRequiredService<DeepSeekModelProviderOptions>();
        var httpClient = provider.GetRequiredService<HttpClient>();
        var bonesOptions = provider.GetRequiredService<BonesDeepSeekProviderOptions>();

        Assert.IsType<DeepSeekBonesStrategyAuthoringProvider>(strategyProvider);
        Assert.IsType<DeepSeekBonesPlayTurnProvider>(playTurnProvider);
        Assert.IsType<DeepSeekBonesStrategyEnhancementProvider>(enhancementProvider);
        Assert.NotNull(deepSeekModelProvider);
        Assert.NotNull(httpClient);
        Assert.Same(options, bonesOptions);
        Assert.Equal(options.BaseUrl, deepSeekOptions.BaseUrl);
        Assert.Equal(options.ApiKeySource, deepSeekOptions.ApiKey);
        Assert.Equal(options.Timeout, deepSeekOptions.Timeout);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void AddBonesDeepSeekModelProviders_GivenDuplicateRegistration_ExpectedSingleSingletonInstancePerInterface()
    {
        var services = new ServiceCollection();
        services.AddBonesDeepSeekModelProviders(CreateTestOptions());

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scopeA = provider.CreateScope();
        using var scopeB = provider.CreateScope();

        var strategyA1 = provider.GetRequiredService<IModelProvider<BonesStrategyAuthoringRequest, BonesStrategyAuthoringResult>>();
        var strategyA2 = scopeA.ServiceProvider.GetRequiredService<IModelProvider<BonesStrategyAuthoringRequest, BonesStrategyAuthoringResult>>();
        var strategyB = scopeB.ServiceProvider.GetRequiredService<IModelProvider<BonesStrategyAuthoringRequest, BonesStrategyAuthoringResult>>();

        var playTurnA1 = provider.GetRequiredService<IModelProvider<BonesPlayTurnRequest, BonesPlayTurnResult>>();
        var playTurnA2 = provider.GetRequiredService<IModelProvider<BonesPlayTurnRequest, BonesPlayTurnResult>>();

        var enhancementA1 = provider.GetRequiredService<IModelProvider<BonesStrategyEnhancementRequest, BonesStrategyEnhancementResult>>();
        var enhancementA2 = provider.GetRequiredService<IModelProvider<BonesStrategyEnhancementRequest, BonesStrategyEnhancementResult>>();

        var deepSeekA1 = provider.GetRequiredService<DeepSeekModelProvider>();
        var deepSeekA2 = scopeA.ServiceProvider.GetRequiredService<DeepSeekModelProvider>();

        var httpClientA1 = provider.GetRequiredService<HttpClient>();
        var httpClientA2 = provider.GetRequiredService<HttpClient>();

        Assert.Same(strategyA1, strategyA2);
        Assert.Same(strategyA1, strategyB);
        Assert.Same(playTurnA1, playTurnA2);
        Assert.Same(enhancementA1, enhancementA2);
        Assert.Same(deepSeekA1, deepSeekA2);
        Assert.Same(httpClientA1, httpClientA2);
    }

    private static BonesDeepSeekProviderOptions CreateTestOptions() =>
        BonesDeepSeekProviderOptions.Create(
            baseUrl: "https://api.deepseek.com/v1/",
            model: "deepseek-chat",
            timeout: TimeSpan.FromSeconds(30),
            apiKeySource: "env:DEEPSEEK_API_KEY");
}