namespace Modus.Core.Architecture;

public static class RuntimeDependencyPolicy
{
    private static readonly HashSet<string> TrustedRuntimePrefixes =
    [
        "System",
        "netstandard",
        "mscorlib",
        "Modus",
    ];

    public static DependencyPolicyValidationResult Validate(RuntimeReferenceSet set)
    {
        var forbidden = set.AssemblyReferences
            .Where(reference => TrustedRuntimePrefixes.All(prefix => !reference.StartsWith(prefix, StringComparison.Ordinal)))
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        return new DependencyPolicyValidationResult(forbidden.Count == 0, forbidden);
    }
}
