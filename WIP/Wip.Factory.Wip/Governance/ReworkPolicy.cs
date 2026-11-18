using Wip.Factory.Contracts.Identifiers;
using Wip.Factory.Contracts.Work;
using Wip.Factory.Core;

namespace Wip.Factory.Wip.Governance;

/// <summary>What the rework policy decided after reading a review.</summary>
public sealed record ReworkDecision(
    FactoryWorkState? NextState,
    bool RedispatchImplementation,
    string? Reason)
{
    public static ReworkDecision Deliver() => new(FactoryWorkState.ReadyForDelivery, false, null);

    public static ReworkDecision Rework(string reason) => new(FactoryWorkState.NeedsRework, true, reason);

    public static ReworkDecision Exhausted(string reason) => new(FactoryWorkState.Failed, false, reason);
}

/// <summary>
/// Immutable ledger of every review produced for a work item (F-F.6). A rework cycle must never destroy
/// the review that caused it, so reviews are appended and never replaced.
/// </summary>
public sealed class FactoryReviewLedger
{
    private readonly Dictionary<string, List<FactoryReviewArtifact>> _reviews = new(StringComparer.Ordinal);

    public void Record(WorkItemId workItemId, FactoryReviewArtifact review)
    {
        ArgumentNullException.ThrowIfNull(review);

        if (!_reviews.TryGetValue(workItemId.Value, out var list))
        {
            list = [];
            _reviews[workItemId.Value] = list;
        }

        list.Add(review);
    }

    public IReadOnlyList<FactoryReviewArtifact> ReviewsOf(WorkItemId workItemId)
        => _reviews.TryGetValue(workItemId.Value, out var list) ? list.ToArray() : [];

    public int ReworkAttempts(WorkItemId workItemId)
        => ReviewsOf(workItemId).Count(static review => !string.Equals(review.Disposition, "Accepted", StringComparison.Ordinal));
}

/// <summary>
/// Decides what happens after a review. An accepted review advances toward delivery; a rejected one
/// sends the item back to implementation until the bounded attempt count is exhausted (F-F.6).
/// </summary>
public sealed class FactoryReworkPolicy
{
    public const string ReviewRejectedReason = "review-rejected";
    public const string ReworkExhaustedReason = "rework-attempts-exhausted";

    public ReworkDecision Decide(
        FactoryReviewArtifact review,
        WorkItemSnapshot item,
        FactoryReviewLedger ledger,
        int maxAttempts)
    {
        ArgumentNullException.ThrowIfNull(review);
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(ledger);

        if (maxAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAttempts), maxAttempts, "At least one attempt is required.");
        }

        if (string.Equals(review.Disposition, "Accepted", StringComparison.Ordinal))
        {
            return ReworkDecision.Deliver();
        }

        var attempts = ledger.ReworkAttempts(item.WorkItemId);
        if (attempts >= maxAttempts)
        {
            return ReworkDecision.Exhausted(ReworkExhaustedReason);
        }

        // The item must be in review for a rejection to make sense; anything else is a programming error
        // rather than something to paper over.
        if (item.State != FactoryWorkState.InReview)
        {
            throw new InvalidOperationException(
                $"Work item '{item.WorkItemId.Value}' is '{item.State}', not '{FactoryWorkState.InReview}', so a review rejection cannot apply.");
        }

        return ReworkDecision.Rework(ReviewRejectedReason);
    }

    /// <summary>
    /// Applies a rejection as two committed transitions, so the item is genuinely back in implementation
    /// rather than parked in a rework state nothing dispatches from.
    /// </summary>
    public (WorkItemSnapshot? Committed, WorkTransitionResult Result) ApplyRejection(WorkItemSnapshot item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var toRework = WorkStateMachine.Transition(item.State, FactoryWorkState.NeedsRework);
        if (!toRework.Succeeded)
        {
            return (null, toRework);
        }

        var reworking = item with { State = FactoryWorkState.NeedsRework, Version = item.Version + 1 };

        var toImplementation = WorkStateMachine.Transition(reworking.State, FactoryWorkState.InImplementation);
        if (!toImplementation.Succeeded)
        {
            return (null, toImplementation);
        }

        return (reworking with { State = FactoryWorkState.InImplementation, Version = reworking.Version + 1 }, toImplementation);
    }
}
