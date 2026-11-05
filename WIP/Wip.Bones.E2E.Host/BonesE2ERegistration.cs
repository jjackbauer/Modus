using Microsoft.Extensions.DependencyInjection;
using Wip.Abstractions.Artifacts;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Sessions;
using Wip.Artifacts.Local;
using Wip.Bones.Agents.Enhance;
using Wip.Bones.Agents.Play;
using Wip.Bones.Agents.Ponder;
using Wip.Bones.Agents.Viewer;
using Wip.Bones.Agents.Workflow;
using Wip.Bones.Engine;
using Wip.Bones.Identifiers;
using Wip.Builder;

namespace Wip.Bones.E2E.Host;

/// <summary>
/// Bones shell-host registration used only by the E2E transcript harness. It lives in this
/// test-only host so <c>Wip.ShellHost</c> has no dependency on any <c>Wip.Bones.*</c> project.
/// </summary>
public static class BonesE2ERegistration
{
    public const string EnableEnvironmentVariable = "WIP_BONES_E2E";

    public static void Register(WipBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var repositoryPath = Directory.GetCurrentDirectory();
        var artifactStore = new WipArtifactStoreLocal(repositoryPath);

        builder.Services.AddSingleton<IArtifactStore>(artifactStore);
        builder.Services.AddSingleton(new BonesMatchSimulator());
        builder.Services.AddSingleton(new BonesGameEngine());
        builder.Services.AddSingleton<IModelProvider<BonesStrategyAuthoringRequest, BonesStrategyAuthoringResult>, StubBonesStrategyModelProvider>();
        builder.Services.AddSingleton<IModelProvider<BonesPlayTurnRequest, BonesPlayTurnResult>, StubBonesPlayTurnModelProvider>();
        builder.Services.AddSingleton<IModelProvider<BonesStrategyEnhancementRequest, BonesStrategyEnhancementResult>, StubBonesEnhancementModelProvider>();
        builder.Services.AddSingleton<IBonesPublicMatchFeed, NoOpBonesPublicMatchFeed>();

        builder.AddBonesLearningWorkflowWithViewer(new Uri("http://127.0.0.1:8765"));
    }

    private sealed class NoOpBonesPublicMatchFeed : IBonesPublicMatchFeed
    {
        public void PublishMatchState(
            SessionId sessionId,
            BonesGameId matchId,
            int matchSeed,
            BonesMatchResult matchResult,
            bool isComplete)
        {
        }
    }

    private sealed class StubBonesStrategyModelProvider
        : IModelProvider<BonesStrategyAuthoringRequest, BonesStrategyAuthoringResult>
    {
        public ValueTask<ModelProviderResponse<BonesStrategyAuthoringResult>> ExecuteAsync(
            ModelProviderRequest<BonesStrategyAuthoringRequest> request,
            CapabilityContext context,
            CancellationToken cancellationToken)
        {
            const string markdown = """
                # Bones Strategy

                ## Rules
                - Prefer matching open ends before passing.
                - Track board state transitions from observation transcripts.
                - Use GetLegalMoves options only; pass when the legal set is empty.
                """;

            return ValueTask.FromResult(
                new ModelProviderResponse<BonesStrategyAuthoringResult>(
                    payload: new BonesStrategyAuthoringResult(markdown),
                    providerId: "bones-strategy-stub",
                    modelId: request.ModelId,
                    usage: new ModelProviderUsage(120, 48),
                    correlationId: request.CorrelationId));
        }
    }

    private sealed class StubBonesPlayTurnModelProvider
        : IModelProvider<BonesPlayTurnRequest, BonesPlayTurnResult>
    {
        public ValueTask<ModelProviderResponse<BonesPlayTurnResult>> ExecuteAsync(
            ModelProviderRequest<BonesPlayTurnRequest> request,
            CapabilityContext context,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(
                new ModelProviderResponse<BonesPlayTurnResult>(
                    payload: new BonesPlayTurnResult(request.Payload.AllowedMoves[0].MoveId.Value),
                    providerId: "bones-play-turn-stub",
                    modelId: request.ModelId,
                    usage: new ModelProviderUsage(64, 16),
                    correlationId: request.CorrelationId));
    }

    private sealed class StubBonesEnhancementModelProvider
        : IModelProvider<BonesStrategyEnhancementRequest, BonesStrategyEnhancementResult>
    {
        public ValueTask<ModelProviderResponse<BonesStrategyEnhancementResult>> ExecuteAsync(
            ModelProviderRequest<BonesStrategyEnhancementRequest> request,
            CapabilityContext context,
            CancellationToken cancellationToken)
        {
            const string markdown = """
                # Bones Strategy

                ## Rules
                - Prefer matching open ends before passing.
                - After a loss, prioritize blocking opponent scoring lines earlier in the round.
                - Use GetLegalMoves options only; pass when the legal set is empty.

                ## Enhancements
                - Refined tactics based on match outcome review.
                """;

            return ValueTask.FromResult(
                new ModelProviderResponse<BonesStrategyEnhancementResult>(
                    payload: new BonesStrategyEnhancementResult(markdown),
                    providerId: "bones-enhance-stub",
                    modelId: request.ModelId,
                    usage: new ModelProviderUsage(140, 60),
                    correlationId: request.CorrelationId));
        }
    }
}