using Microsoft.Extensions.DependencyInjection;

namespace Wip.Builder;

public static class ServiceCollectionExtensions
{
    public static WipBuilder AddWipCapabilities(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return new WipBuilder(services);
    }

    public static WipBuilder AddWipCapabilities(this IServiceCollection services, Action<WipBuilderOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new WipBuilderOptions();
        configure(options);
        return new WipBuilder(services, options);
    }

    public static WipBuilder AddWipBuilder(this IServiceCollection services)
    {
        return services.AddWipCapabilities();
    }

    public static WipBuilder AddWipBuilder(this IServiceCollection services, Action<WipBuilderOptions> configure)
    {
        return services.AddWipCapabilities(configure);
    }
}
