using Microsoft.Extensions.DependencyInjection;

namespace Modus.Core.Plugins;

public interface IPluginDependencyRegister
{
    void Register(IServiceCollection services);
}