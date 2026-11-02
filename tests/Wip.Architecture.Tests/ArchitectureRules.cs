using System;
using System.Collections.Generic;
using System.Linq;

namespace Wip.Architecture.Tests;

internal sealed record ArchitectureRule(string Id, string Source, IReadOnlyList<string> ForbiddenPatterns);

internal sealed record ArchitectureViolation(string RuleId, string Source, string Target);

internal static class ArchitectureRules
{
    public static IReadOnlyList<ArchitectureRule> Rules { get; } =
    [
        new("abstractions-no-implementations", "Wip.Abstractions", ["Wip.*"]),
        new("runtime-no-ui-or-bones", "Wip.Runtime", ["Wip.Shell", "Wip.ShellHost", "Wip.Bones", "Wip.Bones.*"]),
        new("shellhost-no-bones", "Wip.ShellHost", ["Wip.Bones", "Wip.Bones.*"]),
        new("builder-no-runtime-or-host", "Wip.Builder", ["Wip.Runtime", "Wip.ShellHost"]),
        new("bones-host-no-shell", "Wip.Bones.Host", ["Wip.Shell", "Wip.Shell.*"]),
        new("bones-domain-no-agents-host-web", "Wip.Bones", ["Wip.Bones.Agents", "Wip.Bones.Host", "Wip.Bones.Web"])
    ];

    public static IReadOnlyList<ArchitectureViolation> Evaluate(IReadOnlyDictionary<string, IReadOnlyCollection<string>> graph)
    {
        var violations = new HashSet<ArchitectureViolation>();
        foreach (var rule in Rules)
        {
            if (!graph.ContainsKey(rule.Source))
            {
                continue;
            }

            foreach (var target in Reachable(graph, rule.Source))
            {
                if (string.Equals(target, rule.Source, StringComparison.Ordinal))
                {
                    continue;
                }

                if (rule.ForbiddenPatterns.Any(pattern => Matches(pattern, target)))
                {
                    violations.Add(new ArchitectureViolation(rule.Id, rule.Source, target));
                }
            }
        }

        return violations.ToArray();
    }

    public static IReadOnlyCollection<string> Reachable(
        IReadOnlyDictionary<string, IReadOnlyCollection<string>> graph,
        string source)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        if (graph.TryGetValue(source, out var direct))
        {
            foreach (var target in direct)
            {
                if (visited.Add(target))
                {
                    queue.Enqueue(target);
                }
            }
        }

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!graph.TryGetValue(current, out var next))
            {
                continue;
            }

            foreach (var target in next)
            {
                if (visited.Add(target))
                {
                    queue.Enqueue(target);
                }
            }
        }

        return visited.ToArray();
    }

    public static bool Matches(string pattern, string target)
    {
        if (pattern.EndsWith(".*", StringComparison.Ordinal))
        {
            return target.StartsWith(pattern[..^1], StringComparison.Ordinal);
        }

        return string.Equals(pattern, target, StringComparison.Ordinal);
    }
}
