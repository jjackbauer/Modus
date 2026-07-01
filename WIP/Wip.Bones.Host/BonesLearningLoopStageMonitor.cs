using Microsoft.Extensions.Logging;
using Wip.Abstractions.Artifacts;
using Wip.Abstractions.Identifiers;

namespace Wip.Bones.Host;

internal static class BonesLearningLoopStageMonitor
{
    public static async Task MonitorAsync(
        IArtifactStore artifactStore,
        SessionId sessionId,
        long iterationNumber,
        string? workflowSessionId,
        string? matchId,
        BonesLoopState state,
        CancellationToken cancellationToken,
        ILogger? logger = null)
    {
        var observedStages = new HashSet<string>(StringComparer.Ordinal);
        var stageOrder = new[] { "Ponder", "Play", "Enhance" };

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var descriptors = await artifactStore.ListAsync(sessionId, cancellationToken);
                var stageName = ResolveStageFromArtifacts(descriptors, observedStages, stageOrder);
                if (!string.IsNullOrWhiteSpace(stageName))
                {
                    state.UpdateCurrentIteration(new BonesLoopIterationStatus(
                        iterationNumber,
                        stageName,
                        workflowSessionId,
                        matchId,
                        false));
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger?.LogWarning(ex, "Transient error listing artifacts for session {SessionId}; will retry on next poll.", sessionId);
            }

            try
            {
                await Task.Delay(50, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private static string? ResolveStageFromArtifacts(
        IReadOnlyList<ArtifactDescriptor> descriptors,
        HashSet<string> observedStages,
        IReadOnlyList<string> stageOrder)
    {
        var hasPonder = descriptors.Any(descriptor =>
            descriptor.RelativePath.Contains("bones-ponder-execution", StringComparison.Ordinal));
        var hasPlay = descriptors.Any(descriptor =>
            descriptor.RelativePath.Contains("bones-play-match-execution", StringComparison.Ordinal));
        var hasEnhance = descriptors.Any(descriptor =>
            descriptor.RelativePath.Contains("bones-enhance-strategy-execution", StringComparison.Ordinal));

        if (hasEnhance)
            observedStages.Add("Enhance");
        if (hasPlay)
            observedStages.Add("Play");
        if (hasPonder)
            observedStages.Add("Ponder");

        foreach (var stage in stageOrder.Reverse())
        {
            if (observedStages.Contains(stage))
                return stage;
        }

        return hasPonder ? "Ponder" : null;
    }

    public static string? InferFailureStage(IReadOnlyList<ArtifactDescriptor> descriptors)
    {
        var hasPonder = descriptors.Any(descriptor =>
            descriptor.RelativePath.Contains("bones-ponder-execution", StringComparison.Ordinal));
        var hasPlay = descriptors.Any(descriptor =>
            descriptor.RelativePath.Contains("bones-play-match-execution", StringComparison.Ordinal));
        var hasEnhance = descriptors.Any(descriptor =>
            descriptor.RelativePath.Contains("bones-enhance-strategy-execution", StringComparison.Ordinal));

        if (!hasPonder)
            return "Ponder";

        if (!hasPlay)
            return "Play";

        if (!hasEnhance)
            return "Enhance";

        return "Workflow";
    }
}

internal static class BonesWorkflowStageFailureRecorder
{
    private const int MaxExcerptLength = 4096;

    public sealed record FailureRecord(
        string FailureStage,
        string ErrorMessage,
        int RetryCount,
        string? ModelProviderResponseExcerpt,
        DateTimeOffset RecordedAtUtc);

    public static async Task SaveAsync(
        IArtifactStore artifactStore,
        SessionId sessionId,
        string failureStage,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        await SaveAsync(
            artifactStore,
            sessionId,
            new FailureRecord(
                failureStage,
                errorMessage,
                0,
                null,
                DateTimeOffset.UtcNow),
            cancellationToken);
    }

    public static async Task SaveAsync(
        IArtifactStore artifactStore,
        SessionId sessionId,
        FailureRecord failure,
        CancellationToken cancellationToken)
    {
        var excerpt = failure.ModelProviderResponseExcerpt;
        if (excerpt is { Length: > MaxExcerptLength })
            excerpt = excerpt[..MaxExcerptLength];

        var payload = new
        {
            FailureStage = failure.FailureStage,
            ErrorMessage = failure.ErrorMessage,
            RetryCount = failure.RetryCount,
            ModelProviderResponseExcerpt = excerpt,
            RecordedAtUtc = failure.RecordedAtUtc,
        };

        await artifactStore.SaveAsync(
            sessionId,
            new ArtifactContent(
                artifactId: new ArtifactId($"bones-workflow-stage-failure-{Guid.NewGuid():N}"),
                kind: ArtifactKind.Json,
                fileName: "bones-workflow-stage-failure",
                content: System.Text.Json.JsonSerializer.Serialize(payload),
                producerType: "Wip.Bones.Host.BonesLearningLoopHost",
                producerVersion: "1.0.0",
                producedAtUtc: DateTimeOffset.UtcNow),
            cancellationToken);
    }
}