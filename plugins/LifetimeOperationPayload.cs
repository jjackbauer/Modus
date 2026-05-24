using Modus.Core.Messaging;

namespace Modus.SamplePlugins.Lifetime;

public sealed record LifetimeOperationPayload(
    LifetimeOperationResult? Result,
    SyncErrorPayload? Error) : ISyncPayload
{
    public static LifetimeOperationPayload FromResult(LifetimeOperationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new LifetimeOperationPayload(Result: result, Error: null);
    }

    public static LifetimeOperationPayload FromError(string code, string message)
    {
        return new LifetimeOperationPayload(
            Result: null,
            Error: new SyncErrorPayload(code, message));
    }
}