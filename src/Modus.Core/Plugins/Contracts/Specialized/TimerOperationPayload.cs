namespace Modus.Core.Plugins;

using Modus.Core.Messaging;

public sealed record TimerOperationPayload(
    string? TimestampUtcIso8601,
    SyncErrorPayload? Error) : ISyncPayload
{
    public static TimerOperationPayload FromResult(TimerWriteCurrentTimeResult payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return new TimerOperationPayload(TimestampUtcIso8601: payload.TimestampUtcIso8601, Error: null);
    }

    public static TimerOperationPayload FromError(string code, string message)
    {
        return new TimerOperationPayload(
            TimestampUtcIso8601: null,
            Error: new SyncErrorPayload(code, message));
    }
}