using Wip.Abstractions.Identifiers;
using Wip.Factory.Contracts.Blackboard;

namespace Wip.Factory.Blackboard;

/// <summary>
/// In-memory blackboard over the six owned views (F-H). It resolves references only, applies
/// role scoping before any budget calculation, and reports what it withheld so a caller can never
/// mistake a denied read for an empty store.
/// </summary>
public sealed class FactoryBlackboard : IFactoryBlackboard
{
    private readonly List<BlackboardReference> _references = [];
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async ValueTask<BlackboardReference> PublishAsync(
        BlackboardReference artifact,
        AgentExecutionIdentity producer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentNullException.ThrowIfNull(producer);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // The producer identity is authoritative: a caller cannot claim another role's authorship.
            var recorded = artifact with { ProducingRole = producer.Role };
            _references.Add(recorded);
            return recorded;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<ContextBundle> ResolveAsync(
        ContextQuery query,
        AgentExecutionIdentity principal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(principal);

        if (query.TokenBudget <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(query), query.TokenBudget, "A token budget must be positive.");
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var requestedViews = query.RequestedViews.Count == 0
                ? Enum.GetValues<BlackboardView>()
                : query.RequestedViews.ToArray();

            var candidates = _references
                .Where(reference => requestedViews.Contains(reference.View))
                .OrderBy(static reference => reference.View)
                .ThenBy(static reference => reference.ArtifactId.Value, StringComparer.Ordinal)
                .ToArray();

            var readable = new List<BlackboardReference>();
            var withheld = new List<BlackboardReference>();

            foreach (var candidate in candidates)
            {
                if (IsReadable(candidate, principal))
                {
                    readable.Add(candidate);
                }
                else
                {
                    withheld.Add(candidate);
                }
            }

            // Budget applies only after scoping, so a denied read never consumes another caller's budget.
            var included = new List<BlackboardReference>();
            var truncated = new List<string>();
            var used = 0;
            var overBudget = false;

            foreach (var reference in readable)
            {
                if (used + reference.EstimatedTokens > query.TokenBudget)
                {
                    truncated.Add(reference.ArtifactId.Value);
                    overBudget = true;
                    continue;
                }

                included.Add(reference);
                used += reference.EstimatedTokens;
            }

            return new ContextBundle(included, withheld, truncated, overBudget, used);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Role scoping (F-H.3). A reflection principal reads shared material and its own role-private
    /// material, never a holdout and never another role's private material.
    /// </summary>
    private static bool IsReadable(BlackboardReference reference, AgentExecutionIdentity principal)
        => reference.Sensitivity switch
        {
            BlackboardSensitivity.Shared => true,
            BlackboardSensitivity.Holdout => !principal.IsReflection,
            // Role-private material is readable only by the role that produced it — whether or not the
            // reader is a reflection principal. Another agent's private tool arguments are never shared.
            BlackboardSensitivity.RolePrivate =>
                string.Equals(reference.ProducingRole, principal.Role, StringComparison.Ordinal),
            _ => false
        };
}
