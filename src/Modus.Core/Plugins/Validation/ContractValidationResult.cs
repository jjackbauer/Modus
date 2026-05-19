namespace Modus.Core.Plugins;

public sealed record ContractValidationResult(bool IsValid, IReadOnlyList<string> MissingCapabilities);