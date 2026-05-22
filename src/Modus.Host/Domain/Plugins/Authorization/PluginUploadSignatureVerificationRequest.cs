namespace Modus.Host.Plugins.Authorization;

public sealed record PluginUploadSignatureVerificationRequest(
    string PackageName,
    ReadOnlyMemory<byte> PackageBytes,
    ReadOnlyMemory<byte> SignatureBytes)
{
    public PluginUploadSignatureVerificationRequest(string packageName, byte[] packageBytes, byte[] signatureBytes)
        : this(
            packageName,
            new ReadOnlyMemory<byte>(packageBytes ?? throw new ArgumentNullException(nameof(packageBytes))),
            new ReadOnlyMemory<byte>(signatureBytes ?? throw new ArgumentNullException(nameof(signatureBytes))))
    {
    }
}