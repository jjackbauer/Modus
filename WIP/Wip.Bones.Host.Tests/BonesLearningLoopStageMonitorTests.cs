using Wip.Abstractions.Artifacts;
using Wip.Abstractions.Identifiers;
using Wip.Bones.Host;
using Xunit;

namespace Wip.Bones.Host.Tests;

public sealed class BonesLearningLoopStageMonitorTests
{
    private static readonly string[] StageOrder = ["Ponder", "Play", "Evaluate", "Promote", "Enhance"];

    [Fact]
    public void ResolveStageFromArtifacts_GivenPlayCompletedAndEvaluationRunning_ExpectedEvaluate()
    {
        var descriptors = new[]
        {
            Descriptor("bones-ponder-execution"),
            Descriptor("bones-play-match-execution"),
        };

        var stage = BonesLearningLoopStageMonitor.ResolveStageFromArtifacts(
            descriptors,
            new HashSet<string>(StringComparer.Ordinal),
            StageOrder);

        Assert.Equal("Evaluate", stage);
    }

    [Fact]
    public void ResolveStageFromArtifacts_GivenPromotionArtifactWritten_ExpectedPromote()
    {
        var descriptors = new[]
        {
            Descriptor("bones-ponder-execution"),
            Descriptor("bones-play-match-execution"),
            Descriptor("bones-strategy-promotion-abc"),
        };

        var stage = BonesLearningLoopStageMonitor.ResolveStageFromArtifacts(
            descriptors,
            new HashSet<string>(StringComparer.Ordinal),
            StageOrder);

        Assert.Equal("Promote", stage);
    }

    [Fact]
    public void ResolveStageFromArtifacts_GivenEnhanceExecutionWritten_ExpectedEnhance()
    {
        var descriptors = new[]
        {
            Descriptor("bones-ponder-execution"),
            Descriptor("bones-play-match-execution"),
            Descriptor("bones-strategy-promotion-abc"),
            Descriptor("bones-enhance-strategy-execution"),
        };

        var stage = BonesLearningLoopStageMonitor.ResolveStageFromArtifacts(
            descriptors,
            new HashSet<string>(StringComparer.Ordinal),
            StageOrder);

        Assert.Equal("Enhance", stage);
    }

    [Fact]
    public void InferFailureStage_GivenPlayCompletedButEvaluationFailed_ExpectedEvaluate()
    {
        var descriptors = new[]
        {
            Descriptor("bones-ponder-execution"),
            Descriptor("bones-play-match-execution"),
        };

        Assert.Equal("Evaluate", BonesLearningLoopStageMonitor.InferFailureStage(descriptors));
    }

    private static ArtifactDescriptor Descriptor(string fileName)
        => new(
            new ArtifactId(Guid.NewGuid().ToString("N")),
            new SessionId("stage-monitor-test"),
            ArtifactKind.Json,
            $".wip/sessions/stage-monitor-test/artifacts/{fileName}.json",
            "test",
            "1.0.0",
            DateTimeOffset.UtcNow);
}
