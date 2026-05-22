namespace Modus.Host.Plugins.Authorization;

public sealed class PluginUploadAuthorizationOptions
{
    public IList<TrustedPluginAuthorKey> TrustedAuthorKeys { get; } = new List<TrustedPluginAuthorKey>();
}