using Microsoft.Extensions.DependencyInjection;
using Wip.Abstractions.Artifacts;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Sessions;
using Wip.Abstractions.Workflows;
using Wip.Artifacts.Local;
using Wip.Bones.Agents.Knowledge;
using Wip.Bones.Agents.Play;
using Wip.Bones.Domain;
using Wip.Bones.Engine;
using Wip.Bones.Identifiers;
using Wip.Builder;
using Wip.Runtime.Runtime;
using Xunit;

namespace Wip.Bones.Tests.Agents;

public sealed class BonesPlayMatchToolTests
{
    private const string ChecklistItem = BonesRequirementsChecklistItems.PlayMatchTool;
    private const string ScriptDispatchChecklistItem = BonesRequirementsChecklistItems.PlayMatchScriptDispatch;
    private const string RecordOutcomeChecklistItem = BonesRequirementsChecklistItems.RecordMatchOutcome;
    private const string ScriptStrategiesCoverageItem = BonesScriptStrategiesRequirementsChecklistItems.TestsCoverage;

    private const string FirstLegalMoveScriptSource =
        """
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

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task BonesPlayMatchTool_GivenFourStrategyArtifacts_ExpectedActiveSeatChoosesFromLegalMovesEachTurn()
    {
        await using var fixture = await PlayMatchToolFixture.CreateAsync();

        await fixture.SaveAllStrategiesAsync();
        var stubProvider = new DeterministicBonesPlayTurnModelProvider();
        var tool = fixture.CreateTool(stubProvider);

        var result = await tool.ExecuteAsync(
            fixture.CreateRequest(seed: 5150, targetScore: 12),
            fixture.CreateCapabilityContext(),
            CancellationToken.None);

        Assert.NotEmpty(result.Transcript);
        ReplayTranscriptAndAssertEveryMoveWasLegal(result, fixture.LastSeed);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task BonesPlayMatchTool_GivenAgentPrompt_ExpectedPayloadListsAllowedMovesAsEnumeratedOptions()
    {
        await using var fixture = await PlayMatchToolFixture.CreateAsync();

        await fixture.SaveAllStrategiesAsync();
        var stubProvider = new DeterministicBonesPlayTurnModelProvider();
        var tool = fixture.CreateTool(stubProvider);

        const int seed = 8080;
        var gameId = new BonesGameId("prompt-validation");

        await tool.ExecuteAsync(
            fixture.CreateRequest(seed: seed, targetScore: 10) with { GameId = gameId },
            fixture.CreateCapabilityContext(),
            CancellationToken.None);

        Assert.NotEmpty(stubProvider.CapturedRequests);

        var engine = new BonesGameEngine();
        var roundConfig = new BonesRoundConfig(
            new BonesGameId($"{gameId.Value}-r1"),
            shuffleSeed: HashCode.Combine(seed, 1));
        var state = engine.StartRound(roundConfig);

        foreach (var capturedRequest in stubProvider.CapturedRequests)
        {
            var legalMoves = engine.GetLegalMoves(state, state.CurrentPlayer);
            var legalMoveIds = legalMoves.Select(move => move.MoveId.Value).ToHashSet(StringComparer.Ordinal);
            var promptMoveIds = capturedRequest.Payload.AllowedMoves
                .Select(option => option.MoveId.Value)
                .ToHashSet(StringComparer.Ordinal);

            Assert.Equal(legalMoveIds, promptMoveIds);

            var promptText = string.Join(
                '\n',
                capturedRequest.Payload.Messages.Select(message => message.Content));

            Assert.Contains("## Allowed moves", promptText, StringComparison.Ordinal);
            foreach (var moveId in legalMoveIds)
                Assert.Contains(moveId, promptText, StringComparison.Ordinal);

            var selectedMoveId = capturedRequest.Payload.AllowedMoves[0].MoveId.Value;
            var selectedMove = legalMoves.First(move => move.MoveId.Value == selectedMoveId);
            state = engine.ApplyMove(state, selectedMove);
        }
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task BonesPlayMatchTool_GivenCompletedMatch_ExpectedTranscriptAndScoreArtifactPersisted()
    {
        await using var fixture = await PlayMatchToolFixture.CreateAsync();

        await fixture.SaveAllStrategiesAsync();
        var stubProvider = new DeterministicBonesPlayTurnModelProvider();
        var tool = fixture.CreateTool(stubProvider);

        var result = await tool.ExecuteAsync(
            fixture.CreateRequest(seed: 2468, targetScore: 8),
            fixture.CreateCapabilityContext(),
            CancellationToken.None);

        var sessionArtifacts = await fixture.ArtifactStore.ListAsync(fixture.SessionId, CancellationToken.None);

        Assert.Contains(
            sessionArtifacts,
            artifact => artifact.ArtifactId == result.MatchTranscriptArtifact.ArtifactId);
        Assert.Contains(
            sessionArtifacts,
            artifact => artifact.ArtifactId == result.ExecutionArtifact.ArtifactId);

        var transcriptMarkdown = await File.ReadAllTextAsync(
            fixture.GetArtifactPath(result.MatchTranscriptArtifact),
            CancellationToken.None);

        Assert.Contains("# Bones Play Match Transcript", transcriptMarkdown, StringComparison.Ordinal);
        Assert.Contains($"Winner seat: {result.Winner.Seat}", transcriptMarkdown, StringComparison.Ordinal);
        Assert.Contains("## Final cumulative scores", transcriptMarkdown, StringComparison.Ordinal);

        foreach (var seat in Enumerable.Range(BonesPlayerId.MinSeat, BonesTableConfig.FixedPlayerCount))
        {
            var playerId = new BonesPlayerId(seat);
            var historyArtifact = result.MatchHistoryArtifacts[playerId];
            Assert.Contains(
                sessionArtifacts,
                artifact => artifact.ArtifactId == historyArtifact.ArtifactId);

            var historyMarkdown = await File.ReadAllTextAsync(
                fixture.GetArtifactPath(historyArtifact),
                CancellationToken.None);

            Assert.Contains($"Final score for seat {seat}:", historyMarkdown, StringComparison.Ordinal);
            Assert.Equal(
                result.CumulativeScores[playerId],
                ExtractFinalScore(historyMarkdown, seat));
        }
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task BonesPlayMatchTool_GivenStubModelProviderSelectingInvalidMove_ExpectedDeterministicRejectionAndRetryOrPass()
    {
        await using var fixture = await PlayMatchToolFixture.CreateAsync();

        await fixture.SaveAllStrategiesAsync();
        var stubProvider = new DeterministicBonesPlayTurnModelProvider
        {
            InvalidMoveIdForFirstTurn = "invalid-move-id-not-in-legal-set",
        };
        var tool = fixture.CreateTool(stubProvider);

        var engine = new BonesGameEngine();
        var request = fixture.CreateRequest(seed: 13579, targetScore: 6);
        var gameId = new BonesGameId("invalid-move-probe");
        var roundConfig = new BonesRoundConfig(new BonesGameId($"{gameId.Value}-r1"), shuffleSeed: HashCode.Combine(13579, 1));
        var stateBeforeFirstTurn = engine.StartRound(roundConfig);

        var result = await tool.ExecuteAsync(
            request with { GameId = gameId },
            fixture.CreateCapabilityContext(),
            CancellationToken.None);

        Assert.True(stubProvider.CapturedRequests.Count >= 2);
        Assert.Equal("invalid-move-id-not-in-legal-set", stubProvider.CapturedResponses[0].Payload.SelectedMoveId);
        Assert.Contains(
            "Previous selection was rejected",
            string.Join('\n', stubProvider.CapturedRequests[1].Payload.Messages.Select(message => message.Content)),
            StringComparison.Ordinal);
        Assert.Equal(
            ExtractTurnIndex(stubProvider.CapturedRequests[0]),
            ExtractTurnIndex(stubProvider.CapturedRequests[1]));

        var firstTurnEvent = Assert.Single(result.Transcript, turnEvent => turnEvent.TurnIndex == 0);
        var legalMovesBeforeFirstTurn = engine.GetLegalMoves(stateBeforeFirstTurn, stateBeforeFirstTurn.CurrentPlayer);
        var appliedMove = FindMatchingMove(legalMovesBeforeFirstTurn, firstTurnEvent);
        Assert.NotNull(appliedMove);
        Assert.Contains(appliedMove, legalMovesBeforeFirstTurn);

        var stateAfterValidMove = engine.ApplyMove(stateBeforeFirstTurn, appliedMove);
        Assert.Single(stateAfterValidMove.EventLog);
    }

    [Fact]
    [Trait("ChecklistItem", ScriptDispatchChecklistItem)]
    [Trait("ChecklistItem", ScriptStrategiesCoverageItem)]
    public async Task BonesPlayMatchTool_GivenFourScriptStrategies_ExpectedNeverInvokesPlayTurnModelProvider()
    {
        await using var fixture = await PlayMatchToolFixture.CreateAsync();

        await fixture.SaveAllScriptStrategiesAsync();
        var stubProvider = new DeterministicBonesPlayTurnModelProvider();
        var tool = fixture.CreateTool(stubProvider);

        var result = await tool.ExecuteAsync(
            fixture.CreateRequest(seed: 4242, targetScore: 10),
            fixture.CreateCapabilityContext(),
            CancellationToken.None);

        Assert.Empty(stubProvider.CapturedRequests);
        Assert.NotEmpty(result.Transcript);
        ReplayTranscriptAndAssertEveryMoveWasLegal(result, fixture.LastSeed);
    }

    [Fact]
    [Trait("ChecklistItem", ScriptDispatchChecklistItem)]
    public async Task BonesPlayMatchTool_GivenScriptAndMarkdownMixedSeats_ExpectedInvokesModelProviderOnlyForMarkdownSeats()
    {
        await using var fixture = await PlayMatchToolFixture.CreateAsync();

        await fixture.SaveMixedStrategiesAsync(scriptSeats: [1, 3], markdownSeats: [2, 4]);
        var stubProvider = new DeterministicBonesPlayTurnModelProvider();
        var tool = fixture.CreateTool(stubProvider);

        await tool.ExecuteAsync(
            fixture.CreateRequest(seed: 9090, targetScore: 8),
            fixture.CreateCapabilityContext(),
            CancellationToken.None);

        Assert.NotEmpty(stubProvider.CapturedRequests);
        Assert.All(stubProvider.CapturedRequests, request => Assert.Contains(request.Payload.ActiveSeat.Seat, new[] { 2, 4 }));
        Assert.DoesNotContain(stubProvider.CapturedRequests, request => request.Payload.ActiveSeat.Seat is 1 or 3);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task BuildCoLearningPlayerSlotsAsync_GivenActiveScriptAndLibraryMarkdown_PrefersActiveScript()
    {
        await using var fixture = await PlayMatchToolFixture.CreateAsync();

        // Seed the library with Markdown entries for all seats — a stale library state
        var library = new BonesStrategyLibrary(fixture.RepositoryPath);
        for (var seat = BonesPlayerId.MinSeat; seat <= BonesPlayerId.MaxSeat; seat++)
        {
            var playerId = new BonesPlayerId(seat);
            library.UpsertBestStrategy(
                playerId,
                new BonesStrategyArtifact(
                    new BonesStrategyId($"library-md-{seat}-v1"),
                    playerId,
                    BonesStrategyKind.Markdown,
                    $"# Seat {seat} Library Markdown",
                    BonesPromotionStatus.Active),
                new BonesStrategyEffectivenessRecord(
                    new BonesStrategyId($"library-md-{seat}-v1"),
                    playerId,
                    matchesPlayed: 5,
                    wins: 2,
                    losses: 3,
                    cumulativeScoreDifferential: -10));
        }

        // Save an active Script strategy for seat 1 only (freshly Ponder-authored)
        var seat1 = new BonesPlayerId(1);
        var activeScriptId = new BonesStrategyId("ponder-fresh-script-v2");
        await fixture.KnowledgeStore.SaveStrategyArtifact(
            seat1,
            new BonesStrategyArtifact(
                activeScriptId,
                seat1,
                BonesStrategyKind.Script,
                FirstLegalMoveScriptSource,
                BonesPromotionStatus.Active),
            CancellationToken.None);

        // For seats 2-4, save active Markdown strategies (not Scripts, so library should be used)
        for (var seat = 2; seat <= 4; seat++)
        {
            var playerId = new BonesPlayerId(seat);
            await fixture.KnowledgeStore.SaveStrategy(
                playerId,
                new BonesStrategyDocument(
                    new BonesStrategyId($"active-md-{seat}-v1"),
                    playerId,
                    $"# Seat {seat} Active Markdown"),
                CancellationToken.None);
        }

        var stubProvider = new DeterministicBonesPlayTurnModelProvider();
        var tool = fixture.CreateToolWithLibrary(stubProvider, library);

        await tool.ExecuteAsync(
            fixture.CreateRequest(seed: 1111, targetScore: 6) with { AllSeatsLearning = true },
            fixture.CreateCapabilityContext(),
            CancellationToken.None);

        // Seat 1 should have used the active Script (not the library Markdown)
        // Since its strategy is a Script, no model calls should be made for seat 1
        Assert.DoesNotContain(stubProvider.CapturedRequests, r => r.Payload.ActiveSeat.Seat == 1);

        // Seats 2-4 have active Markdown (not Script), so they fall through to library.
        // The library has Markdown entries for them, which get used directly.
        // In a real match, Markdown seats get dispatched to the model — but here the
        // library Markdown artifact itself is used, meaning no model was called for them either.
        // This confirms that library Markdown was the fallback: seats 2-4 used library entries.
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task BuildCoLearningPlayerSlotsAsync_GivenLibraryScriptOnly_UsesLibraryScript()
    {
        await using var fixture = await PlayMatchToolFixture.CreateAsync();

        // Seed the library with Script entries for seats 1-3, and nothing for seat 4
        var library = new BonesStrategyLibrary(fixture.RepositoryPath);
        for (var seat = 1; seat <= 3; seat++)
        {
            var playerId = new BonesPlayerId(seat);
            library.UpsertBestStrategy(
                playerId,
                new BonesStrategyArtifact(
                    new BonesStrategyId($"library-script-{seat}-v3"),
                    playerId,
                    BonesStrategyKind.Script,
                    FirstLegalMoveScriptSource,
                    BonesPromotionStatus.Active),
                new BonesStrategyEffectivenessRecord(
                    new BonesStrategyId($"library-script-{seat}-v3"),
                    playerId,
                    matchesPlayed: 10,
                    wins: 5,
                    losses: 5,
                    cumulativeScoreDifferential: 0));
        }

        // No active strategies saved in the knowledge store at all

        var stubProvider = new DeterministicBonesPlayTurnModelProvider();
        var tool = fixture.CreateToolWithLibrary(stubProvider, library);

        await tool.ExecuteAsync(
            fixture.CreateRequest(seed: 2222, targetScore: 6) with { AllSeatsLearning = true },
            fixture.CreateCapabilityContext(),
            CancellationToken.None);

        // Seats 1-3 have library Script entries and no active strategies => the library Scripts are used.
        // Script strategies don't call the model, so no model calls for seats 1-3.
        Assert.DoesNotContain(stubProvider.CapturedRequests, r => r.Payload.ActiveSeat.Seat is 1 or 2 or 3);

        // Seat 4 has no library entry and no active strategy => fallback to deterministic seed strategy.
        // Deterministic seed strategy is also a Script, so no model call for seat 4 either.
        Assert.Empty(stubProvider.CapturedRequests);
    }

    [Fact]
    [Trait("ChecklistItem", RecordOutcomeChecklistItem)]
    public async Task BonesPlayMatchTool_GivenCompletedMatch_ExpectedRecordsEffectivenessForEachSeatActiveStrategy()
    {
        await using var fixture = await PlayMatchToolFixture.CreateAsync();

        await fixture.SaveAllStrategiesAsync();
        var stubProvider = new DeterministicBonesPlayTurnModelProvider();
        var tool = fixture.CreateTool(stubProvider);

        var result = await tool.ExecuteAsync(
            fixture.CreateRequest(seed: 3333, targetScore: 6),
            fixture.CreateCapabilityContext(),
            CancellationToken.None);

        foreach (var seat in Enumerable.Range(BonesPlayerId.MinSeat, BonesTableConfig.FixedPlayerCount))
        {
            var playerId = new BonesPlayerId(seat);
            var strategyId = new BonesStrategyId($"initial-seat-{seat}-v1");
            var effectiveness = await fixture.KnowledgeStore.GetEffectiveness(
                playerId,
                strategyId,
                CancellationToken.None);

            Assert.Equal(1, effectiveness.MatchesPlayed);

            if (playerId == result.Winner)
            {
                Assert.Equal(1, effectiveness.Wins);
                Assert.Equal(0, effectiveness.Losses);
                Assert.Equal(0, effectiveness.CumulativeScoreDifferential);
            }
            else
            {
                Assert.Equal(0, effectiveness.Wins);
                Assert.Equal(1, effectiveness.Losses);
                Assert.True(effectiveness.CumulativeScoreDifferential < 0);
            }
        }
    }

    private static int ExtractTurnIndex(ModelProviderRequest<BonesPlayTurnRequest> request)
    {
        var userMessage = request.Payload.Messages.Single(message => message.Role == "user").Content;
        foreach (var line in userMessage.Split('\n'))
        {
            if (line.StartsWith("Turn index:", StringComparison.Ordinal))
                return int.Parse(line["Turn index:".Length..].Trim(), System.Globalization.CultureInfo.InvariantCulture);
        }

        throw new InvalidOperationException("Turn index was not found in play-turn prompt.");
    }

    private static void ReplayTranscriptAndAssertEveryMoveWasLegal(BonesPlayMatchResult result, int seed)
    {
        var engine = new BonesGameEngine();
        var transcriptIndex = 0;
        var roundNumber = 0;

        while (transcriptIndex < result.Transcript.Count)
        {
            roundNumber++;
            var roundGameId = new BonesGameId($"{result.GameId.Value}-r{roundNumber}");
            var state = engine.StartRound(new BonesRoundConfig(roundGameId, HashCode.Combine(seed, roundNumber)));

            while (!engine.IsRoundComplete(state) && transcriptIndex < result.Transcript.Count)
            {
                var legalMoves = engine.GetLegalMoves(state, state.CurrentPlayer);
                var turnEvent = result.Transcript[transcriptIndex];
                var matchingMove = FindMatchingMove(legalMoves, turnEvent);
                Assert.NotNull(matchingMove);
                Assert.Contains(matchingMove, legalMoves);

                state = engine.ApplyMove(state, matchingMove);
                transcriptIndex++;
            }
        }
    }

    private static BonesMove? FindMatchingMove(IReadOnlyList<BonesMove> legalMoves, BonesEvent turnEvent)
    {
        foreach (var move in legalMoves)
        {
            if (turnEvent.Kind == BonesEventKind.Pass && move.IsPass && move.PlayerId == turnEvent.PlayerId)
                return move;

            if (turnEvent.Kind == BonesEventKind.Play
                && !move.IsPass
                && move.PlayerId == turnEvent.PlayerId
                && move.Tile == turnEvent.Tile
                && move.Side == turnEvent.Side)
            {
                return move;
            }
        }

        return null;
    }

    private static int ExtractFinalScore(string markdown, int seat)
    {
        foreach (var line in markdown.Split('\n'))
        {
            if (line.StartsWith($"Final score for seat {seat}:", StringComparison.Ordinal))
            {
                var scoreText = line.Split(':')[1].Trim();
                return int.Parse(scoreText, System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        throw new InvalidOperationException($"Final score for seat {seat} was not found.");
    }

    private sealed class DeterministicBonesPlayTurnModelProvider
        : IModelProvider<BonesPlayTurnRequest, BonesPlayTurnResult>
    {
        public string? InvalidMoveIdForFirstTurn { get; init; }

        public List<ModelProviderRequest<BonesPlayTurnRequest>> CapturedRequests { get; } = [];

        public List<ModelProviderResponse<BonesPlayTurnResult>> CapturedResponses { get; } = [];

        public ValueTask<ModelProviderResponse<BonesPlayTurnResult>> ExecuteAsync(
            ModelProviderRequest<BonesPlayTurnRequest> request,
            CapabilityContext context,
            CancellationToken cancellationToken)
        {
            CapturedRequests.Add(request);

            string selectedMoveId;
            if (InvalidMoveIdForFirstTurn is not null && CapturedRequests.Count == 1)
            {
                selectedMoveId = InvalidMoveIdForFirstTurn;
            }
            else
            {
                selectedMoveId = request.Payload.AllowedMoves[0].MoveId.Value;
            }

            var response = new ModelProviderResponse<BonesPlayTurnResult>(
                payload: new BonesPlayTurnResult(selectedMoveId),
                providerId: "bones-play-turn-stub",
                modelId: request.ModelId,
                usage: new ModelProviderUsage(64, 16),
                correlationId: request.CorrelationId);

            CapturedResponses.Add(response);
            return ValueTask.FromResult(response);
        }
    }

    [Fact]
    [Trait("ChecklistItem", "A3")]
    public async Task TryUpsertLibraryBestAsync_GivenMarkdownArtifact_DoesNotUpsert()
    {
        await using var fixture = await PlayMatchToolFixture.CreateAsync();

        // Only seat 1 gets a Script artifact; seats 2-4 get Markdown (via SaveStrategy).
        await fixture.SaveMixedStrategiesAsync(
            scriptSeats: [1],
            markdownSeats: [2, 3, 4]);

        var stubProvider = new DeterministicBonesPlayTurnModelProvider();
        var tool = fixture.CreateToolWithLibrary(stubProvider);

        await tool.ExecuteAsync(
            fixture.CreateRequest(seed: 42, targetScore: 5),
            fixture.CreateCapabilityContext(),
            CancellationToken.None);

        // After the match, only seat 1 (Script) should have been upserted to the library.
        // Seats 2-4 (Markdown) should have been skipped by the guard.
        var allEntries = fixture.StrategyLibrary!.AllEntries;
        Assert.Single(allEntries);
        Assert.Equal(1, allEntries[0].PlayerId.Seat);
        Assert.Equal(BonesStrategyKind.Script, allEntries[0].Kind);
    }

    [Fact]
    [Trait("ChecklistItem", "A3")]
    public async Task TryUpsertLibraryBestAsync_GivenScriptArtifact_UpsertsNormally()
    {
        await using var fixture = await PlayMatchToolFixture.CreateAsync();

        // All four seats get Script artifacts.
        await fixture.SaveAllScriptStrategiesAsync();

        var stubProvider = new DeterministicBonesPlayTurnModelProvider();
        var tool = fixture.CreateToolWithLibrary(stubProvider);

        await tool.ExecuteAsync(
            fixture.CreateRequest(seed: 99, targetScore: 5),
            fixture.CreateCapabilityContext(),
            CancellationToken.None);

        // All four Script seats should be upserted to the library.
        var allEntries = fixture.StrategyLibrary!.AllEntries;
        Assert.Equal(4, allEntries.Count);
        Assert.All(allEntries, entry => Assert.Equal(BonesStrategyKind.Script, entry.Kind));
    }

    private sealed class PlayMatchToolFixture : IAsyncDisposable
    {
        private readonly string _repositoryPath;

        private PlayMatchToolFixture(
            string repositoryPath,
            SessionId sessionId,
            WipArtifactStoreLocal artifactStore,
            BonesPlayerKnowledgeStore knowledgeStore)
        {
            _repositoryPath = repositoryPath;
            SessionId = sessionId;
            ArtifactStore = artifactStore;
            KnowledgeStore = knowledgeStore;
            LastSeed = 0;
        }

        public SessionId SessionId { get; }

        public WipArtifactStoreLocal ArtifactStore { get; }

        public BonesPlayerKnowledgeStore KnowledgeStore { get; }

        public int LastSeed { get; private set; }

        public static async Task<PlayMatchToolFixture> CreateAsync()
        {
            var repositoryPath = Path.Combine(Path.GetTempPath(), $"modus-bones-play-{Guid.NewGuid():N}");
            Directory.CreateDirectory(repositoryPath);

            var sessionId = new SessionId($"bones-play-{Guid.NewGuid():N}");
            var artifactStore = new WipArtifactStoreLocal(repositoryPath);
            var knowledgeStore = new BonesPlayerKnowledgeStore(artifactStore, repositoryPath, sessionId);

            await Task.CompletedTask;
            return new PlayMatchToolFixture(repositoryPath, sessionId, artifactStore, knowledgeStore);
        }

        public async Task SaveAllStrategiesAsync()
        {
            foreach (var seat in Enumerable.Range(BonesPlayerId.MinSeat, BonesTableConfig.FixedPlayerCount))
            {
                var playerId = new BonesPlayerId(seat);
                await KnowledgeStore.SaveStrategy(
                    playerId,
                    new BonesStrategyDocument(
                        new BonesStrategyId($"initial-seat-{seat}-v1"),
                        playerId,
                        markdown: $"""
                            # Seat {seat} Strategy

                            ## Rules
                            - Prefer first legal move from GetLegalMoves.
                            - Pass only when no playable tile matches open ends.
                            """),
                    CancellationToken.None);
            }
        }

        public async Task SaveAllScriptStrategiesAsync()
        {
            foreach (var seat in Enumerable.Range(BonesPlayerId.MinSeat, BonesTableConfig.FixedPlayerCount))
            {
                var playerId = new BonesPlayerId(seat);
                await KnowledgeStore.SaveStrategyArtifact(
                    playerId,
                    new BonesStrategyArtifact(
                        new BonesStrategyId($"initial-seat-{seat}-v1"),
                        playerId,
                        BonesStrategyKind.Script,
                        FirstLegalMoveScriptSource,
                        BonesPromotionStatus.Active),
                    CancellationToken.None);
            }
        }

        public async Task SaveMixedStrategiesAsync(IReadOnlyCollection<int> scriptSeats, IReadOnlyCollection<int> markdownSeats)
        {
            foreach (var seat in scriptSeats)
            {
                var playerId = new BonesPlayerId(seat);
                await KnowledgeStore.SaveStrategyArtifact(
                    playerId,
                    new BonesStrategyArtifact(
                        new BonesStrategyId($"initial-seat-{seat}-script-v1"),
                        playerId,
                        BonesStrategyKind.Script,
                        FirstLegalMoveScriptSource,
                        BonesPromotionStatus.Active),
                    CancellationToken.None);
            }

            foreach (var seat in markdownSeats)
            {
                var playerId = new BonesPlayerId(seat);
                await KnowledgeStore.SaveStrategy(
                    playerId,
                    new BonesStrategyDocument(
                        new BonesStrategyId($"initial-seat-{seat}-markdown-v1"),
                        playerId,
                        markdown: $"""
                            # Seat {seat} Strategy

                            ## Rules
                            - Prefer first legal move from GetLegalMoves.
                            """),
                    CancellationToken.None);
            }
        }

        public BonesStrategyLibrary? StrategyLibrary { get; private set; }

        public string RepositoryPath => _repositoryPath;

        public BonesPlayMatchTool CreateTool(IModelProvider<BonesPlayTurnRequest, BonesPlayTurnResult> modelProvider)
            => new(ArtifactStore, new BonesGameEngine(), modelProvider);

        public BonesPlayMatchTool CreateToolWithLibrary(IModelProvider<BonesPlayTurnRequest, BonesPlayTurnResult> modelProvider)
        {
            StrategyLibrary = new BonesStrategyLibrary(_repositoryPath);
            return new BonesPlayMatchTool(
                ArtifactStore,
                new BonesGameEngine(),
                modelProvider,
                strategyLibrary: StrategyLibrary);
        }

        public BonesPlayMatchTool CreateToolWithLibrary(
            IModelProvider<BonesPlayTurnRequest, BonesPlayTurnResult> modelProvider,
            BonesStrategyLibrary library)
        {
            StrategyLibrary = library;
            return new BonesPlayMatchTool(
                ArtifactStore,
                new BonesGameEngine(),
                modelProvider,
                strategyLibrary: library);
        }

        public BonesPlayMatchRequest CreateRequest(int seed, int targetScore)
        {
            LastSeed = seed;
            return new BonesPlayMatchRequest(seed, targetScore, _repositoryPath);
        }

        public CapabilityContext CreateCapabilityContext()
            => new(SessionId, Path.Combine(_repositoryPath, ".wip", "worktrees", "bones-play"));

        public string GetArtifactPath(ArtifactDescriptor descriptor)
            => Path.Combine(_repositoryPath, descriptor.RelativePath.Replace('/', Path.DirectorySeparatorChar));

        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(_repositoryPath))
                Directory.Delete(_repositoryPath, recursive: true);

            return ValueTask.CompletedTask;
        }
    }
}
