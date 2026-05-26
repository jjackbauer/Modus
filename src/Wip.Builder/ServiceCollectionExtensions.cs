using Microsoft.Extensions.DependencyInjection;

namespace Wip.Builder;

public static class ServiceCollectionExtensions
{
    public static WipBuilder AddWipBuilder(this IServiceCollection services)
    {
        return new WipBuilder(services);
    }
}
