using Wip.Tholdem.Engine;
using Wip.Tholdem.History;

namespace Wip.Tholdem.Agents;

/// <summary>
/// A heads-up poker agent. Implementations may only use <see cref="PlayerObservation"/> and
/// <see cref="AgentTurnContext"/>; both are hidden-information-safe projections. The turn context
/// carries only public turn coordinates — hand number, acting seat and per-seat action index — with
/// no seed or other secret-derived value, so an agent's inputs cannot be inverted to the session
/// seed or to the deck.
/// </summary>
public interface IPokerAgent
{
    string Name { get; }

    ValueTask<PokerAction> ActAsync(
        PlayerObservation observation,
        AgentTurnContext context,
        CancellationToken cancellationToken);
}
