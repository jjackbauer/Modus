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
        new("bones-domain-no-agents-host-web", "Wip.Bones", ["Wip.Bones.Agents", "Wip.Bones.Host", "Wip.Bones.Web"]),
        new("sandbox-protocol-no-implementations", "Wip.Sandbox.Protocol", ["Wip.*"]),
        new("sandbox-worker-no-host", "Wip.Sandbox.Worker", ["Wip.Bones", "Wip.Bones.*", "Wip.Shell", "Wip.Shell.*", "Wip.Runtime", "Wip.ShellHost", "Wip.Bones.Host"]),
        new("sandbox-client-no-host", "Wip.Sandbox.WorkerClient", ["Wip.Bones", "Wip.Bones.*", "Wip.Shell", "Wip.Shell.*", "Wip.Runtime", "Wip.ShellHost", "Wip.Bones.Host"]),
        new("tholdem-no-bones", "Wip.Tholdem", ["Wip.Bones", "Wip.Bones.*", "Wip.Shell", "Wip.Shell.*", "Wip.ShellHost"]),
        new("tholdem-agents-no-bones-or-hosts", "Wip.Tholdem.Agents", ["Wip.Bones", "Wip.Bones.*", "Wip.Shell", "Wip.Shell.*", "Wip.ShellHost", "Wip.Tholdem.Host"]),
        new("tholdem-web-no-bones-or-hosts", "Wip.Tholdem.Web", ["Wip.Bones", "Wip.Bones.*", "Wip.Shell", "Wip.Shell.*", "Wip.ShellHost", "Wip.Tholdem.Host"]),
        new("tholdem-host-no-bones", "Wip.Tholdem.Host", ["Wip.Bones", "Wip.Bones.*", "Wip.Shell", "Wip.Shell.*", "Wip.ShellHost"]),
        new("bones-agents-no-legacy-provider", "Wip.Bones.Agents", ["Wip.Bones.ModelProviders.DeepSeek"]),

        // Factory layering (F-A.5). Distinct rule ids: six.
        new("factory-contracts-no-implementations", "Wip.Factory.Contracts",
        [
            "Wip.Factory.Core", "Wip.Factory.Persistence", "Wip.Factory.Blackboard", "Wip.Factory.Wip",
            "Wip.Factory.Modus", "Wip.Factory.Admission", "Wip.Factory.Adaptation", "Wip.Factory.Telemetry",
            "Wip.Factory.Analysis", "Wip.Factory.Agents.Core", "Wip.Factory.Host",
            "Wip.Runtime", "Wip.Shell", "Wip.ShellHost", "Wip.Shell.*"
        ]),
        new("factory-core-no-infrastructure", "Wip.Factory.Core",
        [
            "Wip.Factory.Persistence", "Wip.Factory.Blackboard", "Wip.Factory.Wip", "Wip.Factory.Modus",
            "Wip.Factory.Admission", "Wip.Factory.Adaptation", "Wip.Factory.Telemetry",
            "Wip.Factory.Analysis", "Wip.Factory.Agents.Core", "Wip.Factory.Host"
        ]),
        new("factory-wip-adapter-no-hosts", "Wip.Factory.Wip",
            ["Wip.Factory.Host", "Wip.Shell", "Wip.ShellHost", "Wip.Shell.*"]),
        new("factory-agents-no-hosts", "Wip.Factory.Agents.Core",
            ["Wip.Factory.Host", "Wip.Shell", "Wip.ShellHost", "Wip.Shell.*"]),
        new("factory-analysis-no-orchestrator", "Wip.Factory.Analysis",
            ["Wip.Runtime", "Wip.Factory.Host", "Wip.Shell", "Wip.ShellHost", "Wip.Shell.*"]),
        new("factory-no-bones", "Wip.Factory.Adaptation", ["Wip.Bones", "Wip.Bones.*"]),
        new("factory-no-bones", "Wip.Factory.Admission", ["Wip.Bones", "Wip.Bones.*"]),
        new("factory-no-bones", "Wip.Factory.Analysis", ["Wip.Bones", "Wip.Bones.*"])
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
