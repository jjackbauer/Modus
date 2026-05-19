using Modus.Core.Architecture;
using Xunit;

namespace Modus.Architecture.Tests;

public sealed class DependencyPolicyTests
{
    [Fact]
    public void DependencyRules_GivenThirdPartyRuntimeReferenceInPluginPath_ExpectedBuildOrValidationFailure()
    {
        var references = new RuntimeReferenceSet(
            [
                "System.Runtime",
                "System.Collections",
                "Microsoft.Extensions.DependencyInjection",
                "Newtonsoft.Json",
            ]);

        var result = RuntimeDependencyPolicy.Validate(references);

        Assert.False(result.IsCompliant);
        Assert.Contains("Microsoft.Extensions.DependencyInjection", result.ForbiddenReferences);
        Assert.Contains("Newtonsoft.Json", result.ForbiddenReferences);
    }

    [Fact]
    public void DependencyRules_GivenOnlyFrameworkAndModusRuntimeReferences_ExpectedValidationPasses()
    {
        var references = new RuntimeReferenceSet(
            [
                "System.Runtime",
                "netstandard",
                "Modus.Host",
            ]);

        var result = RuntimeDependencyPolicy.Validate(references);

        Assert.True(result.IsCompliant);
        Assert.Empty(result.ForbiddenReferences);
    }
}
