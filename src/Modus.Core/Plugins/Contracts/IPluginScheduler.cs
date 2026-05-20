namespace Modus.Core.Plugins;

public interface IPluginScheduler
{
    void ScheduleRecurring(JobName jobName, TimeSpan interval, OperationName operation);

    void ScheduleAt(JobName jobName, DateTimeOffset runAt, OperationName operation);
}