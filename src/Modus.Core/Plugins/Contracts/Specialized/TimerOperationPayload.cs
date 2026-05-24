namespace Modus.Core.Plugins;

using Modus.Core.Messaging;

public sealed record TimerOperationPayload(
    ISyncPayload? Payload,
    SyncErrorPayload? Error) : ISyncPayload
{
    public static TimerOperationPayload FromResult(ISyncPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return new TimerOperationPayload(Payload: payload, Error: null);
    }

    public static TimerOperationPayload FromError(string code, string message)
    {
        return new TimerOperationPayload(
            Payload: null,
            Error: new SyncErrorPayload(code, message));
    }
}