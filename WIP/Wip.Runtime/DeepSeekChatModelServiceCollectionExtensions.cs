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

        return services.AddDeepSeekChatModelCore();
    }

    /// <summary>
    /// Registers the DeepSeek <see cref="IChatModel"/> adapter resolving
    /// <see cref="DeepSeekModelProviderOptions"/> from the service provider.
    /// </summary>
    public static IServiceCollection AddDeepSeekChatModel(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services.AddDeepSeekChatModelCore();
    }

    private static IServiceCollection AddDeepSeekChatModelCore(this IServiceCollection services)
    {
        services.TryAddSingleton(static _ => new HttpClient());
        services.TryAddSingleton(static serviceProvider => new DeepSeekChatModel(
            serviceProvider.GetRequiredService<HttpClient>(),
            serviceProvider.GetRequiredService<DeepSeekModelProviderOptions>()));

        // IChatModel resolves the same concrete singleton instance so hosts can decorate it.
        services.TryAddSingleton<IChatModel>(static serviceProvider =>
            serviceProvider.GetRequiredService<DeepSeekChatModel>());

        return services;
    }
}
