namespace Modus.Core.Plugins;

using Modus.Core.Messaging;

public sealed record TelemetryOperationPayload(
    TelemetryResult? Result,
    SyncErrorPayload? Error) : ISyncPayload
{
    public static TelemetryOperationPayload FromResult(TelemetryResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new TelemetryOperationPayload(Result: result, Error: null);
    }

    public static TelemetryOperationPayload FromError(string code, string message)
    {
        return new TelemetryOperationPayload(
            Result: null,
            Error: new SyncErrorPayload(code, message));
    }
}