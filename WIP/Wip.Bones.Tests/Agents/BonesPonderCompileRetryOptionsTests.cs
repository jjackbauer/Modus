using Wip.Bones.Agents.Ponder;
using Xunit;

namespace Wip.Bones.Tests.Agents;

public sealed class BonesPonderCompileRetryOptionsTests
{
    [Fact]
    [Trait("ChecklistItem", BonesLearningLoopReliabilityRequirementsChecklistItems.PonderCompileRetryOptions)]
    public void BonesPonderCompileRetryOptions_GivenConfiguredBudget_ExpectedRetryCountIsExposed()
    {
        var options = new BonesCompileRetryOptions
        {
            MaxCompileRetries = 4,
        };

        Assert.Equal(4, options.MaxCompileRetries);
        Assert.True(new BonesCompileRetryOptions().MaxCompileRetries >= 1);
    }
}
