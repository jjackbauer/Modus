namespace Modus.Core.Plugins;

public interface IPluginScheduler
{
    void ScheduleRecurring(string jobName, TimeSpan interval, string operation);

    void ScheduleAt(string jobName, DateTimeOffset runAt, string operation);
}