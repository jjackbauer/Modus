using System.Security.Cryptography;
using System.Text;
using Wip.Tholdem.Agents;
using Wip.Tholdem.Agents.Learning;
using Wip.Tholdem.Agents.Simulation;
using Wip.Tholdem.Engine;
using Wip.Tholdem.History;
using Xunit;

namespace Wip.Tholdem.Tests.Learning;

internal sealed record RecordedTurn(PlayerObservation Observation, AgentTurnContext Context, PokerAction Action);

/// <summary>Delegates every decision and keeps the observation/context pair it answered.</summary>
internal sealed class RecordingAgent : IPokerAgent
{
    private readonly IPokerAgent _inner;
    private readonly List<RecordedTurn> _turns = [];

    public RecordingAgent(IPokerAgent inner)
    {
        _inner = inner;
    }

    public string Name => _inner.Name;

    public IReadOnlyList<RecordedTurn> Turns => _turns;

    public async ValueTask<PokerAction> ActAsync(
        PlayerObservation observation,
        AgentTurnContext context,
        CancellationToken cancellationToken)
    {
        var action = await _inner.ActAsync(observation, context, cancellationToken);
        _turns.Add(new RecordedTurn(observation, context, action));
        return action;
    }
}

internal static class LearningTestSupport
{
    public static byte[] Utf8(string text) => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text);

    public static string Sha256Hex(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    /// <summary>
    /// A synthetic report with an exactly known lower bound, so the promotion gate can be proven
    /// independently of the simulator.
    /// </summary>
    public static EvaluationReport SyntheticReport(double lowerBound, int seeds) =>
        new(
            "challenger-x",
            "incumbent-y",
            seeds,
            4,
            10,
            lowerBound + 2.0,
            new ConfidenceInterval(lowerBound, lowerBound + 4.0),
            lowerBound,
            ChallengerWins: 2,
            IncumbentWins: 1,
            Ties: 0,
            ChallengerNetChips: 120);

    public static void AssertLegalAction(PlayerObservation observation, PokerAction action)
    {
        Assert.Contains(action.Kind, observation.LegalActionKinds);
        if (action.Kind is ActionKind.Bet or ActionKind.Raise)
        {
            Assert.InRange(action.Amount, observation.MinRaiseTo, observation.MaxRaiseTo);
        }
        else
        {
            Assert.Equal(0, action.Amount);
        }
    }
}
