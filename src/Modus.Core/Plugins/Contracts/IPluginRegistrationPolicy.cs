namespace Modus.Core.Plugins;

public interface IPluginRegistrationPolicy
{
    IReadOnlyList<PluginRegistrationStep> BuildRegistrationPlan(IPluginContract plugin);
}
