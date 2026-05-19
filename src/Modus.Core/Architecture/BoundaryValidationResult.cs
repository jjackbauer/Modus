namespace Modus.Core.Architecture;

public sealed record BoundaryValidationResult(bool IsCompliant, IReadOnlyList<string> Violations);
