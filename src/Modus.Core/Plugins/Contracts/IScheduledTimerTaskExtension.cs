namespace Modus.Core.Plugins;

using Modus.Core.Messaging;

public interface IScheduledTimerTaskExtension : IPluginOperationCatalog, IPluginScheduledEvents, ISyncResponder
{
	new IReadOnlyCollection<OperationName> SupportedOperations { get; }

	new void RegisterSchedules(IPluginScheduler scheduler);

	new SyncResponse Handle(SyncRequest request);
}