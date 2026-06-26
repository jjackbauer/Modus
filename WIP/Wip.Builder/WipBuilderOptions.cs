namespace Wip.Builder;

public enum DuplicateCapabilityRegistrationBehavior
{
    Reject = 0,
    ReplaceExisting = 1
}

public sealed class WipBuilderOptions
{
    public DuplicateCapabilityRegistrationBehavior DuplicateCapabilityBehavior { get; set; } =
        DuplicateCapabilityRegistrationBehavior.Reject;
}