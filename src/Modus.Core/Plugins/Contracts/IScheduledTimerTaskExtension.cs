namespace Modus.Core.Plugins;

using Modus.Core.Messaging;

public interface IScheduledTimerTaskExtension : IPluginOperationCatalog, IPluginScheduledEvents, ISyncResponder<SyncRequest, SyncResponse<ISyncPayload>>
{
	new IReadOnlyCollection<OperationName> SupportedOperations { get; }

	new void RegisterSchedules(IPluginScheduler scheduler);

	new SyncResponse<ISyncPayload> Handle(SyncRequest request);
}