using Wip.Factory.Contracts.Identifiers;
using Wip.Factory.Contracts.Work;

namespace Wip.Factory.Core;

/// <summary>An ordering constraint: <paramref name="Dependent"/> requires <paramref name="Prerequisite"/>.</summary>
public sealed record WorkDependencyEdge(WorkItemId Dependent, WorkItemId Prerequisite);

/// <summary>An edge deliberately modelled as a feedback relation rather than an ordering constraint.</summary>
public sealed record DeclaredFeedbackEdge(WorkItemId Dependent, WorkItemId Prerequisite);

/// <summary>Result of validating a dependency graph.</summary>
public sealed record DependencyGraphValidation(bool IsAcyclic, IReadOnlyList<WorkItemId> CyclePath)
{
    public static DependencyGraphValidation Acyclic() => new(true, []);

    public static DependencyGraphValidation Cyclic(IReadOnlyList<WorkItemId> cyclePath) => new(false, cyclePath);
}

/// <summary>
/// Dependency readiness for factory work items (F-A.3, F-C.8). Ordering edges must be acyclic;
/// satisfaction is evaluated against committed state, never against an event's payload.
/// </summary>
public static class DependencyReadiness
{
    /// <summary>
    /// Detects a cycle among ordering edges. Edges declared as feedback are excluded, because those are
    /// modelled as relationships rather than prerequisites.
    /// </summary>
    public static DependencyGraphValidation ValidateAcyclic(
        IEnumerable<WorkDependencyEdge> edges,
        IEnumerable<DeclaredFeedbackEdge>? declaredFeedbackEdges = null)
    {
        ArgumentNullException.ThrowIfNull(edges);

        var feedback = (declaredFeedbackEdges ?? []).ToHashSet();

        var adjacency = new Dictionary<WorkItemId, List<WorkItemId>>();
        foreach (var edge in edges)
        {
            if (feedback.Contains(new DeclaredFeedbackEdge(edge.Dependent, edge.Prerequisite)))
            {
                continue;
            }

            if (!adjacency.TryGetValue(edge.Dependent, out var targets))
            {
                targets = [];
                adjacency[edge.Dependent] = targets;
            }

            targets.Add(edge.Prerequisite);

            if (!adjacency.ContainsKey(edge.Prerequisite))
            {
                adjacency[edge.Prerequisite] = [];
            }
        }

        var visiting = new HashSet<WorkItemId>();
        var visited = new HashSet<WorkItemId>();
        var path = new List<WorkItemId>();

        foreach (var node in adjacency.Keys)
        {
            var cycle = Visit(node, adjacency, visiting, visited, path);
            if (cycle is not null)
            {
                return DependencyGraphValidation.Cyclic(cycle);
            }
        }

        return DependencyGraphValidation.Acyclic();
    }

    /// <summary>
    /// True when every prerequisite of the work item is satisfied: a work-item prerequisite must be
    /// released, and an artifact/decision/baseline prerequisite must be present in
    /// <paramref name="satisfiedReferences"/>.
    /// </summary>
    public static bool IsSatisfied(
        WorkItemSnapshot snapshot,
        IReadOnlyDictionary<WorkItemId, FactoryWorkState> committedStates,
        IReadOnlySet<string> satisfiedReferences)
        => UnsatisfiedDependencies(snapshot, committedStates, satisfiedReferences).Count == 0;

    /// <summary>Lists the prerequisites that are not yet satisfied, in declaration order.</summary>
    public static IReadOnlyList<WorkDependency> UnsatisfiedDependencies(
        WorkItemSnapshot snapshot,
        IReadOnlyDictionary<WorkItemId, FactoryWorkState> committedStates,
        IReadOnlySet<string> satisfiedReferences)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(committedStates);
        ArgumentNullException.ThrowIfNull(satisfiedReferences);

        var unsatisfied = new List<WorkDependency>();
        foreach (var dependency in snapshot.Dependencies)
        {
            var satisfied = dependency.Kind switch
            {
                DependencyKind.WorkItem =>
                    committedStates.TryGetValue(dependency.Prerequisite, out var state)
                    && state is FactoryWorkState.Released or FactoryWorkState.OperationallyValidated,
                _ => satisfiedReferences.Contains(dependency.Reference)
            };

            if (!satisfied)
            {
                unsatisfied.Add(dependency);
            }
        }

        return unsatisfied;
    }

    private static IReadOnlyList<WorkItemId>? Visit(
        WorkItemId node,
        IReadOnlyDictionary<WorkItemId, List<WorkItemId>> adjacency,
        HashSet<WorkItemId> visiting,
        HashSet<WorkItemId> visited,
        List<WorkItemId> path)
    {
        if (visited.Contains(node))
        {
            return null;
        }

        if (!visiting.Add(node))
        {
            var start = path.FindIndex(candidate => candidate.Equals(node));
            var cycle = (start >= 0 ? path.Skip(start) : path).ToList();
            cycle.Add(node);
            return cycle;
        }

        path.Add(node);

        foreach (var target in adjacency[node])
        {
            var cycle = Visit(target, adjacency, visiting, visited, path);
            if (cycle is not null)
            {
                return cycle;
            }
        }

        path.RemoveAt(path.Count - 1);
        visiting.Remove(node);
        visited.Add(node);
        return null;
    }
}
