using Modus.Core.Plugins;
using Xunit;

namespace Modus.Core.Tests.Plugins;

public sealed class PluginRuntimeStateTransitionsTests
{
    [Fact]
    public void RuntimeStates_GivenCanonicalLifecycle_ExpectedEnumContainsFailureAndRollbackStates()
    {
        var states = Enum.GetNames<PluginRuntimeState>();

        Assert.Equal(
            [
                nameof(PluginRuntimeState.Discovered),
                nameof(PluginRuntimeState.Validated),
                nameof(PluginRuntimeState.Loaded),
                nameof(PluginRuntimeState.Registered),
                nameof(PluginRuntimeState.Activated),
                nameof(PluginRuntimeState.Active),
                nameof(PluginRuntimeState.Deactivating),
                nameof(PluginRuntimeState.Unloaded),
                nameof(PluginRuntimeState.RollbackPending),
                nameof(PluginRuntimeState.Failed),
            ],
            states);
    }

    [Fact]
    public void LifecycleStateMachine_GivenValidPlugin_ExpectedOrderedStateProgressionToActive()
    {
        Assert.True(PluginRuntimeStateTransitions.IsAllowedTransition(PluginRuntimeState.Discovered, PluginRuntimeState.Validated));
        Assert.True(PluginRuntimeStateTransitions.IsAllowedTransition(PluginRuntimeState.Validated, PluginRuntimeState.Loaded));
        Assert.True(PluginRuntimeStateTransitions.IsAllowedTransition(PluginRuntimeState.Loaded, PluginRuntimeState.Registered));
        Assert.True(PluginRuntimeStateTransitions.IsAllowedTransition(PluginRuntimeState.Registered, PluginRuntimeState.Activated));
        Assert.True(PluginRuntimeStateTransitions.IsAllowedTransition(PluginRuntimeState.Activated, PluginRuntimeState.Active));
    }

    [Fact]
    public void Rollback_GivenActivationFailureAfterRegistration_ExpectedStateTransitionToRollbackPendingThenFailed()
    {
        Assert.True(PluginRuntimeStateTransitions.IsAllowedTransition(PluginRuntimeState.Registered, PluginRuntimeState.RollbackPending));
        Assert.True(PluginRuntimeStateTransitions.IsAllowedTransition(PluginRuntimeState.Activated, PluginRuntimeState.RollbackPending));
        Assert.True(PluginRuntimeStateTransitions.IsAllowedTransition(PluginRuntimeState.RollbackPending, PluginRuntimeState.Failed));
    }

    [Fact]
    public void HotUnload_GivenPluginDeactivationRequest_ExpectedTransitionFromActiveToDeactivatingToUnloaded()
    {
        Assert.True(PluginRuntimeStateTransitions.IsAllowedTransition(PluginRuntimeState.Active, PluginRuntimeState.Deactivating));
        Assert.True(PluginRuntimeStateTransitions.IsAllowedTransition(PluginRuntimeState.Deactivating, PluginRuntimeState.Unloaded));
    }

    [Fact]
    public void LifecycleStateMachine_GivenValidationFailure_ExpectedTransitionToFailedWithoutHostTermination()
    {
        Assert.True(PluginRuntimeStateTransitions.IsAllowedTransition(PluginRuntimeState.Validated, PluginRuntimeState.Failed));
    }

    [Fact]
    public void LifecycleStateMachine_GivenRetryPolicyPermits_ExpectedFailedTransitionsBackToDiscovered()
    {
        Assert.True(PluginRuntimeStateTransitions.IsAllowedTransition(PluginRuntimeState.Failed, PluginRuntimeState.Discovered));
    }

    [Fact]
    public void LifecycleStateMachine_GivenDisallowedTransition_ExpectedTransitionRejected()
    {
        Assert.False(PluginRuntimeStateTransitions.IsAllowedTransition(PluginRuntimeState.Discovered, PluginRuntimeState.Active));
        Assert.False(PluginRuntimeStateTransitions.IsAllowedTransition(PluginRuntimeState.Unloaded, PluginRuntimeState.Active));
        Assert.False(PluginRuntimeStateTransitions.IsAllowedTransition(PluginRuntimeState.Active, PluginRuntimeState.Failed));
    }

    [Fact]
    public void LifecycleStateMachine_GivenStateQuery_ExpectedAllowedNextStatesReturnedDeterministically()
    {
        var nextStates = PluginRuntimeStateTransitions.GetAllowedNextStates(PluginRuntimeState.Registered);

        Assert.Equal(
            [
                PluginRuntimeState.Activated,
                PluginRuntimeState.RollbackPending,
            ],
            nextStates);
    }
}