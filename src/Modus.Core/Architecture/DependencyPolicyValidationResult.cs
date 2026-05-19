namespace Modus.Core.Architecture;

public sealed record DependencyPolicyValidationResult(bool IsCompliant, IReadOnlyList<string> ForbiddenReferences);
