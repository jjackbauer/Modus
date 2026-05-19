namespace Modus.Core.Messaging;

public enum SyncFallbackReason
{
    None = 0,
    SubscriberUnavailable = 1,
    CapabilityUnavailable = 2,
    ConsistencyRequirement = 3,
    DeadlineExceeded = 4,
    ManualOverride = 5,
}