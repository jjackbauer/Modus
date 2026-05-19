namespace Modus.Core.Plugins;

public interface IPluginScheduledEvents
{
    void RegisterSchedules(IPluginScheduler scheduler);
}