using System.Text.Json;
using Wip.Abstractions.Chat;
using Wip.Tholdem.Agents.Runtime;
using Wip.Tholdem.Engine;
using Wip.Tholdem.Tests.Runtime;
using Xunit;

namespace Wip.Tholdem.Tests.ModelHarness;

/// <summary>
/// Offline proof of the Ponder tool path: a scripted chat model (a test double, not a live claim)
/// asks the real runtime pipeline for pot odds through the real invoker, and the second request
/// carries the real tool result before a legal scripted action is returned. No network and no model
/// is reached.
/// </summary>
public sealed class PonderToolPathTests
{
    private static readonly string[] ExpectedTools =
    [
        "equity-calculator",
        "pot-odds",
        "hand-history-query",
        "opponent-statistics",
    ];

    [Fact]
    public async Task Ponder_GivenToolCallingModel_ExpectedToolResultAndLegalAction()
    {
        var observation = RuntimeAgentTestSupport.Observation();
        Assert.Contains(ActionKind.Fold, observation.LegalActionKinds);

        var model = new ScriptedChatModel(
            RuntimeAgentTestSupport.ToolTurn(
                RuntimeAgentTestSupport.CreateToolCall(
                    "call-pot-odds",
                    "pot-odds",
                    "{\"pot\":100,\"amountToCall\":25}")),
            ChatMessage.Assistant("{\"action\":\"fold\"}"));
        using var pipeline = RuntimePipeline.Create(model, new TholdemAgentOptions(ModelId: "scripted"));

        var action = await pipeline.Agent.ActAsync(
            observation,
            RuntimeAgentTestSupport.Context(observation),
            CancellationToken.None);

        Assert.Equal(2, model.Requests.Count);

        var toolMessage = Assert.Single(
            model.Requests[1].Messages,
            message => message.Role == ChatRole.Tool);
        using var resultDocument = JsonDocument.Parse(toolMessage.Content);
        var result = resultDocument.RootElement;
        Assert.Equal(0.2, result.GetProperty("breakEvenEquity").GetDouble());
        Assert.Equal(25L, result.GetProperty("callAmount").GetInt64());
        Assert.Equal(0.2, result.GetProperty("potOdds").GetDouble());

        Assert.Equal(PokerAction.Fold(), action);
        Assert.Contains(action.Kind, observation.LegalActionKinds);
        Assert.Empty(pipeline.Agent.Violations);

        foreach (var request in model.Requests)
        {
            Assert.Equal(
                ExpectedTools,
                request.Tools.Select(static definition => definition.Name).ToArray());
        }
    }
}
