namespace Modus.Core.Plugins;

using Modus.Core.Messaging;

public sealed record TimerWriteCurrentTimeResult(string TimestampUtcIso8601) : ISyncPayload;