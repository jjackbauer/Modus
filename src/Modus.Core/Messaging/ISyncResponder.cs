namespace Modus.Core.Messaging;

public interface ISyncResponder
{
    SyncResponse Handle(SyncRequest request);
}
