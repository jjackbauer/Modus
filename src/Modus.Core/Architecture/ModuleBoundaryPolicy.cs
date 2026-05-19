namespace Modus.Core.Architecture;

public static class ModuleBoundaryPolicy
{
    public static BoundaryValidationResult Validate(ModuleDependencyGraph graph)
    {
        var violations = graph.Dependencies
            .Where(IsCrossModuleConcreteDependency)
            .Select(d => $"{d.SourceModule} has cross-module concrete dependency on {d.TargetType}")
            .ToList();

        return new BoundaryValidationResult(violations.Count == 0, violations);
    }

    private static bool IsCrossModuleConcreteDependency(ModuleDependency dependency)
    {
        if (dependency.Kind != DependencyKind.Concrete)
        {
            return false;
        }

        if (dependency.TargetType.Contains(".Internal.", StringComparison.Ordinal))
        {
            return true;
        }

        if (!TryGetModuleRoot(dependency.SourceModule, out var sourceModuleRoot))
        {
            return false;
        }

        if (!TryGetModuleRoot(dependency.TargetType, out var targetModuleRoot))
        {
            return false;
        }

        return !string.Equals(sourceModuleRoot, targetModuleRoot, StringComparison.Ordinal);
    }

    private static bool TryGetModuleRoot(string typeOrModuleName, out string moduleRoot)
    {
        moduleRoot = string.Empty;
        const string modulePrefix = "Modus.Module.";

        if (!typeOrModuleName.StartsWith(modulePrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var segments = typeOrModuleName.Split('.');
        if (segments.Length < 3)
        {
            return false;
        }

        moduleRoot = string.Join('.', segments[0], segments[1], segments[2]);
        return true;
    }
}
