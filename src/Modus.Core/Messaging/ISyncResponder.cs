namespace Modus.Core.Messaging;

public interface ISyncResponder
{
    SyncResponse Handle(SyncRequest request);
}

public interface ISyncResponder<TRequest, TResponse>
{
    TResponse Handle(TRequest request);
}
