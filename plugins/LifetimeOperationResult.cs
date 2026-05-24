using Modus.Core.Messaging;

namespace Modus.SamplePlugins.Lifetime;

public sealed record LifetimeOperationResult(
    string Lifetime,
    string InstanceId,
    int CreationIndex,
    int Invocation) : ISyncPayload;
