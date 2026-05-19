using Modus.Core.Architecture;
using Xunit;

namespace Modus.Architecture.Tests;

public sealed class ModuleBoundaryRulesTests
{
    [Fact]
    public void BoundaryRules_GivenCrossModuleConcreteDependency_ExpectedEnforcementFailure()
    {
        var graph = new ModuleDependencyGraph(
            [
                new ModuleDependency("Modus.Module.Billing", "Modus.Module.Orders.Internal.OrderRepository", DependencyKind.Concrete),
                new ModuleDependency("Modus.Module.Billing", "Modus.Core.Contracts.IOrderReader", DependencyKind.Contract),
            ]);

        var result = ModuleBoundaryPolicy.Validate(graph);

        Assert.False(result.IsCompliant);
        Assert.Contains(result.Violations, v =>
            v.Contains("cross-module concrete dependency", StringComparison.Ordinal) &&
            v.Contains("Modus.Module.Orders.Internal.OrderRepository", StringComparison.Ordinal));
    }

    [Fact]
    public void BoundaryRules_GivenCrossModuleConcreteDependencyOutsideInternalNamespace_ExpectedEnforcementFailure()
    {
        var graph = new ModuleDependencyGraph(
            [
                new ModuleDependency("Modus.Module.Billing", "Modus.Module.Orders.Application.OrderService", DependencyKind.Concrete),
            ]);

        var result = ModuleBoundaryPolicy.Validate(graph);

        Assert.False(result.IsCompliant);
        Assert.Contains(result.Violations, v =>
            v.Contains("cross-module concrete dependency", StringComparison.Ordinal) &&
            v.Contains("Modus.Module.Orders.Application.OrderService", StringComparison.Ordinal));
    }

    [Fact]
    public void BoundaryRules_GivenCrossModuleContractDependency_ExpectedCompliantBoundary()
    {
        var graph = new ModuleDependencyGraph(
            [
                new ModuleDependency("Modus.Module.Billing", "Modus.Module.Orders.Contracts.IOrderReader", DependencyKind.Contract),
            ]);

        var result = ModuleBoundaryPolicy.Validate(graph);

        Assert.True(result.IsCompliant);
        Assert.Empty(result.Violations);
    }

    [Fact]
    public void BoundaryRules_GivenSameModuleConcreteDependency_ExpectedCompliantBoundary()
    {
        var graph = new ModuleDependencyGraph(
            [
                new ModuleDependency("Modus.Module.Billing", "Modus.Module.Billing.Services.InvoiceCalculator", DependencyKind.Concrete),
            ]);

        var result = ModuleBoundaryPolicy.Validate(graph);

        Assert.True(result.IsCompliant);
        Assert.Empty(result.Violations);
    }
}
