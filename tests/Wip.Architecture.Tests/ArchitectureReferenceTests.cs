using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Wip.Architecture.Tests;

public sealed class ArchitectureReferenceTests
{
    [Fact]
    public void ProjectGraph_GivenWipSolution_ExpectedAllWipProjects()
    {
        var graph = ProjectReferenceGraph.Load(RepositoryRoot.Find());

        Assert.Equal(43, graph.Count);
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
            ["Wip.Bones.Agents"] = ["Wip.Bones", "Wip.Bones.ModelProviders.DeepSeek"],
            ["Wip.Bones"] = [],
            ["Wip.Bones.ModelProviders.DeepSeek"] = []
        };

        var violations = ArchitectureRules.Evaluate(graph);

        Assert.Contains(
            violations,
            violation => violation.RuleId == "shellhost-no-bones"
                && violation.Source == "Wip.ShellHost"
                && violation.Target == "Wip.Bones");

        // WP4: the domain agents must never reach back into the private DeepSeek provider stack.
        Assert.Contains(
            violations,
            violation => violation.RuleId == "bones-agents-no-legacy-provider"
                && violation.Source == "Wip.Bones.Agents"
                && violation.Target == "Wip.Bones.ModelProviders.DeepSeek");
    }

    /// <summary>
    /// WP-2.1: the host owns composition and the per-run scope. If the runtime builds its own
    /// provider, hosts and runtime see different singleton instances.
    /// </summary>
    [Fact]
    public void RuntimeSource_GivenBuildServiceProvider_ExpectedNoNestedProvider()
    {
        var runtimeDirectory = Path.Combine(RepositoryRoot.Find(), "WIP", "Wip.Runtime");
        Assert.True(Directory.Exists(runtimeDirectory), $"Missing runtime directory: {runtimeDirectory}");

        var offenders = Directory
            .EnumerateFiles(runtimeDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains(
                $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase))
            .Where(path => File.ReadAllText(path).Contains("BuildServiceProvider", StringComparison.Ordinal))
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            "Wip.Runtime must not build service providers; the host owns composition: " + string.Join(", ", offenders));
    }
}
