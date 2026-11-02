using Wip.Bones.Agents.Enhance;
using Wip.Bones.Agents.Knowledge;
using Wip.Bones.Domain;
using Wip.Bones.Engine;
using Wip.Bones.Identifiers;
using Xunit;

namespace Wip.Bones.Tests.Agents;

public sealed class BonesEnhancePromptBuilderTests
{
    private const string ChecklistItem = BonesRequirementsChecklistItems.EnhanceScriptRefinementPrompt;

    private const string PriorScriptSource =
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
    public void BonesEnhancePromptBuilder_GivenPriorScriptAndLossOutcome_ExpectedUserPromptRequestsScriptRevision()
    {
        var playerId = new BonesPlayerId(1);
        var priorStrategy = new BonesStrategyDocument(
            new BonesStrategyId("initial-seat-1-v1"),
            playerId,
            PriorScriptSource);
        var matchHistories = new[]
        {
            new BonesMatchHistoryTranscript(
                new BonesGameId("loss-match"),
                playerId,
                """
                # Bones Match History
                Match winner seat: 3
                Final score for seat 1: 4
                Final score for seat 3: 10
                """),
        };

        var request = BonesEnhancePromptBuilder.BuildEnhancementRequest(
            playerId,
            priorStrategy,
            matchHistories,
            ["bones-player-1-match-loss-match"]);

        var userPrompt = request.Messages.Single(message => message.Role == "user").Content;

        Assert.Contains(BonesEnhancePromptBuilder.PriorScriptHeader, userPrompt, StringComparison.Ordinal);
        Assert.Contains(PriorScriptSource, userPrompt, StringComparison.Ordinal);
        Assert.Contains("Outcome: Loss", userPrompt, StringComparison.Ordinal);
        Assert.Contains("Score differential vs winner: -6", userPrompt, StringComparison.Ordinal);
        Assert.Contains(BonesEnhancePromptBuilder.RefinementGuidanceHeader, userPrompt, StringComparison.Ordinal);
        Assert.Contains("revise ChooseMove logic", userPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("```csharp", userPrompt, StringComparison.Ordinal);
        Assert.Contains(BonesStrategyScriptApiReference.InterfaceName, userPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("## Rules", userPrompt, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BonesEnhancePromptBuilder_GivenPriorScriptAndWinOutcome_ExpectedUserPromptPreservesWinningHeuristics()
    {
        var playerId = new BonesPlayerId(2);
        var priorStrategy = new BonesStrategyDocument(
            new BonesStrategyId("initial-seat-2-v1"),
            playerId,
            PriorScriptSource);
        var matchHistories = new[]
        {
            new BonesMatchHistoryTranscript(
                new BonesGameId("win-match"),
                playerId,
                """
                # Bones Match History
                Match winner seat: 2
                Final score for seat 2: 14
                Final score for seat 4: 8
                """),
        };

        var request = BonesEnhancePromptBuilder.BuildEnhancementRequest(
            playerId,
            priorStrategy,
            matchHistories,
            ["bones-player-2-match-win-match"]);

        var systemPrompt = request.Messages.Single(message => message.Role == "system").Content;
        var userPrompt = request.Messages.Single(message => message.Role == "user").Content;

        Assert.Contains("C# strategy script", systemPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Preserve incumbent", systemPrompt, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("Outcome: Win", userPrompt, StringComparison.Ordinal);
        Assert.Contains("preserve effective heuristics", userPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(BonesStrategyScriptApiReference.ContractHeader, userPrompt, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", BonesRequirementsChecklistItems.T2_4_OpponentAwareEnhancement)]
    public void BuildEnhancementRequest_GivenOpponentStrategies_IncludesOpponentSummariesInPrompt()
    {
        var playerId = new BonesPlayerId(1);
        var priorStrategy = new BonesStrategyDocument(
            new BonesStrategyId("seat-1-v1"),
            playerId,
            PriorScriptSource);
        var matchHistories = new[]
        {
            new BonesMatchHistoryTranscript(
                new BonesGameId("game-1"),
                playerId,
                "# Bones Match History\nMatch winner seat: 1\nFinal score for seat 1: 10\n"),
        };

        var opponentStrategies = new Dictionary<BonesPlayerId, BonesStrategyArtifact>
        {
            [new BonesPlayerId(2)] = new BonesStrategyArtifact(
                new BonesStrategyId("seat-2-aggressive"),
                new BonesPlayerId(2),
                BonesStrategyKind.Script,
                "public sealed class SeatStrategy : IBonesPlayerSlot { public BonesMove ChooseMove(BonesRoundState s, IReadOnlyList<BonesMove> m) => m[0]; }",
                BonesPromotionStatus.Active),
            [new BonesPlayerId(3)] = new BonesStrategyArtifact(
                new BonesStrategyId("seat-3-blocker"),
                new BonesPlayerId(3),
                BonesStrategyKind.Markdown,
                "# Blocking Strategy\nAlways block opponent high tiles.",
                BonesPromotionStatus.Active),
        };

        var request = BonesEnhancePromptBuilder.BuildEnhancementRequest(
            playerId,
            priorStrategy,
            matchHistories,
            ["bones-player-1-match-game-1"],
            opponentStrategies);

        var userPrompt = request.Messages.Single(message => message.Role == "user").Content;

        Assert.Contains(BonesEnhancePromptBuilder.OpponentStrategySummaryHeader, userPrompt, StringComparison.Ordinal);
        Assert.Contains("### Opponent seat 2 (Script)", userPrompt, StringComparison.Ordinal);
        Assert.Contains("seat-2-aggressive", userPrompt, StringComparison.Ordinal);
        Assert.Contains("### Opponent seat 3 (Markdown)", userPrompt, StringComparison.Ordinal);
        Assert.Contains("seat-3-blocker", userPrompt, StringComparison.Ordinal);
        Assert.Contains("Blocking Strategy", userPrompt, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", BonesRequirementsChecklistItems.T2_4_OpponentAwareEnhancement)]
    public void BuildEnhancementRequest_GivenNoOpponentStrategies_OmitsOpponentSection()
    {
        var playerId = new BonesPlayerId(1);
        var priorStrategy = new BonesStrategyDocument(
            new BonesStrategyId("seat-1-v1"),
            playerId,
            PriorScriptSource);
        var matchHistories = Array.Empty<BonesMatchHistoryTranscript>();

        var request = BonesEnhancePromptBuilder.BuildEnhancementRequest(
            playerId,
            priorStrategy,
            matchHistories,
            []);

        var userPrompt = request.Messages.Single(message => message.Role == "user").Content;

        Assert.DoesNotContain(BonesEnhancePromptBuilder.OpponentStrategySummaryHeader, userPrompt, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", BonesRequirementsChecklistItems.T2_4_OpponentAwareEnhancement)]
    public void BuildEnhancementRequest_GivenEmptyOpponentDictionary_OmitsOpponentSection()
    {
        var playerId = new BonesPlayerId(1);
        var priorStrategy = new BonesStrategyDocument(
            new BonesStrategyId("seat-1-v1"),
            playerId,
            PriorScriptSource);
        var matchHistories = Array.Empty<BonesMatchHistoryTranscript>();
        var emptyOpponents = new Dictionary<BonesPlayerId, BonesStrategyArtifact>();

        var request = BonesEnhancePromptBuilder.BuildEnhancementRequest(
            playerId,
            priorStrategy,
            matchHistories,
            [],
            emptyOpponents);

        var userPrompt = request.Messages.Single(message => message.Role == "user").Content;

        Assert.DoesNotContain(BonesEnhancePromptBuilder.OpponentStrategySummaryHeader, userPrompt, StringComparison.Ordinal);
    }

    // --- E1: Configurable OpponentSourceExcerptMaxLength ---

    [Fact]
    public void BuildEnhancementRequest_GivenCustomExcerptLength_UsesConfiguredMaxLength()
    {
        var playerId = new BonesPlayerId(1);
        var priorStrategy = new BonesStrategyDocument(
            new BonesStrategyId("seat-1-v1"),
            playerId,
            PriorScriptSource);
        var matchHistories = Array.Empty<BonesMatchHistoryTranscript>();

        // Create an opponent with a long source (>50 chars)
        var longSource = new string('x', 200);
        var opponentStrategies = new Dictionary<BonesPlayerId, BonesStrategyArtifact>
        {
            [new BonesPlayerId(2)] = new BonesStrategyArtifact(
                new BonesStrategyId("long-source"),
                new BonesPlayerId(2),
                BonesStrategyKind.Markdown,
                longSource,
                BonesPromotionStatus.Active),
        };

        var smallMaxLength = 50;
        var request = BonesEnhancePromptBuilder.BuildEnhancementRequest(
            playerId,
            priorStrategy,
            matchHistories,
            [],
            opponentStrategies,
            opponentSourceExcerptMaxLength: smallMaxLength);

        var userPrompt = request.Messages.Single(message => message.Role == "user").Content;

        Assert.Contains(BonesEnhancePromptBuilder.OpponentStrategySummaryHeader, userPrompt, StringComparison.Ordinal);
        // Should contain truncated source (only first 50 chars + "...")
        Assert.Contains(new string('x', 50) + "...", userPrompt, StringComparison.Ordinal);
        // Should NOT contain the full 200-char source
        Assert.DoesNotContain(new string('x', 51), userPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildEnhancementRequest_GivenDefaultExcerptLength_UsesDefault4000()
    {
        var playerId = new BonesPlayerId(1);
        var priorStrategy = new BonesStrategyDocument(
            new BonesStrategyId("seat-1-v1"),
            playerId,
            PriorScriptSource);
        var matchHistories = Array.Empty<BonesMatchHistoryTranscript>();

        var shortSource = "short source text";
        var opponentStrategies = new Dictionary<BonesPlayerId, BonesStrategyArtifact>
        {
            [new BonesPlayerId(2)] = new BonesStrategyArtifact(
                new BonesStrategyId("short-source"),
                new BonesPlayerId(2),
                BonesStrategyKind.Markdown,
                shortSource,
                BonesPromotionStatus.Active),
        };

        var request = BonesEnhancePromptBuilder.BuildEnhancementRequest(
            playerId,
            priorStrategy,
            matchHistories,
            [],
            opponentStrategies);

        var userPrompt = request.Messages.Single(message => message.Role == "user").Content;

        Assert.Contains(shortSource, userPrompt, StringComparison.Ordinal);
    }

    // --- E2: Opponent performance summaries in enhance prompts ---

    [Fact]
    public void BuildEnhancementRequest_GivenOpponentEffectiveness_IncludesPerformanceSection()
    {
        var playerId = new BonesPlayerId(1);
        var priorStrategy = new BonesStrategyDocument(
            new BonesStrategyId("seat-1-v1"),
            playerId,
            PriorScriptSource);
        var matchHistories = Array.Empty<BonesMatchHistoryTranscript>();

        var opponentEffectiveness = new Dictionary<BonesPlayerId, BonesStrategyEffectivenessRecord>
        {
            [new BonesPlayerId(2)] = new BonesStrategyEffectivenessRecord(
                new BonesStrategyId("opp-2"),
                new BonesPlayerId(2),
                matchesPlayed: 10,
                wins: 6,
                losses: 4,
                cumulativeScoreDifferential: 15),
            [new BonesPlayerId(3)] = new BonesStrategyEffectivenessRecord(
                new BonesStrategyId("opp-3"),
                new BonesPlayerId(3),
                matchesPlayed: 10,
                wins: 3,
                losses: 7,
                cumulativeScoreDifferential: -20),
        };

        var request = BonesEnhancePromptBuilder.BuildEnhancementRequest(
            playerId,
            priorStrategy,
            matchHistories,
            [],
            opponentEffectiveness: opponentEffectiveness);

        var userPrompt = request.Messages.Single(message => message.Role == "user").Content;

        Assert.Contains(BonesEnhancePromptBuilder.OpponentPerformanceHeader, userPrompt, StringComparison.Ordinal);
        Assert.Contains("Opponent seat 2", userPrompt, StringComparison.Ordinal);
        Assert.Contains("6W / 4L", userPrompt, StringComparison.Ordinal);
        Assert.Contains("60.0%", userPrompt, StringComparison.Ordinal);
        Assert.Contains("cumulative score diff: 15", userPrompt, StringComparison.Ordinal);
        Assert.Contains("Opponent seat 3", userPrompt, StringComparison.Ordinal);
        Assert.Contains("3W / 7L", userPrompt, StringComparison.Ordinal);
        Assert.Contains("cumulative score diff: -20", userPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildEnhancementRequest_GivenNullOpponentEffectiveness_OmitsPerformanceSection()
    {
        var playerId = new BonesPlayerId(1);
        var priorStrategy = new BonesStrategyDocument(
            new BonesStrategyId("seat-1-v1"),
            playerId,
            PriorScriptSource);
        var matchHistories = Array.Empty<BonesMatchHistoryTranscript>();

        var request = BonesEnhancePromptBuilder.BuildEnhancementRequest(
            playerId,
            priorStrategy,
            matchHistories,
            []);

        var userPrompt = request.Messages.Single(message => message.Role == "user").Content;

        Assert.DoesNotContain(BonesEnhancePromptBuilder.OpponentPerformanceHeader, userPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildEnhancementRequest_GivenZeroMatchEffectiveness_ShowsNA()
    {
        var playerId = new BonesPlayerId(1);
        var priorStrategy = new BonesStrategyDocument(
            new BonesStrategyId("seat-1-v1"),
            playerId,
            PriorScriptSource);
        var matchHistories = Array.Empty<BonesMatchHistoryTranscript>();

        var opponentEffectiveness = new Dictionary<BonesPlayerId, BonesStrategyEffectivenessRecord>
        {
            [new BonesPlayerId(2)] = BonesStrategyEffectivenessRecord.Empty(
                new BonesStrategyId("empty-record"),
                new BonesPlayerId(2)),
        };

        var request = BonesEnhancePromptBuilder.BuildEnhancementRequest(
            playerId,
            priorStrategy,
            matchHistories,
            [],
            opponentEffectiveness: opponentEffectiveness);

        var userPrompt = request.Messages.Single(message => message.Role == "user").Content;

        Assert.Contains(BonesEnhancePromptBuilder.OpponentPerformanceHeader, userPrompt, StringComparison.Ordinal);
        Assert.Contains("N/A", userPrompt, StringComparison.Ordinal);
        Assert.Contains("0W / 0L", userPrompt, StringComparison.Ordinal);
    }

    // --- E3: Opponent behavioural profiles in enhance prompts ---

    [Fact]
    public void BuildEnhancementRequest_GivenOpponentProfiles_IncludesProfileSection()
    {
        var playerId = new BonesPlayerId(1);
        var priorStrategy = new BonesStrategyDocument(
            new BonesStrategyId("seat-1-v1"),
            playerId,
            PriorScriptSource);
        var matchHistories = Array.Empty<BonesMatchHistoryTranscript>();

        var frequencies = new Dictionary<int, double> { [4] = 0.5, [8] = 0.3, [12] = 0.2 };

        var opponentProfiles = new Dictionary<BonesPlayerId, BonesOpponentProfile>
        {
            [new BonesPlayerId(2)] = new BonesOpponentProfile
            {
                PreferredPipsFrequencies = frequencies,
                DoublePlayFrequency = 0.25,
                PassRate = 0.10,
                TotalMovesAnalyzed = 30,
            },
        };

        var request = BonesEnhancePromptBuilder.BuildEnhancementRequest(
            playerId,
            priorStrategy,
            matchHistories,
            [],
            opponentProfiles: opponentProfiles);

        var userPrompt = request.Messages.Single(message => message.Role == "user").Content;

        Assert.Contains(BonesEnhancePromptBuilder.OpponentProfileHeader, userPrompt, StringComparison.Ordinal);
        Assert.Contains("**Seat 2** (30 moves", userPrompt, StringComparison.Ordinal);
        Assert.Contains("Double play frequency: 25.0%", userPrompt, StringComparison.Ordinal);
        Assert.Contains("Pass rate: 10.0%", userPrompt, StringComparison.Ordinal);
        Assert.Contains("4 pips: 50.0%", userPrompt, StringComparison.Ordinal);
        Assert.Contains("8 pips: 30.0%", userPrompt, StringComparison.Ordinal);
        Assert.Contains("12 pips: 20.0%", userPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildEnhancementRequest_GivenNullOpponentProfiles_OmitsProfileSection()
    {
        var playerId = new BonesPlayerId(1);
        var priorStrategy = new BonesStrategyDocument(
            new BonesStrategyId("seat-1-v1"),
            playerId,
            PriorScriptSource);
        var matchHistories = Array.Empty<BonesMatchHistoryTranscript>();

        var request = BonesEnhancePromptBuilder.BuildEnhancementRequest(
            playerId,
            priorStrategy,
            matchHistories,
            []);

        var userPrompt = request.Messages.Single(message => message.Role == "user").Content;

        Assert.DoesNotContain(BonesEnhancePromptBuilder.OpponentProfileHeader, userPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildEnhancementRequest_GivenOpponentEffectivenessAndProfiles_IncludesBothSections()
    {
        var playerId = new BonesPlayerId(1);
        var priorStrategy = new BonesStrategyDocument(
            new BonesStrategyId("seat-1-v1"),
            playerId,
            PriorScriptSource);
        var matchHistories = Array.Empty<BonesMatchHistoryTranscript>();

        var opponentEffectiveness = new Dictionary<BonesPlayerId, BonesStrategyEffectivenessRecord>
        {
            [new BonesPlayerId(2)] = new BonesStrategyEffectivenessRecord(
                new BonesStrategyId("opp-2"),
                new BonesPlayerId(2),
                10, 5, 5, 0),
        };

        var opponentProfiles = new Dictionary<BonesPlayerId, BonesOpponentProfile>
        {
            [new BonesPlayerId(2)] = new BonesOpponentProfile
            {
                PreferredPipsFrequencies = new Dictionary<int, double> { [6] = 1.0 },
                DoublePlayFrequency = 0.0,
                PassRate = 0.0,
                TotalMovesAnalyzed = 10,
            },
        };

        var request = BonesEnhancePromptBuilder.BuildEnhancementRequest(
            playerId,
            priorStrategy,
            matchHistories,
            [],
            opponentEffectiveness: opponentEffectiveness,
            opponentProfiles: opponentProfiles);

        var userPrompt = request.Messages.Single(message => message.Role == "user").Content;

        Assert.Contains(BonesEnhancePromptBuilder.OpponentPerformanceHeader, userPrompt, StringComparison.Ordinal);
        Assert.Contains(BonesEnhancePromptBuilder.OpponentProfileHeader, userPrompt, StringComparison.Ordinal);
    }
}