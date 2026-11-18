using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace Wip.Factory.Host.Tests;

/// <summary>
/// The D3 compliance gate. It parses the committed requirement document and asserts that every named
/// test resolves to a real xUnit test method in one of the built factory test assemblies. A grep for a
/// name is not evidence — only reflection over the compiled assemblies is.
/// </summary>
public sealed class BehaviorProofComplianceGateTests
{
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Wip.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root could not be located from the test output directory.");
    }

    private static IReadOnlyList<string> SpecTestNames()
    {
        var documentPath = Path.Combine(RepositoryRoot(), "harness", "requirements", "Wip.Factory.Alpha.md");
        var text = File.ReadAllText(documentPath);

        return Regex.Matches(text, @"(?m)^\d+\.\s+`([^`]+)`")
            .Select(static match => match.Groups[1].Value)
            .ToArray();
    }

    private static IReadOnlyList<string> DiscoveredTestNames()
    {
        var root = RepositoryRoot();
        // Factory suites live under WIP/, the architecture gate under tests/. Both are scanned.
        var assemblyPaths = new[]
        {
            Path.Combine(root, "WIP", "Wip.Factory.Core.Tests", "bin", "Debug", "net10.0", "Wip.Factory.Core.Tests.dll"),
            Path.Combine(root, "WIP", "Wip.Factory.Agents.Tests", "bin", "Debug", "net10.0", "Wip.Factory.Agents.Tests.dll"),
            Path.Combine(root, "WIP", "Wip.Factory.Host.Tests", "bin", "Debug", "net10.0", "Wip.Factory.Host.Tests.dll"),
            Path.Combine(root, "tests", "Wip.Architecture.Tests", "bin", "Debug", "net10.0", "Wip.Architecture.Tests.dll")
        };

        var discovered = new List<string>();

        foreach (var path in assemblyPaths)
        {

            if (!File.Exists(path))
            {
                continue;
            }

            var assembly = Assembly.LoadFrom(path);
            foreach (var type in assembly.GetTypes().Where(static type => type.IsClass && type.IsPublic))
            {
                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    var isTest = method.GetCustomAttributes()
                        .Any(static attribute =>
                            attribute.GetType().Name is "FactAttribute" or "TheoryAttribute");

                    if (isTest)
                    {
                        discovered.Add(method.Name);
                    }
                }
            }
        }

        return discovered;
    }

    [Fact]
    public void BehaviorProofCompliance_GivenChecklistItems_RequiresExecutableRuntimeProofForEachItem()
    {
        var specNames = SpecTestNames();
        var discovered = DiscoveredTestNames().ToHashSet(StringComparer.Ordinal);

        Assert.Equal(82, specNames.Count);
        Assert.NotEmpty(discovered);

        var missing = specNames.Where(name => !discovered.Contains(name)).ToArray();

        Assert.True(
            missing.Length == 0,
            $"The requirement document names {specNames.Count} tests but {missing.Length} are not present as xUnit tests: "
            + string.Join(", ", missing));
    }
}
