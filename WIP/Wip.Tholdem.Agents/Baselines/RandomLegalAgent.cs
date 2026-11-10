using Wip.Tholdem.Engine;
using Wip.Tholdem.History;

namespace Wip.Tholdem.Agents.Baselines;

/// <summary>
/// Uniform random legal action, with every choice derived from <see cref="SeedDerivation"/>.
/// </summary>
public sealed class RandomLegalAgent : IPokerAgent
{
    public RandomLegalAgent(string? name = null)
    {
        Name = string.IsNullOrWhiteSpace(name) ? "random-legal" : name;
    }

    public string Name { get; }

    public ValueTask<PokerAction> ActAsync(
        PlayerObservation observation,
        AgentTurnContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(observation);
        cancellationToken.ThrowIfCancellationRequested();

        var kinds = observation.LegalActionKinds;
        if (kinds.Count == 0)
        {
            throw new InvalidOperationException(
                $"Agent '{Name}' received an observation that offers no legal action.");
        }

        // Randomness comes from the agent's own observation (own cards plus public state) and the
        // public turn coordinates, never from caller-supplied state.
        var observationJson = observation.ToCanonicalJson();
        var kindIndex = (int)(SeedDerivation.DeriveAgentSeed(
            observationJson,
            context.HandNumber,
            context.Seat,
            context.ActionIndex,
            0UL) % (ulong)kinds.Count);
        var kind = kinds[kindIndex];

        long amount = 0;
        if (kind is ActionKind.Bet or ActionKind.Raise)
        {
            var span = observation.MaxRaiseTo - observation.MinRaiseTo + 1;
            var offset = SeedDerivation.DeriveAgentSeed(
                observationJson,
                context.HandNumber,
                context.Seat,
                context.ActionIndex,
                1UL) % (ulong)span;
            amount = observation.MinRaiseTo + (long)offset;
        }

        return ValueTask.FromResult(new PokerAction(kind, amount));
    }
}