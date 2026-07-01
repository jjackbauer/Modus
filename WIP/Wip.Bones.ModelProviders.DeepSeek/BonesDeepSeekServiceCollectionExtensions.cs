using Microsoft.Extensions.DependencyInjection;
using Wip.Abstractions.Capabilities;
using Wip.Bones.Agents.Enhance;
using Wip.Bones.Agents.Play;
using Wip.Bones.Agents.Ponder;
using Wip.Runtime.Runtime;

namespace Wip.Bones.ModelProviders.DeepSeek;

public static class BonesDeepSeekServiceCollectionExtensions
{
    public static IServiceCollection AddBonesDeepSeekModelProviders(
        this IServiceCollection services,
        BonesDeepSeekProviderOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        services.AddSingleton(options);
        services.AddSingleton(CreateDeepSeekModelProviderOptions(options));
        services.AddSingleton(static _ => new HttpClient());
        services.AddSingleton<DeepSeekModelProvider>();
        services.AddSingleton<IModelProvider<BonesStrategyAuthoringRequest, BonesStrategyAuthoringResult>, DeepSeekBonesStrategyAuthoringProvider>();
        services.AddSingleton<IModelProvider<BonesPlayTurnRequest, BonesPlayTurnResult>, DeepSeekBonesPlayTurnProvider>();
        services.AddSingleton<IModelProvider<BonesStrategyEnhancementRequest, BonesStrategyEnhancementResult>, DeepSeekBonesStrategyEnhancementProvider>();

        return services;
    }

    private static DeepSeekModelProviderOptions CreateDeepSeekModelProviderOptions(BonesDeepSeekProviderOptions options) =>
        new(
            baseUrl: options.BaseUrl,
            apiKey: options.ApiKeySource,
            timeout: options.Timeout);
}