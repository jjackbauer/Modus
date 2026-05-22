namespace Modus.Host.Plugins.Authorization;

public sealed record PluginUploadAuthorizationResult(
    bool IsAuthorized,
    bool CanProceedToExtraction,
    string? TrustedKeyId,
    string? FailureReason)
{
    public static PluginUploadAuthorizationResult Authorized(string trustedKeyId)
        => new(
            IsAuthorized: true,
            CanProceedToExtraction: true,
            TrustedKeyId: trustedKeyId,
            FailureReason: null);

    public static PluginUploadAuthorizationResult Unauthorized(string failureReason)
        => new(
            IsAuthorized: false,
            CanProceedToExtraction: false,
            TrustedKeyId: null,
            FailureReason: failureReason);
}