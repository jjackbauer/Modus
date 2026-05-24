namespace Modus.Core.Messaging;

public sealed record SyncErrorPayload(string Code, string Message) : ISyncPayload;
