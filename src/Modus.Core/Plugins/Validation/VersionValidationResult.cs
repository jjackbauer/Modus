namespace Modus.Core.Plugins;

public sealed record VersionValidationResult(bool IsCompatible, string? Error);