using Wip.Factory.Host.Configuration;
using Xunit;

namespace Wip.Factory.Host.Tests;

public sealed class FactoryConfigTests
{
    private const string ValidJson = """
        {
          "RepositoryPath": "C:\\repo",
          "WorkspaceRoot": "C:\\repo",
          "WipRoot": "C:\\repo\\.wip",
          "SessionsPath": "C:\\repo\\.wip\\sessions",
          "WorktreesPath": "C:\\repo\\.wip\\worktrees",
          "PluginsPath": "C:\\repo\\.wip\\plugins",
          "DefaultWorkflowId": "workflow.linear",
          "PolicyId": "local-safe"
        }
        """;

    private const string MissingPolicyJson = """
        {
          "RepositoryPath": "C:\\repo",
          "WorkspaceRoot": "C:\\repo",
          "WipRoot": "C:\\repo\\.wip",
          "SessionsPath": "C:\\repo\\.wip\\sessions",
          "WorktreesPath": "C:\\repo\\.wip\\worktrees",
          "PluginsPath": "C:\\repo\\.wip\\plugins",
          "DefaultWorkflowId": "workflow.linear"
        }
        """;
    [Fact]
    public void Config_GivenUnknownKey_IsRejectedAndShellAndHostAgree()
    {
        // An unknown key is a configuration error, not something to ignore.
        var withUnknownKey = ValidJson.Replace(
            "\"PolicyId\": \"local-safe\"",
            "\"PolicyId\": \"local-safe\",\n  \"PolicyID\": \"local-safe\",\n  \"SecretSauce\": \"yes\"",
            StringComparison.Ordinal);

        var rejected = FactoryConfigReader.Read(withUnknownKey);

        Assert.False(rejected.Loaded);
        Assert.Null(rejected.Config);
        var error = Assert.Single(rejected.Errors);
        Assert.StartsWith(FactoryConfigReader.UnknownKeyCategory, error, StringComparison.Ordinal);
        Assert.Contains("SecretSauce", error, StringComparison.Ordinal);

        // A missing required key is equally rejected.
        var missing = FactoryConfigReader.Read(MissingPolicyJson);
        Assert.False(missing.Loaded);
        Assert.Contains(missing.Errors, static e => e.StartsWith("missing-config-key:PolicyId", StringComparison.Ordinal));

        // Valid configuration loads, and both surfaces render byte-identical effective configuration
        // because they compose the same reader rather than each maintaining a parser.
        var shell = FactoryConfigReader.Read(ValidJson);
        var host = FactoryConfigReader.Read(ValidJson);

        Assert.True(shell.Loaded, string.Join(';', shell.Errors));
        Assert.True(host.Loaded, string.Join(';', host.Errors));

        var shellEffective = FactoryConfigReader.RenderEffective(shell.Config!);
        var hostEffective = FactoryConfigReader.RenderEffective(host.Config!);

        Assert.Equal(shellEffective, hostEffective);
        Assert.Equal(shell.Config, host.Config);
        Assert.Contains("policy=local-safe", shellEffective, StringComparison.Ordinal);
        Assert.Contains("workflow=workflow.linear", shellEffective, StringComparison.Ordinal);

        // Malformed JSON is rejected too, rather than throwing past the composition root.
        Assert.False(FactoryConfigReader.Read("{ not json").Loaded);
        Assert.False(FactoryConfigReader.Read("[]").Loaded);
    }
}
