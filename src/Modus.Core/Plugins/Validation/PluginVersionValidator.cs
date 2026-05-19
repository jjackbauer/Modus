namespace Modus.Core.Plugins;

public static class PluginVersionValidator
{
    public static VersionValidationResult Validate(Version pluginVersion, Version requiredVersion)
    {
        if (pluginVersion < requiredVersion)
        {
            return new VersionValidationResult(IsCompatible: false, Error: "Contract version mismatch.");
        }

        return new VersionValidationResult(IsCompatible: true, Error: null);
    }
}