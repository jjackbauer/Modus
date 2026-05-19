namespace Modus.Core.Architecture;

public sealed record ModuleDependency(string SourceModule, string TargetType, DependencyKind Kind);
