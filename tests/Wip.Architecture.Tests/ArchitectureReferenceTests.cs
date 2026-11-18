using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Wip.Architecture.Tests;

public sealed class ArchitectureReferenceTests
{
    [Fact]
    public void ProjectGraph_GivenSolutionProjects_MatchesSolutionDeclaredSet()
    {
        var root = RepositoryRoot.Find();
        var graph = ProjectReferenceGraph.Load(root);
        var declared = SolutionProjects.LoadDeclaredWipProjectNames(root);

        Assert.NotEmpty(declared);
        Assert.NotEmpty(graph);

        // The gate asserts the on-disk project set and the solution agree, in both directions:
        // a project added to disk but not to the solution fails, and a stale solution entry fails.
        Assert.Equal(
            declared.OrderBy(static name => name, StringComparer.Ordinal),
            graph.Keys.OrderBy(static name => name, StringComparer.Ordinal));
    }

    [Fact]
    public void ArchitectureRules_GivenSyntheticFactoryEdge_ReportsTheMatchingFactoryRule()
    {
        var factoryRules = ArchitectureRules
            .Rules
            .Where(static rule => rule.Id.StartsWith("factory-", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(6, factoryRules.Select(static rule => rule.Id).Distinct(StringComparer.Ordinal).Count());

        foreach (var rule in factoryRules)
        {
            var target = rule.ForbiddenPatterns.First(static pattern => !pattern.EndsWith(".*", StringComparison.Ordinal));
            var graph = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.Ordinal)
            {
                [rule.Source] = [target],
                [target] = []
            };

            var violations = ArchitectureRules.Evaluate(graph);

            Assert.True(
                violations.Any(violation => violation.RuleId == rule.Id
                    && violation.Source == rule.Source
                    && violation.Target == target),
                $"Rule '{rule.Id}' did not bite on the synthetic edge {rule.Source} -> {target}.");
        }
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
