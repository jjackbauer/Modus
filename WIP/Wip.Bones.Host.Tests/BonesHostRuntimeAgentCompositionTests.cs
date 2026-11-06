using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Wip.Abstractions.Artifacts;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Chat;
using Wip.Abstractions.Identifiers;
using Wip.Bones.Agents.Enhance;
using Wip.Bones.Agents.Ponder;
using Wip.Bones.Domain;
using Wip.Bones.Host;
using Wip.Bones.Identifiers;
using Xunit;

namespace Wip.Bones.Host.Tests;

/// <summary>
/// WP4 host-composition proof: the Bones learning host composes both domain agents over the
/// host-owned <see cref="IChatModel"/> (AgentExecutor + RuntimeToolInvoker + ArtifactAgentMemory)
/// and the stub chat model drives a full Ponder -> Enhance pass that persists a Script strategy.
/// </summary>
public sealed class BonesHostRuntimeAgentCompositionTests
{
    [Fact]
    public async Task AddBonesHostServices_GivenStubProvider_ExpectedPonderAndEnhanceComposeOverChatModelAndPersistScriptStrategy()
    {
        ClearBonesHostEnvironment();
        var dataDirectory = BonesHostTestPaths.CreateTempDataDirectory();

        try
        {
            var options = CreateHostOptions(dataDirectory);
            var services = new ServiceCollection();
            services.AddBonesHostServices(options);

            await using var provider = services.BuildServiceProvider();

            var chatModel = provider.GetRequiredService<IChatModel>();
            var ponderAgent = provider.GetRequiredService<BonesPonderAgent>();
            var enhanceAgent = provider.GetRequiredService<BonesEnhanceStrategyAgent>();
            var artifactStore = provider.GetRequiredService<IArtifactStore>();

            Assert.IsType<StubBonesChatModel>(chatModel);

            var sessionId = new SessionId("session-runtime-composition");
            var context = new CapabilityContext(sessionId, dataDirectory);
            var playerId = new BonesPlayerId(1);

            // ---- Ponder: chat model -> AgentExecutor -> RuntimeToolInvoker -> ArtifactAgentMemory ----
            // The host loop ponders every seat (AllSeatsLearning), and Enhance requires an active
            // opponent strategy per seat.
            BonesPonderResult? seatOneResult = null;
            for (var seat = BonesPlayerId.MinSeat; seat <= BonesPlayerId.MaxSeat; seat++)
            {
                var seatId = new BonesPlayerId(seat);
                var seatResult = await ponderAgent.ExecuteAsync(
                    new BonesPonderRequest(seatId, dataDirectory, "bones-strategy-author"),
                    context,
                    CancellationToken.None);

                Assert.Equal(new BonesStrategyId($"initial-seat-{seat}-v1"), seatResult.StrategyId);
                Assert.Equal(BonesStrategyKind.Script, seatResult.AuthoredStrategy.Kind);
                Assert.Equal(BonesPromotionStatus.Active, seatResult.AuthoredStrategy.PromotionStatus);
                Assert.Contains("class SeatStrategy", seatResult.StrategyDocument.Markdown, StringComparison.Ordinal);

                if (seat == playerId.Seat)
                    seatOneResult = seatResult;
            }

            var ponderResult = Assert.IsType<BonesPonderResult>(seatOneResult);

            var descriptors = await artifactStore.ListAsync(sessionId, CancellationToken.None);

            // The strategy artifact proves the stub chat model's script round-tripped through the
            // runtime compile tool and the knowledge store.
            Assert.Contains(
                descriptors,
                descriptor => descriptor.ArtifactId.Value == "bones-strategy-initial-seat-1-v1");

            // ArtifactAgentMemory writes agent-memory.json; its presence proves the memory port was
            // composed into the host-owned agents.
            Assert.Contains(
                descriptors,
                descriptor => string.Equals(descriptor.ProducerType, "ArtifactAgentMemory", StringComparison.Ordinal));

            // ---- Enhance: same composed runtime, promotion evaluation wired through the host ----
            var enhanceResult = await enhanceAgent.ExecuteAsync(
                new BonesEnhanceStrategyRequest(playerId, dataDirectory, ModelId: "bones-strategy-enhance"),
                context,
                CancellationToken.None);

            Assert.Equal(ponderResult.StrategyId, enhanceResult.PriorStrategyId);
            Assert.Equal(enhanceResult.StrategyId, enhanceResult.StrategyDocument.StrategyId);
            Assert.Equal(new BonesStrategyId("initial-seat-1-v2"), enhanceResult.StrategyId);
            Assert.Contains("class SeatStrategy", enhanceResult.StrategyDocument.Markdown, StringComparison.Ordinal);
            Assert.True(Enum.IsDefined(enhanceResult.PromotionOutcome));
            Assert.False(string.IsNullOrWhiteSpace(enhanceResult.ActiveStrategyId.Value));
        }
        finally
        {
            ClearBonesHostEnvironment();
            try
            {
                Directory.Delete(dataDirectory, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static BonesHostOptions CreateHostOptions(string dataDirectory)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BonesHost:DataDirectory"] = dataDirectory,
                ["BonesHost:ListenUrl"] = BonesHostTestNetwork.AllocateListenUrl().ToString(),
                ["BonesHost:ModelProvider"] = "stub",
                ["BonesHost:DefaultParameters:GameCount"] = "1",
                ["BonesHost:DefaultParameters:Seed"] = "4242",
                ["BonesHost:DefaultParameters:TargetScore"] = "8",
                ["BonesHost:DefaultParameters:LearningPlayerId"] = "1",
            })
            .Build();

        return BonesHostOptions.Bind(configuration);
    }

    private static void ClearBonesHostEnvironment()
    {
        Environment.SetEnvironmentVariable("BonesHost__ModelProvider", null);
        Environment.SetEnvironmentVariable("BonesHost__DeepSeek__BaseUrl", null);
        Environment.SetEnvironmentVariable("BonesHost__DeepSeek__Model", null);
        Environment.SetEnvironmentVariable("BonesHost__DeepSeek__TimeoutSeconds", null);
        Environment.SetEnvironmentVariable("BonesHost__DeepSeek__ApiKeySource", null);
    }
}
