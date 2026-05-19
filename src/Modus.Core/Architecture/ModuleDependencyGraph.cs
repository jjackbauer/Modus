namespace Modus.Core.Architecture;

public sealed record ModuleDependencyGraph(IReadOnlyList<ModuleDependency> Dependencies);
