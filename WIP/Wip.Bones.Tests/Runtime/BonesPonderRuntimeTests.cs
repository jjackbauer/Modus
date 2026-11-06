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
using Wip.Bones.Agents.Knowledge;
using Wip.Bones.Agents.Ponder;
using Wip.Bones.Agents.Runtime;
using Wip.Bones.Domain;
using Wip.Bones.Engine;
using Wip.Bones.Identifiers;
using Wip.Runtime.Runtime;
using Xunit;

namespace Wip.Bones.Tests.Runtime;

public sealed class BonesPonderRuntimeTests
{
    private const string ValidScriptSource = """
        using Wip.Bones.Domain;
        using Wip.Bones.Engine;

        public sealed class SeatStrategy : IBonesPlayerSlot
        {
            public BonesMove ChooseMove(BonesRoundState state, IReadOnlyList<BonesMove> legalMoves)
            {
                return legalMoves[0];
            }
        }
        """;

    private const string InvalidScriptSource = """
        using Wip.Bones.Domain;
        using Wip.Bones.Engine;

        public sealed class SeatStrategy : IBonesPlayerSlot
        {
            public BonesMove ChooseMove(BonesRoundState state, IReadOnlyList<BonesMove> legalMoves)
            {
                return legalMoves[0];
            }
        """;

    private const string ValidMarkdown = $"```csharp\n{ValidScriptSource}\n```";

    private const string ObservationMarkdown =
        "# Observation\nSeat 2 watched open ends shift from 6-4 to 5-4 after a Left play.";

    [Fact]
    public async Task ExecuteAsync_GivenSameInputThroughLegacyAndRuntimePaths_ExpectedDeterministicParity()
    {
        // Arrange
        var sessionId = new SessionId("parity-session-wp2");
        var playerId = new BonesPlayerId(2);

        await using var legacyFixture = await PonderFixture.CreateAsync(sessionId);
        await using var runtimeFixture = await PonderFixture.CreateAsync(sessionId);

        await legacyFixture.SaveObservationAsync(playerId, new BonesGameId("observe-parity"), ObservationMarkdown);
        await runtimeFixture.SaveObservationAsync(playerId, new BonesGameId("observe-parity"), ObservationMarkdown);

        var legacyProvider = new SingleAnswerModelProvider(ValidMarkdown);
        var legacyAgent = new BonesPonderAgent(legacyFixture.Store, legacyProvider);

        var scriptHost = new BonesStrategyScriptHost();
        using var tooling = new RuntimeTooling(scriptHost);
        var runtimeModel = new ToolRouteChatModel(ValidMarkdown, ValidScriptSource);
        var runtimeAgent = new BonesPonderAgent(
            runtimeFixture.Store,
            tooling.CreateExecutor(runtimeModel),
            scriptHost,
            new BonesCompileRetryOptions(),
            memory: null,
            new BonesAgentLoopOptions());

        // Act
        var legacyResult = await legacyAgent.ExecuteAsync(
            legacyFixture.Request(playerId),
            legacyFixture.Context(),
            CancellationToken.None);
        var runtimeResult = await runtimeAgent.ExecuteAsync(
            runtimeFixture.Request(playerId),
            runtimeFixture.Context(),
            CancellationToken.None);

        // Assert — domain result parity
        Assert.Equal(new BonesStrategyId("initial-seat-2-v1"), legacyResult.StrategyId);
        Assert.Equal(new BonesStrategyId("initial-seat-2-v1"), runtimeResult.StrategyId);
        Assert.Equal(legacyResult.StrategyId, runtimeResult.StrategyId);
        Assert.Equal(legacyResult.StrategyDocument, runtimeResult.StrategyDocument);
        Assert.Equal(legacyResult.AuthoredStrategy, runtimeResult.AuthoredStrategy);
        Assert.Equal(legacyResult.ObservationsLoaded, runtimeResult.ObservationsLoaded);

        Assert.Equal(1, runtimeResult.ObservationsLoaded);
        Assert.Equal(BonesStrategyKind.Script, runtimeResult.AuthoredStrategy.Kind);
        Assert.Equal(BonesPromotionStatus.Active, runtimeResult.AuthoredStrategy.PromotionStatus);
        Assert.Equal(ValidScriptSource, runtimeResult.StrategyDocument.Markdown);
        Assert.Equal(ValidScriptSource, runtimeResult.AuthoredStrategy.Source);

        // Assert — persisted strategy artifact descriptor parity (deterministic fields only)
        Assert.Equal(legacyResult.StrategyArtifact.ArtifactId, runtimeResult.StrategyArtifact.ArtifactId);
        Assert.Equal(legacyResult.StrategyArtifact.Kind, runtimeResult.StrategyArtifact.Kind);
        Assert.Equal(legacyResult.StrategyArtifact.ProducerType, runtimeResult.StrategyArtifact.ProducerType);
        Assert.Equal(legacyResult.StrategyArtifact.ProducerVersion, runtimeResult.StrategyArtifact.ProducerVersion);
        Assert.Equal(legacyResult.StrategyArtifact.RelativePath, runtimeResult.StrategyArtifact.RelativePath);

        Assert.Equal("bones-strategy-initial-seat-2-v1", runtimeResult.StrategyArtifact.ArtifactId.Value);
        Assert.Equal(ArtifactKind.Json, runtimeResult.StrategyArtifact.Kind);
        Assert.Equal("Wip.Bones.Agents.BonesPlayerKnowledgeStore", runtimeResult.StrategyArtifact.ProducerType);
        Assert.Equal("1.0.0", runtimeResult.StrategyArtifact.ProducerVersion);
        Assert.StartsWith(
            ".wip/sessions/parity-session-wp2/artifacts/",
            runtimeResult.StrategyArtifact.RelativePath,
            StringComparison.Ordinal);
        Assert.Contains(
            "bones-player-2-strategy-initial-seat-2-v1",
            runtimeResult.StrategyArtifact.RelativePath,
            StringComparison.Ordinal);

        // Assert — execution artifact JSON parity (GUID artifact ids and ProducedAtUtc excluded)
        var legacyExecutionJson = await legacyFixture.ReadArtifactAsync(legacyResult.ExecutionArtifact);
        var runtimeExecutionJson = await runtimeFixture.ReadArtifactAsync(runtimeResult.ExecutionArtifact);
        Assert.Equal(legacyExecutionJson, runtimeExecutionJson);

        using (var execution = JsonDocument.Parse(runtimeExecutionJson))
        {
            Assert.Equal(2, execution.RootElement.GetProperty("PlayerSeat").GetInt32());
            Assert.Equal("initial-seat-2-v1", execution.RootElement.GetProperty("StrategyId").GetString());
            Assert.Equal("bones-strategy-author", execution.RootElement.GetProperty("ModelId").GetString());
            Assert.Equal(1, execution.RootElement.GetProperty("ObservationsLoaded").GetInt32());
            Assert.Equal(JsonValueKind.Null, execution.RootElement.GetProperty("BootstrapSource").ValueKind);
            Assert.Equal(1, execution.RootElement.GetProperty("CompileAttempts").GetInt32());
            Assert.True(execution.RootElement.GetProperty("FinalCompileSucceeded").GetBoolean());
            Assert.Equal(JsonValueKind.Null, execution.RootElement.GetProperty("LastCompileFailureReason").ValueKind);
        }

        // Assert — first-turn prompt parity (system + user); the runtime turn-2 prompt adds the
        // tool observation, which is asserted separately below.
        var legacyPrompt = Assert.Single(legacyProvider.CapturedRequests).Payload.Messages;
        Assert.Equal(2, runtimeModel.CapturedRequests.Count);
        var runtimePrompt = runtimeModel.CapturedRequests[0].Messages;
        var legacyPromptLines = ToPromptLines(legacyPrompt);
        var runtimePromptLines = ToPromptLines(runtimePrompt);

        Assert.Equal(2, runtimePromptLines.Count);
        Assert.Equal("system", runtimePromptLines[0].Role);
        Assert.Equal("user", runtimePromptLines[1].Role);
        Assert.Equal(legacyPromptLines[0].Content, runtimePromptLines[0].Content);
        Assert.Equal(legacyPromptLines[1].Content, runtimePromptLines[1].Content);
        Assert.Contains("Player seat: 2", runtimePromptLines[1].Content, StringComparison.Ordinal);
        Assert.Contains(ObservationMarkdown, runtimePromptLines[1].Content, StringComparison.Ordinal);

        // Assert — the runtime path carries the compile tool definition and the first tool
        // observation is a successful compile.
        var sentTool = Assert.Single(runtimeModel.CapturedRequests[0].Tools);
        Assert.Equal("tool.bones.strategy-compile", sentTool.Name);
        Assert.Equal("object", sentTool.ParametersSchema.GetProperty("type").GetString());

        Assert.Equal(2, runtimeModel.CapturedRequests.Count);
        var toolObservation = Assert.Single(runtimeModel.ToolObservationContents);
        using (var observation = JsonDocument.Parse(toolObservation))
        {
            Assert.True(observation.RootElement.GetProperty("succeeded").GetBoolean());
            Assert.Equal(JsonValueKind.Null, observation.RootElement.GetProperty("failureReason").ValueKind);
        }

        // Assert — the shared comparison helper reports no mismatch for the deterministic snapshot.
        var legacySnapshot = PonderParitySnapshot.Create(
            legacyResult,
            legacyExecutionJson,
            legacyPromptLines);
        var runtimeSnapshot = PonderParitySnapshot.Create(
            runtimeResult,
            runtimeExecutionJson,
            runtimePromptLines);
        Assert.Empty(PonderParitySnapshot.FindMismatches(legacySnapshot, runtimeSnapshot));
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

        var expected = new PonderParitySnapshot(
            "initial-seat-2-v1",
            "strategy markdown",
            "strategy source",
            "Script",
            "Active",
            1,
            "bones-strategy-initial-seat-2-v1",
            "Json",
            "Wip.Bones.Agents.BonesPlayerKnowledgeStore",
            "1.0.0",
            "bones-player-2-strategy-initial-seat-2-v1",
            "{\"PlayerSeat\":2}",
            prompts);

        var identical = expected with
        {
            Prompts = new[] { prompts[0], prompts[1] },
        };

        // Act / Assert — the helper is quiet for identical snapshots.
        Assert.Empty(PonderParitySnapshot.FindMismatches(expected, identical));

        // Act / Assert — a one-character prompt change is reported.
        var alteredPrompt = identical with
        {
            Prompts = new[] { prompts[0], new PromptLine("user", "user prompt altered") },
        };
        var promptMismatch = Assert.Single(PonderParitySnapshot.FindMismatches(expected, alteredPrompt));
        Assert.Contains("Prompts[1].Content", promptMismatch, StringComparison.Ordinal);
        Assert.Contains("user prompt altered", promptMismatch, StringComparison.Ordinal);

        // Act / Assert — an altered result field is reported too.
        var alteredResult = identical with { ExecutionArtifactJson = "{\"PlayerSeat\":3}" };
        var resultMismatch = Assert.Single(PonderParitySnapshot.FindMismatches(expected, alteredResult));
        Assert.Contains("ExecutionArtifactJson", resultMismatch, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_GivenInvalidScriptThenCompileToolThenValidScript_ExpectedDiagnosticsInToolMessageAndStrategyPersisted()
    {
        // Arrange
        await using var fixture = await PonderFixture.CreateAsync(new SessionId("tool-routing-wp2"));
        var playerId = new BonesPlayerId(1);

        var scriptHost = new BonesStrategyScriptHost();
        using var tooling = new RuntimeTooling(scriptHost);
        var model = new DiagnosticsThenFinalChatModel(ValidScriptSource, InvalidScriptSource, ValidMarkdown);
        var agent = new BonesPonderAgent(
            fixture.Store,
            tooling.CreateExecutor(model),
            scriptHost,
            new BonesCompileRetryOptions(),
            memory: null,
            new BonesAgentLoopOptions());

        // Act
        var result = await agent.ExecuteAsync(
            fixture.Request(playerId),
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
            Assert.Contains("expected", failureReason!, StringComparison.OrdinalIgnoreCase);
        }

        // Assert — the valid script from turn 2 is parsed, compiled and persisted.
        Assert.Equal(new BonesStrategyId("initial-seat-1-v1"), result.StrategyId);
        Assert.Equal(BonesStrategyKind.Script, result.AuthoredStrategy.Kind);
        Assert.Equal(ValidScriptSource, result.StrategyDocument.Markdown);

        var artifacts = await fixture.ListArtifactsAsync();
        Assert.Contains(
            artifacts,
            descriptor => descriptor.ArtifactId.Value == "bones-strategy-initial-seat-1-v1");

        var loadedStrategy = await fixture.KnowledgeStore.LoadStrategyArtifact(
            playerId,
            new BonesStrategyId("initial-seat-1-v1"),
            CancellationToken.None);
        Assert.Equal(ValidScriptSource, loadedStrategy.Source);
        Assert.Equal(BonesPromotionStatus.Active, loadedStrategy.PromotionStatus);
    }

    [Fact]
    public async Task ExecuteAsync_GivenModelAlwaysCallsCompileTool_ExpectedMaxStepsReachedWithoutStrategyArtifact()
    {
        // Arrange
        await using var fixture = await PonderFixture.CreateAsync(new SessionId("max-steps-wp2"));

        var scriptHost = new BonesStrategyScriptHost();
        using var tooling = new RuntimeTooling(scriptHost);
        var model = new AlwaysToolCallChatModel(
            BonesAgentToolIds.StrategyCompile.Value,
            JsonSerializer.SerializeToElement(new { scriptSource = ValidScriptSource }));
        var agent = new BonesPonderAgent(
            fixture.Store,
            tooling.CreateExecutor(model),
            scriptHost,
            new BonesCompileRetryOptions(),
            memory: null,
            new BonesAgentLoopOptions { MaxSteps = 2 });

        // Act
        var exception = await Assert.ThrowsAsync<BonesAgentExecutionException>(() =>
            agent.ExecuteAsync(
                fixture.Request(new BonesPlayerId(3)),
                fixture.Context(),
                CancellationToken.None).AsTask());

        // Assert
        Assert.Equal(AgentExecutionStatus.MaxStepsReached, exception.Status);
        Assert.Equal(2, exception.Steps.Count);
        Assert.All(exception.Steps, step => Assert.Single(step.ToolResults));
        Assert.Contains("Max steps (2) reached without a final answer.", exception.Message, StringComparison.Ordinal);
        Assert.Equal(2, model.CallCount);

        var artifacts = await fixture.ListArtifactsAsync();
        Assert.DoesNotContain(
            artifacts,
            descriptor => descriptor.ArtifactId.Value.StartsWith("bones-strategy-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_GivenModelAlwaysCallsUnregisteredTool_ExpectedToolFailureLimitReached()
    {
        // Arrange
        await using var fixture = await PonderFixture.CreateAsync(new SessionId("tool-failure-wp2"));

        var scriptHost = new BonesStrategyScriptHost();
        using var tooling = new RuntimeTooling(scriptHost);
        var model = new AlwaysToolCallChatModel(
            "tool.bones.unregistered",
            JsonSerializer.SerializeToElement(new { value = "ignored" }));
        var agent = new BonesPonderAgent(
            fixture.Store,
            tooling.CreateExecutor(model),
            scriptHost,
            new BonesCompileRetryOptions(),
            memory: null,
            new BonesAgentLoopOptions { MaxConsecutiveToolFailures = 3 });

        // Act
        var exception = await Assert.ThrowsAsync<BonesAgentExecutionException>(() =>
            agent.ExecuteAsync(
                fixture.Request(new BonesPlayerId(4)),
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
            descriptor => descriptor.ArtifactId.Value.StartsWith("bones-strategy-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_GivenMemorySnapshot_ExpectedMemoryMessageBeforeCurrentConversationAndExchangeAppended()
    {
        // Arrange
        await using var fixture = await PonderFixture.CreateAsync(new SessionId("memory-wp2"));
        var playerId = new BonesPlayerId(2);
        var memory = new ArtifactAgentMemory(fixture.Store);
        const string memoryMessage = "Earlier ponder turn: seat 2 preferred ends 6-4.";
        await memory.AppendAsync(
            fixture.SessionId,
            ChatMessage.User(memoryMessage),
            CancellationToken.None);

        var scriptHost = new BonesStrategyScriptHost();
        using var tooling = new RuntimeTooling(scriptHost);
        var model = new SingleAnswerChatModel(ValidMarkdown);
        var agent = new BonesPonderAgent(
            fixture.Store,
            tooling.CreateExecutor(model),
            scriptHost,
            new BonesCompileRetryOptions(),
            memory,
            new BonesAgentLoopOptions());

        // Act
        await agent.ExecuteAsync(
            fixture.Request(playerId),
            fixture.Context(),
            CancellationToken.None);

        // Assert — first captured request: system, memory message, current user prompt.
        var firstRequest = Assert.Single(model.CapturedRequests);
        Assert.Equal(3, firstRequest.Messages.Count);
        Assert.Equal(ChatRole.System, firstRequest.Messages[0].Role);
        Assert.Equal(ChatRole.User, firstRequest.Messages[1].Role);
        Assert.Equal(memoryMessage, firstRequest.Messages[1].Content);
        Assert.Equal(ChatRole.User, firstRequest.Messages[2].Role);
        Assert.Contains("# Strategy script authoring request", firstRequest.Messages[2].Content, StringComparison.Ordinal);
        Assert.Contains("Player seat: 2", firstRequest.Messages[2].Content, StringComparison.Ordinal);

        // Assert — the memory artifact after the run holds the pre-seeded, new user and new
        // assistant messages in order.
        var snapshot = await memory.LoadAsync(fixture.SessionId, CancellationToken.None);
        Assert.Equal(3, snapshot.Messages.Count);
        Assert.Equal(memoryMessage, snapshot.Messages[0].Content);
        Assert.Equal(ChatRole.User, snapshot.Messages[1].Role);
        Assert.Contains("# Strategy script authoring request", snapshot.Messages[1].Content, StringComparison.Ordinal);
        Assert.Equal(ChatRole.Assistant, snapshot.Messages[2].Role);
        Assert.Equal(ValidMarkdown, snapshot.Messages[2].Content);

        // Assert — the artifact is the local store's normalized agent-memory file.
        var artifacts = await fixture.ListArtifactsAsync();
        Assert.Contains(
            artifacts,
            descriptor => descriptor.ProducerType == "ArtifactAgentMemory"
                && descriptor.RelativePath.StartsWith(
                    ".wip/sessions/memory-wp2/artifacts/agent-memory-agent-memory-",
                    StringComparison.Ordinal)
                && descriptor.RelativePath.EndsWith(".json", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_GivenCoLearningAllSeatsAndEmptyLibrary_ExpectedFourSeatPromptsAndStrategiesWithFirstSeatResultReturned()
    {
        // Arrange
        await using var fixture = await PonderFixture.CreateAsync(new SessionId("co-learning-wp2"));
        var library = new BonesStrategyLibrary(Path.Combine(Path.GetTempPath(), $"bones-wp2-library-{Guid.NewGuid():N}"));

        var scriptHost = new BonesStrategyScriptHost();
        using var tooling = new RuntimeTooling(scriptHost);
        var model = new SingleAnswerChatModel(ValidMarkdown);
        var singleSeatAgent = new BonesPonderAgent(
            fixture.Store,
            tooling.CreateExecutor(model),
            scriptHost,
            new BonesCompileRetryOptions(),
            memory: null,
            new BonesAgentLoopOptions());
        var coLearningAgent = new BonesCoLearningPonderAgent(
            singleSeatAgent,
            strategyLibrary: library,
            artifactStore: fixture.Store);

        var request = new BonesPonderRequest(
            new BonesPlayerId(1),
            fixture.RepositoryPath,
            AllSeatsLearning: true);

        // Act
        var result = await coLearningAgent.ExecuteAsync(
            request,
            fixture.Context(),
            CancellationToken.None);

        // Assert — all four seats were prompted with their own seat number.
        Assert.Equal(4, model.CapturedRequests.Count);
        for (var seat = BonesPlayerId.MinSeat; seat <= BonesPlayerId.MaxSeat; seat++)
        {
            var seatRequest = Assert.Single(
                model.CapturedRequests,
                captured => captured.Messages.Any(message =>
                    message.Role == ChatRole.User
                    && message.Content.Contains($"Player seat: {seat}", StringComparison.Ordinal)));
            Assert.Contains(
                $"Observation count: 0",
                seatRequest.Messages[^1].Content,
                StringComparison.Ordinal);
        }

        // Assert — each seat owns its own persisted strategy artifact.
        var artifacts = await fixture.ListArtifactsAsync();
        for (var seat = BonesPlayerId.MinSeat; seat <= BonesPlayerId.MaxSeat; seat++)
        {
            var strategyId = new BonesStrategyId($"initial-seat-{seat}-v1");
            Assert.Contains(
                artifacts,
                descriptor => descriptor.ArtifactId.Value == $"bones-strategy-{strategyId.Value}");

            var loadedStrategy = await fixture.KnowledgeStore.LoadStrategyArtifact(
                new BonesPlayerId(seat),
                strategyId,
                CancellationToken.None);
            Assert.Equal(seat, loadedStrategy.PlayerId.Seat);
            Assert.Equal(ValidScriptSource, loadedStrategy.Source);
        }

        // Assert — the wrapper returns the first completed seat result (seat 1).
        Assert.Equal(new BonesStrategyId("initial-seat-1-v1"), result.StrategyId);
        Assert.Equal(1, result.StrategyDocument.PlayerId.Seat);
        Assert.Equal(1, result.AuthoredStrategy.PlayerId.Seat);
        Assert.Equal(ValidScriptSource, result.StrategyDocument.Markdown);
    }

    [Fact]
    public async Task ExecuteAsync_GivenCompletedExecutionWithoutFinalMessage_ExpectedInvalidOperationException()
    {
        // Arrange
        await using var fixture = await PonderFixture.CreateAsync(new SessionId("null-final-wp2"));
        var executor = new NullFinalMessageExecutor();
        var agent = new BonesPonderAgent(
            fixture.Store,
            executor,
            new BonesStrategyScriptHost(),
            new BonesCompileRetryOptions(),
            memory: null,
            new BonesAgentLoopOptions());

        // Act
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            agent.ExecuteAsync(
                fixture.Request(new BonesPlayerId(1)),
                fixture.Context(),
                CancellationToken.None).AsTask());

        // Assert
        Assert.Equal("The agent runtime completed without a final assistant message.", exception.Message);
        Assert.Equal(1, executor.CallCount);

        var artifacts = await fixture.ListArtifactsAsync();
        Assert.DoesNotContain(
            artifacts,
            descriptor => descriptor.ArtifactId.Value.StartsWith("bones-strategy-", StringComparison.Ordinal));
    }

    [Fact]
    public void ToAgentLoopOptions_GivenDefaults_ExpectedRuntimeBudgetValues()
    {
        var options = new BonesAgentLoopOptions();

        Assert.Equal(3, options.MaxSteps);
        Assert.Null(options.MaxTotalTokens);
        Assert.Equal(TimeSpan.FromMinutes(2), options.Timeout);
        Assert.Equal(2, options.MaxConsecutiveToolFailures);

        var runtime = options.ToAgentLoopOptions();
        Assert.Equal(3, runtime.MaxSteps);
        Assert.Null(runtime.MaxTotalTokens);
        Assert.Equal(TimeSpan.FromMinutes(2), runtime.Timeout);
        Assert.Equal(2, runtime.MaxConsecutiveToolFailures);
    }

    [Fact]
    public void ToAgentLoopOptions_GivenCustomValues_ExpectedExactMapping()
    {
        var options = new BonesAgentLoopOptions
        {
            MaxSteps = 7,
            MaxTotalTokens = 2048,
            Timeout = TimeSpan.FromSeconds(45),
            MaxConsecutiveToolFailures = 4,
        };

        var runtime = options.ToAgentLoopOptions();

        Assert.Equal(7, runtime.MaxSteps);
        Assert.Equal(2048, runtime.MaxTotalTokens);
        Assert.Equal(TimeSpan.FromSeconds(45), runtime.Timeout);
        Assert.Equal(4, runtime.MaxConsecutiveToolFailures);
    }

    [Fact]
    public void BonesAgentToolDefinitionsAndDescriptors_GivenDefaults_ExpectedCompileCapabilityContract()
    {
        // Tool definition contract.
        var definition = BonesAgentToolDefinitions.StrategyCompile;
        Assert.Equal("tool.bones.strategy-compile", definition.Name);
        Assert.Contains("compiles", definition.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("compiler diagnostics", definition.Description, StringComparison.OrdinalIgnoreCase);

        var schema = definition.ParametersSchema;
        Assert.Equal("object", schema.GetProperty("type").GetString());
        var scriptSource = schema.GetProperty("properties").GetProperty("scriptSource");
        Assert.Equal("string", scriptSource.GetProperty("type").GetString());
        Assert.False(string.IsNullOrWhiteSpace(scriptSource.GetProperty("description").GetString()));
        var required = Assert.Single(schema.GetProperty("required").EnumerateArray());
        Assert.Equal("scriptSource", required.GetString());
        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());

        // Descriptor contract.
        var descriptor = Assert.Single(BonesAgentToolDescriptors.Create());
        Assert.Equal("tool.bones.strategy-compile", descriptor.CapabilityId.Value);
        Assert.Equal(CapabilityKind.Tool, descriptor.Kind);
        Assert.Equal(typeof(BonesStrategyCompileTool), descriptor.CapabilityType);
        Assert.Equal(typeof(BonesStrategyCompileToolRequest), descriptor.RequestType);
        Assert.Equal(typeof(BonesStrategyCompileToolResult), descriptor.ResultType);
        Assert.Empty(descriptor.RequiredPermissions);
        Assert.Null(descriptor.PluginOrigin);

        // Optional permission/plugin metadata flows through for WP5.
        var permissions = new[] { new CapabilityPermission("tool.execute") };
        var origin = new PluginOrigin("bones", new CapabilityVersion("1.0.0"), "Wip.Bones.Agents");
        var configured = Assert.Single(BonesAgentToolDescriptors.Create(permissions, origin));
        Assert.Equal(permissions, configured.RequiredPermissions);
        Assert.Equal(origin, configured.PluginOrigin);
    }

    [Fact]
    public async Task ConstructorSelection_GivenOnlyModelProviderRegistered_ExpectedLegacyProviderPath()
    {
        // Arrange
        await using var fixture = await PonderFixture.CreateAsync(new SessionId("ctor-legacy-wp2"));
        var provider = new SingleAnswerModelProvider(ValidMarkdown);
        var services = new ServiceCollection();
        services.AddSingleton<IArtifactStore>(fixture.Store);
        services.AddSingleton<IModelProvider<BonesStrategyAuthoringRequest, BonesStrategyAuthoringResult>>(provider);
        services.AddSingleton<BonesPonderAgent>();

        using var serviceProvider = services.BuildServiceProvider();

        // Act
        var agent = serviceProvider.GetRequiredService<BonesPonderAgent>();
        var result = await agent.ExecuteAsync(
            fixture.Request(new BonesPlayerId(1)),
            fixture.Context(),
            CancellationToken.None);

        // Assert
        Assert.Equal(1, provider.CallCount);
        Assert.Equal(new BonesStrategyId("initial-seat-1-v1"), result.StrategyId);
    }

    [Fact]
    public async Task ConstructorSelection_GivenExecutorRegistered_ExpectedRuntimeExecutorPath()
    {
        // Arrange — production registers the executor instead of the legacy model provider.
        await using var fixture = await PonderFixture.CreateAsync(new SessionId("ctor-runtime-wp2"));
        var executor = new CompletedExecutor(ValidMarkdown);
        var services = new ServiceCollection();
        services.AddSingleton<IArtifactStore>(fixture.Store);
        services.AddSingleton<IAgentExecutor>(executor);
        services.AddSingleton<BonesPonderAgent>();

        using var serviceProvider = services.BuildServiceProvider();

        // Act
        var agent = serviceProvider.GetRequiredService<BonesPonderAgent>();
        var result = await agent.ExecuteAsync(
            fixture.Request(new BonesPlayerId(2)),
            fixture.Context(),
            CancellationToken.None);

        // Assert
        Assert.Equal(1, executor.CallCount);
        Assert.Equal(new BonesStrategyId("initial-seat-2-v1"), result.StrategyId);
    }

    private static IReadOnlyList<PromptLine> ToPromptLines(IReadOnlyList<BonesStrategyAuthoringMessage> messages)
        => messages.Select(message => new PromptLine(message.Role, message.Content)).ToArray();

    private static IReadOnlyList<PromptLine> ToPromptLines(IReadOnlyList<ChatMessage> messages)
        => messages.Select(message => new PromptLine(message.Role.ToString().ToLowerInvariant(), message.Content)).ToArray();

    private sealed record PromptLine(string Role, string Content);

    private sealed record PonderParitySnapshot(
        string StrategyId,
        string StrategyDocumentMarkdown,
        string AuthoredStrategySource,
        string AuthoredStrategyKind,
        string AuthoredStrategyPromotionStatus,
        int ObservationsLoaded,
        string StrategyArtifactId,
        string StrategyArtifactKind,
        string StrategyArtifactProducerType,
        string StrategyArtifactProducerVersion,
        string StrategyArtifactRelativePath,
        string ExecutionArtifactJson,
        IReadOnlyList<PromptLine> Prompts)
    {
        public static PonderParitySnapshot Create(
            BonesPonderResult result,
            string executionArtifactJson,
            IReadOnlyList<PromptLine> prompts)
            => new(
                result.StrategyId.Value,
                result.StrategyDocument.Markdown,
                result.AuthoredStrategy.Source,
                result.AuthoredStrategy.Kind.ToString(),
                result.AuthoredStrategy.PromotionStatus.ToString(),
                result.ObservationsLoaded,
                result.StrategyArtifact.ArtifactId.Value,
                result.StrategyArtifact.Kind.ToString(),
                result.StrategyArtifact.ProducerType,
                result.StrategyArtifact.ProducerVersion,
                result.StrategyArtifact.RelativePath,
                executionArtifactJson,
                prompts);

        public static IReadOnlyList<string> FindMismatches(
            PonderParitySnapshot expected,
            PonderParitySnapshot actual)
        {
            var mismatches = new List<string>();

            void Check(string field, string expectedValue, string actualValue)
            {
                if (!string.Equals(expectedValue, actualValue, StringComparison.Ordinal))
                {
                    mismatches.Add($"{field}: expected '{expectedValue}' but was '{actualValue}'.");
                }
            }

            Check("StrategyId", expected.StrategyId, actual.StrategyId);
            Check("StrategyDocumentMarkdown", expected.StrategyDocumentMarkdown, actual.StrategyDocumentMarkdown);
            Check("AuthoredStrategySource", expected.AuthoredStrategySource, actual.AuthoredStrategySource);
            Check("AuthoredStrategyKind", expected.AuthoredStrategyKind, actual.AuthoredStrategyKind);
            Check("AuthoredStrategyPromotionStatus", expected.AuthoredStrategyPromotionStatus, actual.AuthoredStrategyPromotionStatus);
            Check("ObservationsLoaded", expected.ObservationsLoaded.ToString(), actual.ObservationsLoaded.ToString());
            Check("StrategyArtifactId", expected.StrategyArtifactId, actual.StrategyArtifactId);
            Check("StrategyArtifactKind", expected.StrategyArtifactKind, actual.StrategyArtifactKind);
            Check("StrategyArtifactProducerType", expected.StrategyArtifactProducerType, actual.StrategyArtifactProducerType);
            Check("StrategyArtifactProducerVersion", expected.StrategyArtifactProducerVersion, actual.StrategyArtifactProducerVersion);
            Check("StrategyArtifactRelativePath", expected.StrategyArtifactRelativePath, actual.StrategyArtifactRelativePath);
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
                    Check($"Prompts[{index}].Role", expected.Prompts[index].Role, actual.Prompts[index].Role);
                    Check($"Prompts[{index}].Content", expected.Prompts[index].Content, actual.Prompts[index].Content);
                }
            }

            return mismatches;
        }
    }

    private sealed class PonderFixture : IAsyncDisposable
    {
        private PonderFixture(
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

        public static Task<PonderFixture> CreateAsync(SessionId sessionId)
        {
            var repositoryPath = Path.Combine(
                Path.GetTempPath(),
                $"modus-bones-wp2-{Guid.NewGuid():N}");
            Directory.CreateDirectory(repositoryPath);

            var store = new WipArtifactStoreLocal(repositoryPath);
            var knowledgeStore = new BonesPlayerKnowledgeStore(store, repositoryPath, sessionId);
            return Task.FromResult(new PonderFixture(repositoryPath, sessionId, store, knowledgeStore));
        }

        public BonesPonderRequest Request(BonesPlayerId playerId)
            => new(playerId, RepositoryPath);

        public CapabilityContext Context()
            => new(SessionId, Path.Combine(RepositoryPath, ".wip", "worktrees", "bones-ponder"));

        public async Task SaveObservationAsync(BonesPlayerId playerId, BonesGameId gameId, string markdown)
        {
            await KnowledgeStore.SaveObservation(
                playerId,
                new BonesObservationTranscript(gameId, playerId, markdown),
                CancellationToken.None);
        }

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

    private sealed class SingleAnswerModelProvider
        : IModelProvider<BonesStrategyAuthoringRequest, BonesStrategyAuthoringResult>
    {
        private readonly string _markdown;

        public SingleAnswerModelProvider(string markdown)
        {
            _markdown = markdown;
        }

        public int CallCount { get; private set; }

        public List<ModelProviderRequest<BonesStrategyAuthoringRequest>> CapturedRequests { get; } = [];

        public ValueTask<ModelProviderResponse<BonesStrategyAuthoringResult>> ExecuteAsync(
            ModelProviderRequest<BonesStrategyAuthoringRequest> request,
            CapabilityContext context,
            CancellationToken cancellationToken)
        {
            CallCount++;
            CapturedRequests.Add(request);

            return ValueTask.FromResult(
                new ModelProviderResponse<BonesStrategyAuthoringResult>(
                    new BonesStrategyAuthoringResult(_markdown),
                    providerId: "single-answer-provider",
                    modelId: request.ModelId,
                    usage: new ModelProviderUsage(120, 48),
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
            => throw new NotSupportedException("Streaming is not used by the runtime Ponder tests.");
    }

    private sealed class ToolRouteChatModel : IChatModel
    {
        private readonly string _finalMarkdown;
        private readonly string _scriptSource;

        public ToolRouteChatModel(string finalMarkdown, string scriptSource)
        {
            _finalMarkdown = finalMarkdown;
            _scriptSource = scriptSource;
        }

        public List<ChatRequest> CapturedRequests { get; } = [];

        public List<string> ToolObservationContents { get; } = [];

        public ValueTask<ChatResponse> CompleteAsync(
            ChatRequest request,
            CapabilityContext context,
            CancellationToken cancellationToken)
        {
            CapturedRequests.Add(request);

            if (CapturedRequests.Count == 1)
            {
                // The control hook: if the agent stops advertising the compile tool, no tool call
                // is made and the final answer is deliberately not a compilable script.
                if (!request.Tools.Any(tool =>
                        string.Equals(tool.Name, BonesAgentToolIds.StrategyCompile.Value, StringComparison.Ordinal)))
                {
                    return Respond(request, "TOOL_DEFINITION_MISSING");
                }

                var call = new ToolCall(
                    "call-compile-1",
                    BonesAgentToolIds.StrategyCompile.Value,
                    JsonSerializer.SerializeToElement(new { scriptSource = _scriptSource }));
                return Respond(request, ChatMessage.Assistant(string.Empty, new[] { call }));
            }

            var lastMessage = request.Messages[^1];
            if (lastMessage.Role != ChatRole.Tool)
            {
                return Respond(request, "TOOL_ROUTE_MISSING");
            }

            ToolObservationContents.Add(lastMessage.Content);
            return Respond(
                request,
                IsSucceeded(lastMessage.Content) ? _finalMarkdown : "TOOL_ROUTE_FAILED");
        }

        public IAsyncEnumerable<StreamingChunk> StreamAsync(
            ChatRequest request,
            CapabilityContext context,
            CancellationToken cancellationToken)
            => throw new NotSupportedException("Streaming is not used by the runtime Ponder tests.");

        private static bool IsSucceeded(string toolObservationContent)
        {
            using var document = JsonDocument.Parse(toolObservationContent);
            return document.RootElement.TryGetProperty("succeeded", out var succeeded)
                && succeeded.ValueKind == JsonValueKind.True;
        }

        private ValueTask<ChatResponse> Respond(ChatRequest request, string markdown)
            => ValueTask.FromResult(new ChatResponse(
                ChatMessage.Assistant(markdown),
                request.ModelId,
                providerId: "tool-route-chat-model"));

        private ValueTask<ChatResponse> Respond(ChatRequest request, ChatMessage message)
            => ValueTask.FromResult(new ChatResponse(
                message,
                request.ModelId,
                providerId: "tool-route-chat-model"));
    }

    private sealed class DiagnosticsThenFinalChatModel : IChatModel
    {
        private readonly string _validScriptSource;
        private readonly string _invalidScriptSource;
        private readonly string _finalMarkdown;

        public DiagnosticsThenFinalChatModel(
            string validScriptSource,
            string invalidScriptSource,
            string finalMarkdown)
        {
            _validScriptSource = validScriptSource;
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
            => throw new NotSupportedException("Streaming is not used by the runtime Ponder tests.");
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
            => throw new NotSupportedException("Streaming is not used by the runtime Ponder tests.");
    }

    private sealed class CompletedExecutor : IAgentExecutor
    {
        private readonly string _markdown;

        public CompletedExecutor(string markdown)
        {
            _markdown = markdown;
        }

        public int CallCount { get; private set; }

        public ValueTask<AgentExecutionResult> ExecuteAsync(
            AgentExecutionRequest request,
            CapabilityContext context,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return ValueTask.FromResult(new AgentExecutionResult(
                AgentExecutionStatus.Completed,
                ChatMessage.Assistant(_markdown),
                Array.Empty<AgentStep>(),
                Usage: null,
                Error: null));
        }
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
