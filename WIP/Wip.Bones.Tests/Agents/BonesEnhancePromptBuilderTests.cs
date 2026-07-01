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
}