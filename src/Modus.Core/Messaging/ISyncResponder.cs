namespace Modus.Core.Messaging;

public interface ISyncResponder<TRequest, TResponse>
{
    TResponse Handle(TRequest request);
}
