namespace Wip.Bones.Agents.Ponder;

public sealed class BonesPonderCompileRetryOptions
{
    public const string ConfigurationSectionName = "BonesHost:Ponder";

    public const string MaxCompileRetriesConfigurationKey = "MaxCompileRetries";

    public int MaxCompileRetries { get; set; } = 3;
}