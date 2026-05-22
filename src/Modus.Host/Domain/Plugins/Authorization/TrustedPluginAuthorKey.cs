namespace Modus.Host.Plugins.Authorization;

public sealed record TrustedPluginAuthorKey
{
    public TrustedPluginAuthorKey(string keyId, string publicKeyPem)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(publicKeyPem);

        KeyId = keyId;
        PublicKeyPem = publicKeyPem;
    }

    public string KeyId { get; }

    public string PublicKeyPem { get; }
}