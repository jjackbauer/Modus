using System.Security.Cryptography;

namespace Modus.Host.Plugins.Authorization;

internal sealed class PluginUploadAuthorizationPipeline
{
    private readonly PluginUploadAuthorizationOptions _options;

    public PluginUploadAuthorizationPipeline(PluginUploadAuthorizationOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public PluginUploadAuthorizationResult VerifyPluginUploadSignature(PluginUploadSignatureVerificationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_options.TrustedAuthorKeys.Count == 0)
        {
            return PluginUploadAuthorizationResult.Unauthorized("No trusted plugin author keys are configured.");
        }

        if (request.PackageBytes.IsEmpty)
        {
            return PluginUploadAuthorizationResult.Unauthorized("Plugin upload package payload is empty.");
        }

        if (request.SignatureBytes.IsEmpty)
        {
            return PluginUploadAuthorizationResult.Unauthorized("Plugin upload signature payload is empty.");
        }

        foreach (var trustedKey in _options.TrustedAuthorKeys)
        {
            if (VerifyWithTrustedKey(request.PackageBytes.Span, request.SignatureBytes.Span, trustedKey.PublicKeyPem))
            {
                return PluginUploadAuthorizationResult.Authorized(trustedKey.KeyId);
            }
        }

        return PluginUploadAuthorizationResult.Unauthorized("Plugin upload signature did not match any trusted author key.");
    }

    public async ValueTask<PluginUploadAuthorizationResult> AuthorizeAndContinueAsync(
        PluginUploadSignatureVerificationRequest request,
        Func<CancellationToken, ValueTask> onAuthorized,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(onAuthorized);

        var authorization = VerifyPluginUploadSignature(request);
        if (!authorization.IsAuthorized)
        {
            return authorization;
        }

        await onAuthorized(cancellationToken);
        return authorization;
    }

    private static bool VerifyWithTrustedKey(
        ReadOnlySpan<byte> packageBytes,
        ReadOnlySpan<byte> signatureBytes,
        string publicKeyPem)
    {
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(publicKeyPem);
            return rsa.VerifyData(packageBytes, signatureBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}