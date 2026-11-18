using System.Reflection;
using Wip.Factory.Contracts.Identifiers;
using Xunit;

namespace Wip.Factory.Core.Tests;

/// <summary>
/// Proves contract layering at runtime rather than by reading a project file: the contract assembly
/// must expose no primitive string identity, and its reference graph must reach no implementation.
/// </summary>
public sealed class ContractsLayeringTests
{
    private static readonly Assembly ContractsAssembly = typeof(WorkItemId).Assembly;

    [Fact]
    public void Contracts_GivenScannedContractAssemblies_ContainsNoPrimitiveStringIdentifiers()
    {
        var offenders = new List<string>();

        var contractTypes = ContractsAssembly
            .GetTypes()
            .Where(static type => type.IsPublic || type.IsNestedPublic)
            .ToArray();

        Assert.NotEmpty(contractTypes);

        foreach (var type in contractTypes.Where(static type => !type.IsEnum))
        {
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.PropertyType != typeof(string))
                {
                    continue;
                }

                // A string is only a violation when it stands in for a semantic identity.
                if (property.Name.EndsWith("Id", StringComparison.Ordinal))
                {
                    offenders.Add($"{type.FullName}.{property.Name}");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Contract types expose primitive string identities: " + string.Join(", ", offenders));

        // The scan must have actually seen typed identities, otherwise it proves nothing.
        var identifierStructs = contractTypes
            .Where(static type => type.IsValueType && type.Namespace == typeof(WorkItemId).Namespace)
            .ToArray();
        Assert.Contains(identifierStructs, static type => type == typeof(WorkItemId));
        Assert.Contains(identifierStructs, static type => type == typeof(FactoryEventId));
        Assert.Contains(identifierStructs, static type => type == typeof(AgentProfileVersion));
    }

    [Fact]
    public void Contracts_GivenAssemblyReferenceGraph_ReachesNoImplementationProject()
    {
        var contractsReachable = ReachableWipFactoryAssemblies(ContractsAssembly);

        // The scan must not be vacuous: the contract layer legitimately depends on the WIP contract
        // assembly. Until that reference existed this check walked an empty graph and passed for the
        // wrong reason.
        Assert.NotEmpty(contractsReachable);
        Assert.Contains("Wip.Abstractions", contractsReachable);

        // What it may never reach is another Wip.Factory.* assembly (the contract layer owns no
        // implementation) nor the runtime or shell.
        var contractsImplementations = contractsReachable
            .Where(static name => name.StartsWith("Wip.Factory.", StringComparison.Ordinal))
            .Where(name => !string.Equals(name, ContractsAssembly.GetName().Name, StringComparison.Ordinal))
            .ToArray();

        Assert.True(
            contractsImplementations.Length == 0,
            "Wip.Factory.Contracts reaches implementation projects: " + string.Join(", ", contractsImplementations));

        Assert.DoesNotContain("Wip.Runtime", contractsReachable);
        Assert.DoesNotContain("Wip.Shell", contractsReachable);

        var coreAssembly = Assembly.Load("Wip.Factory.Core");
        var coreReachable = ReachableWipFactoryAssemblies(coreAssembly);

        var forbiddenInfrastructure = new[]
        {
            "Wip.Factory.Persistence", "Wip.Factory.Blackboard", "Wip.Factory.Wip", "Wip.Factory.Modus",
            "Wip.Factory.Admission", "Wip.Factory.Adaptation", "Wip.Factory.Telemetry", "Wip.Factory.Analysis",
            "Wip.Factory.Agents.Core", "Wip.Factory.Host"
        };

        var violations = coreReachable.Where(forbiddenInfrastructure.Contains).ToArray();

        Assert.True(
            violations.Length == 0,
            "Wip.Factory.Core reaches infrastructure: " + string.Join(", ", violations));

        // Core must reach the contract layer, otherwise the check above is vacuous.
        Assert.Contains("Wip.Factory.Contracts", coreReachable);
    }

    private static IReadOnlyCollection<string> ReachableWipFactoryAssemblies(Assembly root)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<Assembly>();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            var assembly = queue.Dequeue();
            foreach (var reference in assembly.GetReferencedAssemblies())
            {
                var name = reference.Name;
                if (string.IsNullOrEmpty(name) || !name.StartsWith("Wip.", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!visited.Add(name))
                {
                    continue;
                }

                try
                {
                    queue.Enqueue(Assembly.Load(reference));
                }
                catch (Exception exception) when (exception is FileNotFoundException or FileLoadException)
                {
                    // An unresolvable Wip.* reference is still recorded, so it cannot hide a violation.
                }
            }
        }

        return visited;
    }
}
