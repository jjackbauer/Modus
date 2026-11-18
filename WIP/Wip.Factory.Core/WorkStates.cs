using Wip.Factory.Contracts.Work;

namespace Wip.Factory.Core;

/// <summary>Outcome of a requested work-state transition.</summary>
public sealed record WorkTransitionResult
{
    private WorkTransitionResult(bool succeeded, FactoryWorkState from, FactoryWorkState to, string? rejectionReason)
    {
        Succeeded = succeeded;
        From = from;
        To = to;
        RejectionReason = rejectionReason;
    }

    public bool Succeeded { get; }

    public FactoryWorkState From { get; }

    /// <summary>The committed state on success; the requested (uncommitted) state on rejection.</summary>
    public FactoryWorkState To { get; }

    public string? RejectionReason { get; }

    public static WorkTransitionResult Accepted(FactoryWorkState from, FactoryWorkState to)
        => new(true, from, to, null);

    public static WorkTransitionResult Rejected(FactoryWorkState from, FactoryWorkState to, string reason)
        => new(false, from, to, reason);
}

/// <summary>
/// The single authority on which work-state transitions commit (F-A.3). A transition is legal only
/// when the target appears in the declared adjacency for the current state; everything else is
/// refused with the state unchanged.
/// </summary>
public static class WorkStateMachine
{
    private static readonly IReadOnlyDictionary<FactoryWorkState, FactoryWorkState[]> Adjacency =
        new Dictionary<FactoryWorkState, FactoryWorkState[]>
        {
            [FactoryWorkState.Proposed] = [FactoryWorkState.RequirementsAligned, FactoryWorkState.Cancelled],
            [FactoryWorkState.RequirementsAligned] =
                [FactoryWorkState.ArchitectureRefined, FactoryWorkState.Blocked, FactoryWorkState.Cancelled],
            [FactoryWorkState.ArchitectureRefined] =
                [FactoryWorkState.ReadyForImplementation, FactoryWorkState.Blocked, FactoryWorkState.Cancelled],
            [FactoryWorkState.ReadyForImplementation] =
                [FactoryWorkState.InImplementation, FactoryWorkState.Blocked, FactoryWorkState.Cancelled],
            [FactoryWorkState.InImplementation] =
                [FactoryWorkState.InReview, FactoryWorkState.NeedsRework, FactoryWorkState.Failed],
            [FactoryWorkState.InReview] =
                [FactoryWorkState.ReadyForDelivery, FactoryWorkState.NeedsRework, FactoryWorkState.Failed],
            [FactoryWorkState.NeedsRework] = [FactoryWorkState.InImplementation, FactoryWorkState.Failed],
            [FactoryWorkState.ReadyForDelivery] =
                [FactoryWorkState.DeliveryValidation, FactoryWorkState.NeedsRework],
            // D-08 A (v0.3 §3.1): the single approved edge, legal only before the merge operation is applied.
            [FactoryWorkState.DeliveryValidation] =
                [FactoryWorkState.ReleaseCandidate, FactoryWorkState.Failed, FactoryWorkState.NeedsRework],
            [FactoryWorkState.ReleaseCandidate] = [FactoryWorkState.Released, FactoryWorkState.Failed],
            [FactoryWorkState.Released] = [FactoryWorkState.OperationallyValidated, FactoryWorkState.Closed],
            [FactoryWorkState.OperationallyValidated] = [FactoryWorkState.Closed],
            [FactoryWorkState.Blocked] =
            [
                FactoryWorkState.RequirementsAligned, FactoryWorkState.ArchitectureRefined,
                FactoryWorkState.ReadyForImplementation, FactoryWorkState.Cancelled
            ],
            [FactoryWorkState.Failed] = [FactoryWorkState.Closed],
            [FactoryWorkState.Cancelled] = [FactoryWorkState.Closed],
            [FactoryWorkState.Closed] = []
        };

    /// <summary>Every state that has a declared adjacency entry, in declared order.</summary>
    public static IReadOnlyCollection<FactoryWorkState> States => Adjacency.Keys.ToArray();

    public static bool IsTerminal(FactoryWorkState state) => state == FactoryWorkState.Closed;

    public static IReadOnlyList<FactoryWorkState> LegalTargets(FactoryWorkState state)
        => Adjacency.TryGetValue(state, out var targets) ? targets : [];

    public static WorkTransitionResult Transition(FactoryWorkState from, FactoryWorkState to)
    {
        if (from == to)
        {
            return WorkTransitionResult.Rejected(from, to, $"Transition from '{from}' to itself is not a state change.");
        }

        var targets = LegalTargets(from);
        if (targets.Contains(to))
        {
            return WorkTransitionResult.Accepted(from, to);
        }

        var allowed = targets.Count == 0 ? "none (terminal)" : string.Join(", ", targets);
        return WorkTransitionResult.Rejected(
            from,
            to,
            $"Transition from '{from}' to '{to}' is not declared. Legal targets: {allowed}.");
    }

    /// <summary>
    /// Applies a transition to a snapshot and returns the committed snapshot, or the rejection with
    /// the original snapshot untouched. Version increments only on commit.
    /// </summary>
    public static (WorkItemSnapshot? Committed, WorkTransitionResult Result) Apply(
        WorkItemSnapshot snapshot,
        FactoryWorkState target)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var result = Transition(snapshot.State, target);
        if (!result.Succeeded)
        {
            return (null, result);
        }

        var committed = snapshot with { State = target, Version = snapshot.Version + 1 };
        return (committed, result);
    }
}
