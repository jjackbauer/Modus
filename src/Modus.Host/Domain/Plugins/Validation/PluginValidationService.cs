namespace Modus.Host.Plugins.Validation;

public sealed class PluginValidationService
{
    public PluginValidationResult Validate(PluginDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        if (!descriptor.IsContractCompliant)
        {
            return PluginValidationResult.Failure("contract violation");
        }

        if (!descriptor.IsValidAssembly)
        {
            return PluginValidationResult.Failure("invalid assembly");
        }

        return PluginValidationResult.Success();
    }
}

public readonly record struct PluginValidationResult(bool IsValid, string? FailureReason)
{
    public static PluginValidationResult Success() => new(true, null);

    public static PluginValidationResult Failure(string reason) => new(false, reason);
}