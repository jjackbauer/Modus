using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Wip.Abstractions.Agents;
using Wip.Abstractions.Artifacts;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Chat;
using Wip.Abstractions.Descriptors;
using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Tools;
using Wip.Artifacts.Local;
using Wip.Bones.Agents.Enhance;
using Wip.Bones.Agents.Knowledge;
using Wip.Bones.Agents.Ponder;
using Wip.Bones.Agents.Runtime;
using Wip.Bones.Domain;
using Wip.Bones.Engine;
using Wip.Bones.Identifiers;
using Wip.Runtime.Runtime;
using Xunit;

namespace Wip.Bones.Tests.Runtime;

public sealed class BonesEnhanceRuntimeTests
{
    private const string FirstLegalMoveScriptSource = """
        using System.Linq;
        using Wip.Bones.Domain;
        using Wip.Bones.Engine;

        public sealed class SeatStrategy : IBonesPlayerSlot
        {
            public BonesMove ChooseMove(BonesRoundState state, IReadOnlyList<BonesMove> legalMoves)
            {
                return legalMoves.OrderBy(move => move.MoveId.Value, System.StringComparer.Ordinal).First();
            }
        }
        """;

    private const string InvalidScriptSource = """
        using System.Linq;
        using Wip.Bones.Domain;
        using Wip.Bones.Engine;

        public sealed class SeatStrategy : IBonesPlayerSlot
        {
            public BonesMove ChooseMove(BonesRoundState state, IReadOnlyList<BonesMove> legalMoves)
            {
                return legalMoves[0];
            }
        """;

    private const string ValidMarkdown = $"```csharp\n{FirstLegalMoveScriptSource}\n```";

    [Fact]
    public async Task ExecuteAsync_GivenSameInputThroughLegacyAndRuntimePaths_ExpectedDeterministicParity()
    {
        // Arrange — two isolated repositories with the same session, player, prior script,
        // match histories, opponents, promotion options and explicit evaluation seed.
        var sessionId = new SessionId("parity-session-wp3");
        var playerId = new BonesPlayerId(2);
        var matchGameId = new BonesGameId("parity-match-b");

        await using var legacyFixture = await EnhanceFixture.CreateAsync(sessionId);
        await using var runtimeFixture = await EnhanceFixture.CreateAsync(sessionId);

        foreach (var fixture in new[] { legacyFixture, runtimeFixture })
        {
            await fixture.SaveAllSeatStrategiesAsync();
            await fixture.SaveMatchHistoryAsync(playerId, new BonesGameId("parity-match-a"));
            await fixture.SaveMatchHistoryAsync(playerId, new BonesGameId("parity-match-b"));
        }

        var legacyProvider = new SingleAnswerEnhancementModelProvider(ValidMarkdown);
        var legacyAgent = new BonesEnhanceStrategyAgent(
            legacyFixture.Store,
            legacyProvider,
            promotionOptions: new BonesStrategyPromotionOptions { EvaluationMatchCount = 5 });

        var scriptHost = new BonesStrategyScriptHost();
        using var tooling = new RuntimeTooling(scriptHost);
        var runtimeModel = new SingleAnswerChatModel(ValidMarkdown);
        var runtimeAgent = new BonesEnhanceStrategyAgent(
            runtimeFixture.Store,
            tooling.CreateExecutor(runtimeModel),
            promotionOptions: new BonesStrategyPromotionOptions { EvaluationMatchCount = 5 },
            memory: null,
            loopOptions: new BonesAgentLoopOptions());

        // Act
        var legacyResult = await legacyAgent.ExecuteAsync(
            legacyFixture.Request(playerId, matchGameId, evaluationSeed: 9001),
            legacyFixture.Context(),
            CancellationToken.None);
        var runtimeResult = await runtimeAgent.ExecuteAsync(
            runtimeFixture.Request(playerId, matchGameId, evaluationSeed: 9001),
            runtimeFixture.Context(),
            CancellationToken.None);

        var legacyCandidate = await legacyFixture.LoadStrategyAsync(playerId, legacyResult.StrategyId);
        var runtimeCandidate = await runtimeFixture.LoadStrategyAsync(playerId, runtimeResult.StrategyId);
        var legacyPromotionJson = await legacyFixture.ReadArtifactAsync(legacyResult.PromotionDecisionArtifact);
        var runtimePromotionJson = await runtimeFixture.ReadArtifactAsync(runtimeResult.PromotionDecisionArtifact);
        var legacyExecutionJson = await legacyFixture.ReadArtifactAsync(legacyResult.ExecutionArtifact);
        var runtimeExecutionJson = await runtimeFixture.ReadArtifactAsync(runtimeResult.ExecutionArtifact);

        var legacyModelRequest = Assert.Single(legacyProvider.CapturedRequests);
        var runtimeModelRequest = Assert.Single(runtimeModel.CapturedRequests);

        // Assert — exact domain results (old path and new path).
        Assert.Equal(new BonesStrategyId("initial-seat-2-v1"), legacyResult.PriorStrategyId);
        Assert.Equal(new BonesStrategyId("initial-seat-2-v1"), runtimeResult.PriorStrategyId);
        Assert.Equal(new BonesStrategyId("initial-seat-2-v2"), legacyResult.StrategyId);
        Assert.Equal(new BonesStrategyId("initial-seat-2-v2"), runtimeResult.StrategyId);
        Assert.Equal(2, runtimeResult.StrategyDocument.PlayerId.Seat);
        Assert.Equal(FirstLegalMoveScriptSource, runtimeResult.StrategyDocument.Markdown);
        Assert.Equal(2, runtimeResult.MatchHistoriesLoaded);
        Assert.Equal(BonesPromotionDecisionOutcome.Rejected, runtimeResult.PromotionOutcome);
        Assert.Equal(new BonesStrategyId("initial-seat-2-v1"), runtimeResult.ActiveStrategyId);
        Assert.Equal(0, runtimeResult.StagnationDetected);
        Assert.Equal(1, runtimeResult.EnhanceIterationsCompleted);

        // Assert — persisted candidate artifact (source/status read back from the store).
        Assert.Equal("bones-strategy-initial-seat-2-v2", runtimeResult.StrategyArtifact.ArtifactId.Value);
        Assert.Equal(ArtifactKind.Json, runtimeResult.StrategyArtifact.Kind);
        Assert.Equal("Wip.Bones.Agents.BonesPlayerKnowledgeStore", runtimeResult.StrategyArtifact.ProducerType);
        Assert.Equal("1.0.0", runtimeResult.StrategyArtifact.ProducerVersion);
        Assert.StartsWith(
            ".wip/sessions/parity-session-wp3/artifacts/",
            runtimeResult.StrategyArtifact.RelativePath,
            StringComparison.Ordinal);
        Assert.Contains(
            "bones-player-2-strategy-initial-seat-2-v2",
            runtimeResult.StrategyArtifact.RelativePath,
            StringComparison.Ordinal);
        Assert.Equal(FirstLegalMoveScriptSource, runtimeCandidate.Source);
        Assert.Equal(BonesStrategyKind.Script, runtimeCandidate.Kind);
        Assert.Equal(BonesPromotionStatus.Rejected, runtimeCandidate.PromotionStatus);

        // Assert — execution artifact JSON fields (GUID artifact ids / timestamps excluded).
        using (var execution = JsonDocument.Parse(runtimeExecutionJson))
        {
            var root = execution.RootElement;
            Assert.Equal(2, root.GetProperty("PlayerSeat").GetInt32());
            Assert.Equal("initial-seat-2-v1", root.GetProperty("PriorStrategyId").GetString());
            Assert.Equal("initial-seat-2-v2", root.GetProperty("StrategyId").GetString());
            Assert.Equal("parity-match-b", root.GetProperty("MatchGameId").GetString());
            Assert.Equal("bones-strategy-enhance", root.GetProperty("ModelId").GetString());
            Assert.Equal(2, root.GetProperty("MatchHistoriesLoaded").GetInt32());
            Assert.Equal(1, root.GetProperty("CompileAttempts").GetInt32());
            Assert.Equal("play", root.GetProperty("PriorStage").GetString());
            Assert.Equal("Rejected", root.GetProperty("PromotionOutcome").GetString());
        }

        // Assert — promotion-decision artifact JSON fields (deterministic metrics).
        using (var decision = JsonDocument.Parse(runtimePromotionJson))
        {
            var root = decision.RootElement;
            Assert.Equal("initial-seat-2-v1", root.GetProperty("IncumbentStrategyId").GetString());
            Assert.Equal(2, root.GetProperty("LearningPlayerSeat").GetInt32());
            Assert.Equal("initial-seat-2-v2", root.GetProperty("CandidateStrategyId").GetString());
            Assert.Equal("Rejected", root.GetProperty("Outcome").GetString());
            Assert.Equal(5, root.GetProperty("EvaluationMatchCount").GetInt32());
            Assert.Equal(
                root.GetProperty("IncumbentWins").GetInt32(),
                root.GetProperty("CandidateWins").GetInt32());
            Assert.Equal(0.0, root.GetProperty("ScoreDifferentialDelta").GetDouble());
        }

        // Assert — first-turn prompt parity (role + content) between the legacy provider request
        // and the provider-neutral ChatRequest.
        Assert.Equal(2, legacyModelRequest.Payload.Messages.Count);
        Assert.Equal(2, runtimeModelRequest.Messages.Count);
        Assert.Equal("system", legacyModelRequest.Payload.Messages[0].Role);
        Assert.Equal("user", legacyModelRequest.Payload.Messages[1].Role);
        Assert.Equal(ChatRole.System, runtimeModelRequest.Messages[0].Role);
        Assert.Equal(ChatRole.User, runtimeModelRequest.Messages[1].Role);
        Assert.Equal(
            legacyModelRequest.Payload.Messages[0].Content,
            runtimeModelRequest.Messages[0].Content);
        Assert.Equal(
            legacyModelRequest.Payload.Messages[1].Content,
            runtimeModelRequest.Messages[1].Content);

        var userPrompt = runtimeModelRequest.Messages[1].Content;
        Assert.Contains("Player seat: 2", userPrompt, StringComparison.Ordinal);
        Assert.Contains("Prior strategy id: initial-seat-2-v1", userPrompt, StringComparison.Ordinal);
        Assert.Contains("Match history count: 2", userPrompt, StringComparison.Ordinal);
        Assert.Contains("parity-match-a", userPrompt, StringComparison.Ordinal);
        Assert.Contains("parity-match-b", userPrompt, StringComparison.Ordinal);
        Assert.Equal("bones-strategy-enhance", runtimeModelRequest.ModelId);
        var sentTool = Assert.Single(runtimeModelRequest.Tools);
        Assert.Equal("tool.bones.strategy-compile", sentTool.Name);

        // Assert — the shared comparison helper reports no mismatch for the deterministic snapshot.
        var legacySnapshot = EnhanceParitySnapshot.Create(
            legacyResult,
            legacyCandidate,
            legacyPromotionJson,
            legacyExecutionJson,
            ToPromptLines(legacyModelRequest.Payload.Messages));
        var runtimeSnapshot = EnhanceParitySnapshot.Create(
            runtimeResult,
            runtimeCandidate,
            runtimePromotionJson,
            runtimeExecutionJson,
            ToPromptLines(runtimeModelRequest.Messages));
        Assert.Empty(EnhanceParitySnapshot.FindMismatches(legacySnapshot, runtimeSnapshot));
    }

    [Fact]
    public void FindMismatches_GivenAlteredPromptOrResult_ExpectedDifferenceReported()
    {
        // Arrange
        var prompts = new[]
        {
            new PromptLine("system", "system prompt"),
            new PromptLine("user", "user prompt"),
        };

        var expected = new EnhanceParitySnapshot(
            "initial-seat-2-v1",
            "initial-seat-2-v2",
            "strategy markdown",
            2,
            "bones-strategy-initial-seat-2-v2",
            "Json",
            "Wip.Bones.Agents.BonesPlayerKnowledgeStore",
            "1.0.0",
            "bones-player-2-strategy-initial-seat-2-v2",
            "strategy source",
            "Script",
            "Rejected",
            2,
            "Rejected",
            "initial-seat-2-v1",
            0,
            1,
            "{\"Outcome\":\"Rejected\"}",
            "{\"PlayerSeat\":2}",
            prompts);

        var identical = expected with { Prompts = new[] { prompts[0], prompts[1] } };

        // Act / Assert — the helper is quiet for identical snapshots.
        Assert.Empty(EnhanceParitySnapshot.FindMismatches(expected, identical));

        // Act / Assert — a one-character prompt change is reported.
        var alteredPrompt = identical with
        {
            Prompts = new[] { prompts[0], new PromptLine("user", "user prompt altered") },
        };
        var promptMismatch = Assert.Single(EnhanceParitySnapshot.FindMismatches(expected, alteredPrompt));
        Assert.Contains("Prompts[1].Content", promptMismatch, StringComparison.Ordinal);
        Assert.Contains("user prompt altered", promptMismatch, StringComparison.Ordinal);

        // Act / Assert — an altered result field is reported too.
        var alteredResult = identical with { ExecutionArtifactJson = "{\"PlayerSeat\":3}" };
        var resultMismatch = Assert.Single(EnhanceParitySnapshot.FindMismatches(expected, alteredResult));
        Assert.Contains("ExecutionArtifactJson", resultMismatch, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_GivenInvalidScriptThenCompileToolThenValidScript_ExpectedDiagnosticsInToolMessageAndStrategyPersisted()
    {
        // Arrange
        await using var fixture = await EnhanceFixture.CreateAsync(new SessionId("tool-routing-wp3"));
        var playerId = new BonesPlayerId(1);
        await fixture.SaveAllSeatStrategiesAsync();

        var scriptHost = new BonesStrategyScriptHost();
        using var tooling = new RuntimeTooling(scriptHost);
        var model = new DiagnosticsThenFinalChatModel(InvalidScriptSource, ValidMarkdown);
        var agent = new BonesEnhanceStrategyAgent(
            fixture.Store,
            tooling.CreateExecutor(model),
            promotionOptions: new BonesStrategyPromotionOptions { EvaluationMatchCount = 5 },
            memory: null,
            loopOptions: new BonesAgentLoopOptions());

        // Act
        var result = await agent.ExecuteAsync(
            fixture.Request(playerId, new BonesGameId("tool-routing-match"), evaluationSeed: 9001),
            fixture.Context(),
            CancellationToken.None);

        // Assert — turn 2 request contains the tool observation with compiler diagnostics.
        Assert.Equal(2, model.CapturedRequests.Count);
        var secondTurnMessages = model.CapturedRequests[1].Messages;
        Assert.Equal(4, secondTurnMessages.Count);
        Assert.Equal(ChatRole.System, secondTurnMessages[0].Role);
        Assert.Equal(ChatRole.User, secondTurnMessages[1].Role);
        Assert.Equal(ChatRole.Assistant, secondTurnMessages[2].Role);

        var toolCall = Assert.Single(secondTurnMessages[2].ToolCalls);
        Assert.Equal("tool.bones.strategy-compile", toolCall.Name);

        var toolMessage = secondTurnMessages[3];
        Assert.Equal(ChatRole.Tool, toolMessage.Role);
        Assert.Equal(toolCall.Id, toolMessage.ToolCallId);

        using (var toolObservation = JsonDocument.Parse(toolMessage.Content))
        {
            Assert.False(toolObservation.RootElement.GetProperty("succeeded").GetBoolean());
            var failureReason = toolObservation.RootElement.GetProperty("failureReason").GetString();
            Assert.False(string.IsNullOrWhiteSpace(failureReason));
            Assert.Contains("Strategy script compilation failed", failureReason!, StringComparison.Ordinal);
            Assert.Contains("} expected", failureReason!, StringComparison.Ordinal);
        }

        // Assert — the valid script from turn 2 is parsed, compiled and persisted for this seat.
        Assert.Equal(new BonesStrategyId("initial-seat-1-v2"), result.StrategyId);
        Assert.Equal(FirstLegalMoveScriptSource, result.StrategyDocument.Markdown);

        var persistedStrategy = await fixture.LoadStrategyAsync(playerId, result.StrategyId);
        Assert.Equal(FirstLegalMoveScriptSource, persistedStrategy.Source);
        Assert.Equal(BonesPromotionStatus.Rejected, persistedStrategy.PromotionStatus);

        var artifacts = await fixture.ListArtifactsAsync();
        Assert.Contains(
            artifacts,
            descriptor => descriptor.ArtifactId.Value == "bones-strategy-initial-seat-1-v2");
    }

    [Fact]
    public async Task ExecuteAsync_GivenModelAlwaysCallsCompileTool_ExpectedMaxStepsReachedWithoutStrategyArtifact()
    {
        // Arrange
        await using var fixture = await EnhanceFixture.CreateAsync(new SessionId("max-steps-wp3"));
        var playerId = new BonesPlayerId(3);
        await fixture.SaveAllSeatStrategiesAsync();

        var scriptHost = new BonesStrategyScriptHost();
        using var tooling = new RuntimeTooling(scriptHost);
        var model = new AlwaysToolCallChatModel(
            BonesAgentToolIds.StrategyCompile.Value,
            JsonSerializer.SerializeToElement(new { scriptSource = FirstLegalMoveScriptSource }));
        var agent = new BonesEnhanceStrategyAgent(
            fixture.Store,
            tooling.CreateExecutor(model),
            promotionOptions: new BonesStrategyPromotionOptions { EvaluationMatchCount = 1 },
            memory: null,
            loopOptions: new BonesAgentLoopOptions { MaxSteps = 2 });

        // Act
        var exception = await Assert.ThrowsAsync<BonesAgentExecutionException>(() =>
            agent.ExecuteAsync(
                fixture.Request(playerId, new BonesGameId("max-steps-match"), evaluationSeed: 9001),
                fixture.Context(),
                CancellationToken.None).AsTask());

        // Assert
        Assert.Equal(AgentExecutionStatus.MaxStepsReached, exception.Status);
        Assert.Equal(2, exception.Steps.Count);
        Assert.All(exception.Steps, step => Assert.Single(step.ToolResults));
        Assert.Contains(
            "Max steps (2) reached without a final answer.",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Equal(2, model.CallCount);

        var artifacts = await fixture.ListArtifactsAsync();
        Assert.DoesNotContain(
            artifacts,
            descriptor => descriptor.ArtifactId.Value == "bones-strategy-initial-seat-3-v2");
    }

    [Fact]
    public async Task ExecuteAsync_GivenModelAlwaysCallsUnregisteredTool_ExpectedToolFailureLimitReached()
    {
        // Arrange
        await using var fixture = await EnhanceFixture.CreateAsync(new SessionId("tool-failure-wp3"));
        var playerId = new BonesPlayerId(4);
        await fixture.SaveAllSeatStrategiesAsync();

        var scriptHost = new BonesStrategyScriptHost();
        using var tooling = new RuntimeTooling(scriptHost);
        var model = new AlwaysToolCallChatModel(
            "tool.bones.unregistered",
            JsonSerializer.SerializeToElement(new { value = "ignored" }));
        var agent = new BonesEnhanceStrategyAgent(
            fixture.Store,
            tooling.CreateExecutor(model),
            promotionOptions: new BonesStrategyPromotionOptions { EvaluationMatchCount = 1 },
            memory: null,
            loopOptions: new BonesAgentLoopOptions { MaxConsecutiveToolFailures = 3 });

        // Act
        var exception = await Assert.ThrowsAsync<BonesAgentExecutionException>(() =>
            agent.ExecuteAsync(
                fixture.Request(playerId, new BonesGameId("tool-failure-match"), evaluationSeed: 9001),
                fixture.Context(),
                CancellationToken.None).AsTask());

        // Assert
        Assert.Equal(AgentExecutionStatus.ToolFailureLimitReached, exception.Status);
        Assert.Equal(3, exception.Steps.Count);
        var lastToolResult = Assert.Single(exception.Steps[^1].ToolResults);
        Assert.True(lastToolResult.IsError);
        Assert.Contains(
            "unknown-tool: no tool named 'tool.bones.unregistered' is registered.",
            lastToolResult.Content,
            StringComparison.Ordinal);
        Assert.Contains("Tool failure limit (3) reached.", exception.Message, StringComparison.Ordinal);

        var artifacts = await fixture.ListArtifactsAsync();
        Assert.DoesNotContain(
            artifacts,
            descriptor => descriptor.ArtifactId.Value == "bones-strategy-initial-seat-4-v2");
    }

    [Fact]
    public async Task ExecuteAsync_GivenMemorySnapshot_ExpectedMemoryMessageBeforeCurrentConversationAndExchangeAppended()
    {
        // Arrange
        await using var fixture = await EnhanceFixture.CreateAsync(new SessionId("memory-wp3"));
        var playerId = new BonesPlayerId(2);
        await fixture.SaveAllSeatStrategiesAsync();

        var memory = new ArtifactAgentMemory(fixture.Store);
        const string memoryMessage = "Earlier enhance turn: seat 2 preferred open ends 6-4.";
        await memory.AppendAsync(
            fixture.SessionId,
            ChatMessage.User(memoryMessage),
            CancellationToken.None);

        var scriptHost = new BonesStrategyScriptHost();
        using var tooling = new RuntimeTooling(scriptHost);
        var model = new SingleAnswerChatModel(ValidMarkdown);
        var agent = new BonesEnhanceStrategyAgent(
            fixture.Store,
            tooling.CreateExecutor(model),
            promotionOptions: new BonesStrategyPromotionOptions { EvaluationMatchCount = 1 },
            memory: memory,
            loopOptions: new BonesAgentLoopOptions());

        // Act
        await agent.ExecuteAsync(
            fixture.Request(playerId, new BonesGameId("memory-match"), evaluationSeed: 9001),
            fixture.Context(),
            CancellationToken.None);

        // Assert — first captured request: system, memory message, current user prompt.
        var firstRequest = Assert.Single(model.CapturedRequests);
        Assert.Equal(3, firstRequest.Messages.Count);
        Assert.Equal(ChatRole.System, firstRequest.Messages[0].Role);
        Assert.Equal(ChatRole.User, firstRequest.Messages[1].Role);
        Assert.Equal(memoryMessage, firstRequest.Messages[1].Content);
        Assert.Equal(ChatRole.User, firstRequest.Messages[2].Role);
        Assert.Contains(
            "# Strategy script enhancement request",
            firstRequest.Messages[2].Content,
            StringComparison.Ordinal);
        Assert.Contains("Player seat: 2", firstRequest.Messages[2].Content, StringComparison.Ordinal);

        // Assert — the memory artifact after the run holds the pre-seeded, new user and new
        // assistant messages in order.
        var snapshot = await memory.LoadAsync(fixture.SessionId, CancellationToken.None);
        Assert.Equal(3, snapshot.Messages.Count);
        Assert.Equal(memoryMessage, snapshot.Messages[0].Content);
        Assert.Equal(ChatRole.User, snapshot.Messages[1].Role);
        Assert.Contains(
            "# Strategy script enhancement request",
            snapshot.Messages[1].Content,
            StringComparison.Ordinal);
        Assert.Equal(ChatRole.Assistant, snapshot.Messages[2].Role);
        Assert.Equal(ValidMarkdown, snapshot.Messages[2].Content);

        // Assert — the artifact is the local store's normalized agent-memory file.
        var artifacts = await fixture.ListArtifactsAsync();
        Assert.Contains(
            artifacts,
            descriptor => descriptor.ProducerType == "ArtifactAgentMemory"
                && descriptor.RelativePath.StartsWith(
                    ".wip/sessions/memory-wp3/artifacts/agent-memory-agent-memory-",
                    StringComparison.Ordinal)
                && descriptor.RelativePath.EndsWith(".json", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_GivenCoLearningAllSeats_ExpectedFourSeatPromptsAndStrategiesWithLastSeatResultReturned()
    {
        // Arrange — the unchanged wrapper runs the new-path single-seat agent for every seat.
        await using var fixture = await EnhanceFixture.CreateAsync(new SessionId("co-learning-wp3"));
        await fixture.SaveAllSeatStrategiesAsync();
        var strategyPool = new StrategyPool(new Random(17));

        var scriptHost = new BonesStrategyScriptHost();
        using var tooling = new RuntimeTooling(scriptHost);
        var model = new SingleAnswerChatModel(ValidMarkdown);
        var singleSeatAgent = new BonesEnhanceStrategyAgent(
            fixture.Store,
            tooling.CreateExecutor(model),
            promotionOptions: new BonesStrategyPromotionOptions { EvaluationMatchCount = 1 },
            memory: null,
            loopOptions: new BonesAgentLoopOptions());
        var coLearningAgent = new BonesCoLearningEnhanceAgent(singleSeatAgent, strategyPool);

        var request = new BonesEnhanceStrategyRequest(
            new BonesPlayerId(1),
            fixture.RepositoryPath,
            AllSeatsLearning: true);

        // Act
        var result = await coLearningAgent.ExecuteAsync(
            request,
            fixture.Context(),
            CancellationToken.None);

        // Assert — all four seats were prompted with their own seat number and prior strategy id.
        Assert.Equal(4, model.CapturedRequests.Count);
        for (var seat = BonesPlayerId.MinSeat; seat <= BonesPlayerId.MaxSeat; seat++)
        {
            var seatRequest = Assert.Single(
                model.CapturedRequests,
                captured => captured.Messages.Any(message =>
                    message.Role == ChatRole.User
                    && message.Content.Contains($"Player seat: {seat}", StringComparison.Ordinal)
                    && message.Content.Contains(
                        $"Prior strategy id: initial-seat-{seat}-v1",
                        StringComparison.Ordinal)));
            Assert.Contains(
                "Match history count: 0",
                seatRequest.Messages[^1].Content,
                StringComparison.Ordinal);
        }

        // Assert — each seat owns its own persisted candidate strategy and pool entry.
        for (var seat = BonesPlayerId.MinSeat; seat <= BonesPlayerId.MaxSeat; seat++)
        {
            var seatPlayerId = new BonesPlayerId(seat);
            var candidateId = new BonesStrategyId($"initial-seat-{seat}-v2");
            var candidate = await fixture.LoadStrategyAsync(seatPlayerId, candidateId);
            Assert.Equal(seat, candidate.PlayerId.Seat);
            Assert.Equal(FirstLegalMoveScriptSource, candidate.Source);
            Assert.Equal(BonesPromotionStatus.Rejected, candidate.PromotionStatus);
            Assert.Contains(
                strategyPool.Pool.Values,
                entry => entry.Seat == seat && entry.Id == candidateId);
        }

        Assert.Equal(4, strategyPool.Count);

        // Assert — the wrapper returns the last completed seat result (seat 4).
        Assert.Equal(new BonesStrategyId("initial-seat-4-v1"), result.PriorStrategyId);
        Assert.Equal(new BonesStrategyId("initial-seat-4-v2"), result.StrategyId);
        Assert.Equal(4, result.StrategyDocument.PlayerId.Seat);
        Assert.Equal(new BonesStrategyId("initial-seat-4-v1"), result.ActiveStrategyId);
        Assert.Equal(BonesPromotionDecisionOutcome.Rejected, result.PromotionOutcome);
    }

    [Fact]
    public async Task ExecuteAsync_GivenCompletedExecutionWithoutFinalMessage_ExpectedInvalidOperationException()
    {
        // Arrange
        await using var fixture = await EnhanceFixture.CreateAsync(new SessionId("null-final-wp3"));
        await fixture.SaveAllSeatStrategiesAsync();
        var executor = new NullFinalMessageExecutor();
        var agent = new BonesEnhanceStrategyAgent(
            fixture.Store,
            executor,
            promotionOptions: new BonesStrategyPromotionOptions { EvaluationMatchCount = 1 },
            memory: null,
            loopOptions: new BonesAgentLoopOptions());

        // Act
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            agent.ExecuteAsync(
                fixture.Request(new BonesPlayerId(1), new BonesGameId("null-final-match"), evaluationSeed: 9001),
                fixture.Context(),
                CancellationToken.None).AsTask());

        // Assert
        Assert.Equal("The agent runtime completed without a final assistant message.", exception.Message);
        Assert.Equal(1, executor.CallCount);

        var artifacts = await fixture.ListArtifactsAsync();
        Assert.DoesNotContain(
            artifacts,
            descriptor => descriptor.ArtifactId.Value == "bones-strategy-initial-seat-1-v2");

        var directException = Assert.Throws<InvalidOperationException>(() =>
            BonesEnhancePrompts.ReadCompletedMarkdown(new AgentExecutionResult(
                AgentExecutionStatus.Completed,
                FinalMessage: null,
                Array.Empty<AgentStep>(),
                Usage: null,
                Error: null)));
        Assert.Equal("The agent runtime completed without a final assistant message.", directException.Message);
    }

    [Fact]
    public void CreateExecutionRequest_GivenSystemUserAssistantMessagesAndMemory_ExpectedMappedRuntimeRequest()
    {
        // Arrange
        var messages = new BonesStrategyEnhancementMessage[]
        {
            new("system", "SYSTEM PROMPT"),
            new("user", "FIRST USER"),
            new("assistant", "PRIOR ASSISTANT"),
            new("user", "FINAL TASK"),
        };
        var memory = new[] { ChatMessage.User("MEMORY MESSAGE") };
        var loopOptions = new AgentLoopOptions
        {
            MaxSteps = 7,
            MaxTotalTokens = 2048,
            Timeout = TimeSpan.FromSeconds(45),
            MaxConsecutiveToolFailures = 4,
        };

        // Act
        var request = BonesEnhancePrompts.CreateExecutionRequest(
            messages,
            "bones-strategy-enhance-test",
            new SessionId("prompts-unit-wp3"),
            memory,
            loopOptions);

        // Assert
        Assert.Equal(new SessionId("prompts-unit-wp3"), request.SessionId);
        Assert.Equal("bones-strategy-enhance-test", request.ModelId);
        Assert.Equal("SYSTEM PROMPT", request.SystemPrompt);
        Assert.Equal("FINAL TASK", request.Task);
        Assert.Equal(3, request.InitialMessages.Count);
        Assert.Equal(ChatRole.User, request.InitialMessages[0].Role);
        Assert.Equal("MEMORY MESSAGE", request.InitialMessages[0].Content);
        Assert.Equal(ChatRole.User, request.InitialMessages[1].Role);
        Assert.Equal("FIRST USER", request.InitialMessages[1].Content);
        Assert.Equal(ChatRole.Assistant, request.InitialMessages[2].Role);
        Assert.Equal("PRIOR ASSISTANT", request.InitialMessages[2].Content);

        var tool = Assert.Single(request.Tools);
        Assert.Equal("tool.bones.strategy-compile", tool.Name);

        Assert.Equal(7, request.Options.MaxSteps);
        Assert.Equal(2048, request.Options.MaxTotalTokens);
        Assert.Equal(TimeSpan.FromSeconds(45), request.Options.Timeout);
        Assert.Equal(4, request.Options.MaxConsecutiveToolFailures);
    }

    [Fact]
    public void CreateExecutionRequest_GivenUnknownRole_ExpectedInvalidOperationException()
    {
        var messages = new BonesStrategyEnhancementMessage[]
        {
            new("system", "SYSTEM PROMPT"),
            new("tool", "UNSUPPORTED MESSAGE"),
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            BonesEnhancePrompts.CreateExecutionRequest(
                messages,
                "bones-strategy-enhance-test",
                new SessionId("unknown-role-wp3"),
                memoryMessages: null,
                new AgentLoopOptions()));

        Assert.Equal("Unsupported Bones enhance prompt role 'tool'.", exception.Message);
    }

    private static BonesStrategyId PriorStrategyId(int seat)
        => new($"initial-seat-{seat}-v1");

    private static string CreateMatchHistoryMarkdown(BonesPlayerId playerId, BonesGameId gameId)
        => $"""
            # Bones Match History

            Game: {gameId.Value}
            Player seat: {playerId.Seat}
            Match winner seat: 3
            Final score for seat {playerId.Seat}: 6
            Final score for seat 3: 14
            """;

    private static IReadOnlyList<PromptLine> ToPromptLines(
        IReadOnlyList<BonesStrategyEnhancementMessage> messages)
        => messages.Select(message => new PromptLine(message.Role, message.Content)).ToArray();

    private static IReadOnlyList<PromptLine> ToPromptLines(IReadOnlyList<ChatMessage> messages)
        => messages
            .Select(message => new PromptLine(
                message.Role.ToString().ToLowerInvariant(),
                message.Content))
            .ToArray();

    private sealed record PromptLine(string Role, string Content);

    private sealed record EnhanceParitySnapshot(
        string PriorStrategyId,
        string StrategyId,
        string StrategyDocumentMarkdown,
        int StrategyDocumentPlayerSeat,
        string StrategyArtifactId,
        string StrategyArtifactKind,
        string StrategyArtifactProducerType,
        string StrategyArtifactProducerVersion,
        string StrategyArtifactRelativePath,
        string PersistedStrategySource,
        string PersistedStrategyKind,
        string PersistedStrategyPromotionStatus,
        int MatchHistoriesLoaded,
        string PromotionOutcome,
        string ActiveStrategyId,
        int StagnationDetected,
        int EnhanceIterationsCompleted,
        string PromotionDecisionArtifactJson,
        string ExecutionArtifactJson,
        IReadOnlyList<PromptLine> Prompts)
    {
        public static EnhanceParitySnapshot Create(
            BonesEnhanceStrategyResult result,
            BonesStrategyArtifact persistedStrategy,
            string promotionDecisionArtifactJson,
            string executionArtifactJson,
            IReadOnlyList<PromptLine> prompts)
            => new(
                result.PriorStrategyId.Value,
                result.StrategyId.Value,
                result.StrategyDocument.Markdown,
                result.StrategyDocument.PlayerId.Seat,
                result.StrategyArtifact.ArtifactId.Value,
                result.StrategyArtifact.Kind.ToString(),
                result.StrategyArtifact.ProducerType,
                result.StrategyArtifact.ProducerVersion,
                result.StrategyArtifact.RelativePath,
                persistedStrategy.Source,
                persistedStrategy.Kind.ToString(),
                persistedStrategy.PromotionStatus.ToString(),
                result.MatchHistoriesLoaded,
                result.PromotionOutcome.ToString(),
                result.ActiveStrategyId.Value,
                result.StagnationDetected,
                result.EnhanceIterationsCompleted,
                promotionDecisionArtifactJson,
                executionArtifactJson,
                prompts);

        public static IReadOnlyList<string> FindMismatches(
            EnhanceParitySnapshot expected,
            EnhanceParitySnapshot actual)
        {
            var mismatches = new List<string>();

            void Check(string field, string expectedValue, string actualValue)
            {
                if (!string.Equals(expectedValue, actualValue, StringComparison.Ordinal))
                {
                    mismatches.Add($"{field}: expected '{expectedValue}' but was '{actualValue}'.");
                }
            }

            Check("PriorStrategyId", expected.PriorStrategyId, actual.PriorStrategyId);
            Check("StrategyId", expected.StrategyId, actual.StrategyId);
            Check(
                "StrategyDocumentMarkdown",
                expected.StrategyDocumentMarkdown,
                actual.StrategyDocumentMarkdown);
            Check(
                "StrategyDocumentPlayerSeat",
                expected.StrategyDocumentPlayerSeat.ToString(),
                actual.StrategyDocumentPlayerSeat.ToString());
            Check("StrategyArtifactId", expected.StrategyArtifactId, actual.StrategyArtifactId);
            Check("StrategyArtifactKind", expected.StrategyArtifactKind, actual.StrategyArtifactKind);
            Check(
                "StrategyArtifactProducerType",
                expected.StrategyArtifactProducerType,
                actual.StrategyArtifactProducerType);
            Check(
                "StrategyArtifactProducerVersion",
                expected.StrategyArtifactProducerVersion,
                actual.StrategyArtifactProducerVersion);
            Check(
                "StrategyArtifactRelativePath",
                expected.StrategyArtifactRelativePath,
                actual.StrategyArtifactRelativePath);
            Check("PersistedStrategySource", expected.PersistedStrategySource, actual.PersistedStrategySource);
            Check("PersistedStrategyKind", expected.PersistedStrategyKind, actual.PersistedStrategyKind);
            Check(
                "PersistedStrategyPromotionStatus",
                expected.PersistedStrategyPromotionStatus,
                actual.PersistedStrategyPromotionStatus);
            Check(
                "MatchHistoriesLoaded",
                expected.MatchHistoriesLoaded.ToString(),
                actual.MatchHistoriesLoaded.ToString());
            Check("PromotionOutcome", expected.PromotionOutcome, actual.PromotionOutcome);
            Check("ActiveStrategyId", expected.ActiveStrategyId, actual.ActiveStrategyId);
            Check(
                "StagnationDetected",
                expected.StagnationDetected.ToString(),
                actual.StagnationDetected.ToString());
            Check(
                "EnhanceIterationsCompleted",
                expected.EnhanceIterationsCompleted.ToString(),
                actual.EnhanceIterationsCompleted.ToString());
            Check(
                "PromotionDecisionArtifactJson",
                expected.PromotionDecisionArtifactJson,
                actual.PromotionDecisionArtifactJson);
            Check("ExecutionArtifactJson", expected.ExecutionArtifactJson, actual.ExecutionArtifactJson);

            if (expected.Prompts.Count != actual.Prompts.Count)
            {
                mismatches.Add(
                    $"Prompts.Count: expected '{expected.Prompts.Count}' but was '{actual.Prompts.Count}'.");
            }
            else
            {
                for (var index = 0; index < expected.Prompts.Count; index++)
                {
                    Check(
                        $"Prompts[{index}].Role",
                        expected.Prompts[index].Role,
                        actual.Prompts[index].Role);
                    Check(
                        $"Prompts[{index}].Content",
                        expected.Prompts[index].Content,
                        actual.Prompts[index].Content);
                }
            }

            return mismatches;
        }
    }

    private sealed class EnhanceFixture : IAsyncDisposable
    {
        private EnhanceFixture(
            string repositoryPath,
            SessionId sessionId,
            WipArtifactStoreLocal store,
            BonesPlayerKnowledgeStore knowledgeStore)
        {
            RepositoryPath = repositoryPath;
            SessionId = sessionId;
            Store = store;
            KnowledgeStore = knowledgeStore;
        }

        public string RepositoryPath { get; }

        public SessionId SessionId { get; }

        public WipArtifactStoreLocal Store { get; }

        public BonesPlayerKnowledgeStore KnowledgeStore { get; }

        public static Task<EnhanceFixture> CreateAsync(SessionId sessionId)
        {
            var repositoryPath = Path.Combine(
                Path.GetTempPath(),
                $"modus-bones-wp3-{Guid.NewGuid():N}");
            Directory.CreateDirectory(repositoryPath);

            var store = new WipArtifactStoreLocal(repositoryPath);
            var knowledgeStore = new BonesPlayerKnowledgeStore(store, repositoryPath, sessionId);
            return Task.FromResult(new EnhanceFixture(repositoryPath, sessionId, store, knowledgeStore));
        }

        public BonesEnhanceStrategyRequest Request(
            BonesPlayerId playerId,
            BonesGameId? matchGameId = null,
            int? evaluationSeed = null)
            => new(playerId, RepositoryPath, matchGameId, EvaluationSeed: evaluationSeed);

        public CapabilityContext Context()
            => new(SessionId, Path.Combine(RepositoryPath, ".wip", "worktrees", "bones-enhance"));

        public async Task SaveActiveStrategyAsync(
            BonesPlayerId playerId,
            BonesStrategyId strategyId,
            string source)
        {
            await KnowledgeStore.SaveStrategyArtifact(
                playerId,
                new BonesStrategyArtifact(
                    strategyId,
                    playerId,
                    BonesStrategyKind.Script,
                    source,
                    BonesPromotionStatus.Active),
                CancellationToken.None);
        }

        public async Task SaveAllSeatStrategiesAsync()
        {
            for (var seat = BonesPlayerId.MinSeat; seat <= BonesPlayerId.MaxSeat; seat++)
            {
                var playerId = new BonesPlayerId(seat);
                await SaveActiveStrategyAsync(
                    playerId,
                    PriorStrategyId(seat),
                    FirstLegalMoveScriptSource);
            }
        }

        public async Task SaveMatchHistoryAsync(BonesPlayerId playerId, BonesGameId gameId)
        {
            await KnowledgeStore.SaveMatchHistory(
                playerId,
                new BonesMatchHistoryTranscript(
                    gameId,
                    playerId,
                    CreateMatchHistoryMarkdown(playerId, gameId)),
                CancellationToken.None);
        }

        public ValueTask<BonesStrategyArtifact> LoadStrategyAsync(
            BonesPlayerId playerId,
            BonesStrategyId strategyId)
            => KnowledgeStore.LoadStrategyArtifact(playerId, strategyId, CancellationToken.None);

        public async Task<string> ReadArtifactAsync(ArtifactDescriptor descriptor)
        {
            var artifactPath = Path.Combine(
                RepositoryPath,
                descriptor.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            return await File.ReadAllTextAsync(artifactPath);
        }

        public async Task<IReadOnlyList<ArtifactDescriptor>> ListArtifactsAsync()
            => await Store.ListAsync(SessionId, CancellationToken.None);

        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(RepositoryPath))
                Directory.Delete(RepositoryPath, recursive: true);

            return ValueTask.CompletedTask;
        }
    }

    private sealed class RuntimeTooling : IDisposable
    {
        private readonly ServiceProvider _serviceProvider;

        public RuntimeTooling(BonesStrategyScriptHost scriptHost)
        {
            var services = new ServiceCollection();
            services.AddSingleton(scriptHost);
            services.AddSingleton(new BonesStrategyCompileTool(scriptHost));
            _serviceProvider = services.BuildServiceProvider();
            ToolDescriptors = BonesAgentToolDescriptors.Create();
        }

        public IReadOnlyList<ICapabilityDescriptor> ToolDescriptors { get; }

        public AgentExecutor CreateExecutor(IChatModel chatModel)
            => new(
                chatModel,
                new RuntimeToolInvoker(
                    _serviceProvider,
                    ToolDescriptors,
                    new ToolInvocationOptions { RecordArtifacts = false }));

        public void Dispose() => _serviceProvider.Dispose();
    }

    private sealed class SingleAnswerEnhancementModelProvider
        : IModelProvider<BonesStrategyEnhancementRequest, BonesStrategyEnhancementResult>
    {
        private readonly string _markdown;

        public SingleAnswerEnhancementModelProvider(string markdown)
        {
            _markdown = markdown;
        }

        public List<ModelProviderRequest<BonesStrategyEnhancementRequest>> CapturedRequests { get; } = [];

        public ValueTask<ModelProviderResponse<BonesStrategyEnhancementResult>> ExecuteAsync(
            ModelProviderRequest<BonesStrategyEnhancementRequest> request,
            CapabilityContext context,
            CancellationToken cancellationToken)
        {
            CapturedRequests.Add(request);
            return ValueTask.FromResult(
                new ModelProviderResponse<BonesStrategyEnhancementResult>(
                    payload: new BonesStrategyEnhancementResult(_markdown),
                    providerId: "single-answer-enhance-provider",
                    modelId: request.ModelId,
                    usage: new ModelProviderUsage(140, 60),
                    correlationId: request.CorrelationId));
        }
    }

    private sealed class SingleAnswerChatModel : IChatModel
    {
        private readonly string _markdown;

        public SingleAnswerChatModel(string markdown)
        {
            _markdown = markdown;
        }

        public List<ChatRequest> CapturedRequests { get; } = [];

        public ValueTask<ChatResponse> CompleteAsync(
            ChatRequest request,
            CapabilityContext context,
            CancellationToken cancellationToken)
        {
            CapturedRequests.Add(request);
            return ValueTask.FromResult(new ChatResponse(
                ChatMessage.Assistant(_markdown),
                request.ModelId,
                providerId: "single-answer-chat-model"));
        }

        public IAsyncEnumerable<StreamingChunk> StreamAsync(
            ChatRequest request,
            CapabilityContext context,
            CancellationToken cancellationToken)
            => throw new NotSupportedException("Streaming is not used by the runtime Enhance tests.");
    }

    private sealed class DiagnosticsThenFinalChatModel : IChatModel
    {
        private readonly string _invalidScriptSource;
        private readonly string _finalMarkdown;

        public DiagnosticsThenFinalChatModel(string invalidScriptSource, string finalMarkdown)
        {
            _invalidScriptSource = invalidScriptSource;
            _finalMarkdown = finalMarkdown;
        }

        public List<ChatRequest> CapturedRequests { get; } = [];

        public ValueTask<ChatResponse> CompleteAsync(
            ChatRequest request,
            CapabilityContext context,
            CancellationToken cancellationToken)
        {
            CapturedRequests.Add(request);

            if (CapturedRequests.Count == 1)
            {
                var call = new ToolCall(
                    "call-compile-invalid-1",
                    BonesAgentToolIds.StrategyCompile.Value,
                    JsonSerializer.SerializeToElement(new { scriptSource = _invalidScriptSource }));
                return ValueTask.FromResult(new ChatResponse(
                    ChatMessage.Assistant(string.Empty, new[] { call }),
                    request.ModelId,
                    providerId: "diagnostics-chat-model"));
            }

            return ValueTask.FromResult(new ChatResponse(
                ChatMessage.Assistant(_finalMarkdown),
                request.ModelId,
                providerId: "diagnostics-chat-model"));
        }

        public IAsyncEnumerable<StreamingChunk> StreamAsync(
            ChatRequest request,
            CapabilityContext context,
            CancellationToken cancellationToken)
            => throw new NotSupportedException("Streaming is not used by the runtime Enhance tests.");
    }

    private sealed class AlwaysToolCallChatModel : IChatModel
    {
        private readonly string _toolName;
        private readonly JsonElement _arguments;

        public AlwaysToolCallChatModel(string toolName, JsonElement arguments)
        {
            _toolName = toolName;
            _arguments = arguments;
        }

        public int CallCount { get; private set; }

        public ValueTask<ChatResponse> CompleteAsync(
            ChatRequest request,
            CapabilityContext context,
            CancellationToken cancellationToken)
        {
            CallCount++;
            var call = new ToolCall($"call-{CallCount}", _toolName, _arguments);
            return ValueTask.FromResult(new ChatResponse(
                ChatMessage.Assistant(string.Empty, new[] { call }),
                request.ModelId,
                providerId: "always-tool-chat-model"));
        }

        public IAsyncEnumerable<StreamingChunk> StreamAsync(
            ChatRequest request,
            CapabilityContext context,
            CancellationToken cancellationToken)
            => throw new NotSupportedException("Streaming is not used by the runtime Enhance tests.");
    }

    private sealed class NullFinalMessageExecutor : IAgentExecutor
    {
        public int CallCount { get; private set; }

        public ValueTask<AgentExecutionResult> ExecuteAsync(
            AgentExecutionRequest request,
            CapabilityContext context,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return ValueTask.FromResult(new AgentExecutionResult(
                AgentExecutionStatus.Completed,
                FinalMessage: null,
                Array.Empty<AgentStep>(),
                Usage: null,
                Error: null));
        }
    }
}
