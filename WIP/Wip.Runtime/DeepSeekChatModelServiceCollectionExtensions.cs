using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Wip.Abstractions.Chat;
using Wip.Runtime.Runtime;

namespace Wip.Runtime;

/// <summary>
/// Host registration for the DeepSeek <see cref="IChatModel"/> adapter (WP-3.5).
/// </summary>
public static class DeepSeekChatModelServiceCollectionExtensions
{
    public static IServiceCollection AddDeepSeekChatModel(
        this IServiceCollection services,
        DeepSeekModelProviderOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        services.AddSingleton(options);
        services.TryAddSingleton(static _ => new HttpClient());
        services.TryAddSingleton<IChatModel>(static serviceProvider => new DeepSeekChatModel(
            serviceProvider.GetRequiredService<HttpClient>(),
            serviceProvider.GetRequiredService<DeepSeekModelProviderOptions>()));

        return services;
    }
}
