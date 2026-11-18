using Wip.Abstractions.Sessions;
using Wip.Factory.Contracts.Identifiers;
using Wip.Factory.Contracts.Work;
using Wip.Factory.Core;
using Xunit;

namespace Wip.Factory.Core.Tests;

public sealed class WorkStateMachineTests
{
    [Fact]
    public void WorkStateMachine_GivenLegalAndIllegalTransitions_CommitsOnlyLegalOnes()
    {
        var snapshot = new WorkItemSnapshot(
            WorkItemId: new WorkItemId("W-1"),
            State: FactoryWorkState.Proposed,
            Version: 1,
            Repository: "modus",
            Dependencies: [],
            CreatedAtUtc: DateTimeOffset.UnixEpoch);

        var (legalCommitted, legalResult) = WorkStateMachine.Apply(snapshot, FactoryWorkState.RequirementsAligned);

        Assert.True(legalResult.Succeeded);
        Assert.NotNull(legalCommitted);
        Assert.Equal(FactoryWorkState.RequirementsAligned, legalCommitted!.State);
        Assert.Equal(2, legalCommitted.Version);

        var (illegalCommitted, illegalResult) = WorkStateMachine.Apply(snapshot, FactoryWorkState.Released);

        Assert.False(illegalResult.Succeeded);
        Assert.Null(illegalCommitted);
        Assert.Contains("not declared", illegalResult.RejectionReason, StringComparison.Ordinal);

        // The refused transition must not have advanced the aggregate.
        Assert.Equal(FactoryWorkState.Proposed, illegalResult.From);
        Assert.Equal(1, snapshot.Version);

        // A terminal state admits nothing, and a self-transition is not a state change.
        Assert.Empty(WorkStateMachine.LegalTargets(FactoryWorkState.Closed));
        Assert.False(WorkStateMachine.Transition(FactoryWorkState.Released, FactoryWorkState.Released).Succeeded);
        Assert.True(WorkStateMachine.IsTerminal(FactoryWorkState.Closed));

        // The rework loop the DELIVER/REVIEW roles depend on is real, not decorative.
        Assert.True(WorkStateMachine.Transition(FactoryWorkState.InReview, FactoryWorkState.NeedsRework).Succeeded);
        Assert.True(WorkStateMachine.Transition(FactoryWorkState.NeedsRework, FactoryWorkState.InImplementation).Succeeded);
    }

    [Fact]
    public void WorkStateMachine_GivenFactoryStates_LeavesSessionStateEnumUnmodified()
    {
        // The WIP session enum is an existing public contract; the factory must not extend it (F-A.4).
        var sessionStates = Enum.GetValues<SessionState>();
        Assert.Equal(9, sessionStates.Length);
        Assert.Equal(SessionState.Created, sessionStates[0]);
        Assert.Equal(SessionState.Aborted, sessionStates[^1]);

        var sessionNames = sessionStates.Select(static state => state.ToString()).ToHashSet(StringComparer.Ordinal);
        var factoryNames = Enum.GetValues<FactoryWorkState>()
            .Select(static state => state.ToString())
            .ToArray();

        // No factory business state may be smuggled into the session vocabulary by name.
        var collisions = factoryNames.Where(sessionNames.Contains).ToArray();
        Assert.True(collisions.Length == 0, "Factory states collide with SessionState names: " + string.Join(", ", collisions));

        // Every factory state is governed by the machine; an undeclared state cannot be committed.
        var machineStates = WorkStateMachine.States.ToHashSet();
        var ungoverned = factoryNames
            .Where(name => !machineStates.Contains(Enum.Parse<FactoryWorkState>(name)))
            .ToArray();
        Assert.True(ungoverned.Length == 0, "Factory states have no transition rules: " + string.Join(", ", ungoverned));

        // The main-line path source §5.1 names must be walkable end to end.
        var path = new[]
        {
            FactoryWorkState.Proposed, FactoryWorkState.RequirementsAligned, FactoryWorkState.ArchitectureRefined,
            FactoryWorkState.ReadyForImplementation, FactoryWorkState.InImplementation, FactoryWorkState.InReview,
            FactoryWorkState.ReadyForDelivery, FactoryWorkState.DeliveryValidation,
            FactoryWorkState.ReleaseCandidate, FactoryWorkState.Released, FactoryWorkState.OperationallyValidated
        };

        for (var i = 0; i < path.Length - 1; i++)
        {
            var result = WorkStateMachine.Transition(path[i], path[i + 1]);
            Assert.True(result.Succeeded, $"Main-line transition {path[i]} -> {path[i + 1]} was rejected: {result.RejectionReason}");
        }
    }
}
