using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Wip.Architecture.Tests;

public sealed class ArchitectureReferenceTests
{
    [Fact]
    public void ProjectGraph_GivenWipSolution_ExpectedAllWipProjects()
    {
        var graph = ProjectReferenceGraph.Load(RepositoryRoot.Find());

        Assert.Equal(39, graph.Count);
    }

    [Fact]
    public void ProjectReferences_GivenForbiddenEdges_ExpectedNoUnwaivedViolations()
    {
        var root = RepositoryRoot.Find();
        var graph = ProjectReferenceGraph.Load(root);
        var violations = ArchitectureRules.Evaluate(graph);
        var waivers = ArchitectureWaivers.Load(root);

        foreach (var rule in ArchitectureRules.Rules)
        {
            Assert.True(graph.ContainsKey(rule.Source), $"Rule '{rule.Id}' names missing project '{rule.Source}'.");
        }

        var unwaived = violations
            .Where(violation => waivers.All(waiver => !waiver.Matches(violation)))
            .ToArray();
        Assert.True(
            unwaived.Length == 0,
            "Unwaived forbidden references: " + string.Join("; ", unwaived.Select(static v => $"{v.RuleId}: {v.Source} -> {v.Target}")));

        var stale = waivers
            .Where(waiver => violations.All(violation => !waiver.Matches(violation)))
            .ToArray();
        Assert.True(
            stale.Length == 0,
            "Stale architecture waivers: " + string.Join("; ", stale.Select(static w => $"{w.Adr}: {w.Source} -> {w.Target}")));
    }

    [Fact]
    public void Rules_GivenSyntheticForbiddenEdge_ExpectedViolation()
    {
        var graph = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.Ordinal)
        {
            ["Wip.ShellHost"] = ["Wip.Bones.Agents"],
            ["Wip.Bones.Agents"] = ["Wip.Bones"],
            ["Wip.Bones"] = []
        };

        var violations = ArchitectureRules.Evaluate(graph);

        Assert.Contains(
            violations,
            violation => violation.RuleId == "shellhost-no-bones"
                && violation.Source == "Wip.ShellHost"
                && violation.Target == "Wip.Bones");
    }
}
