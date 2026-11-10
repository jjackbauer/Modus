namespace Wip.Tholdem.Agents;

/// <summary>
/// The only turn metadata an agent receives besides <c>PlayerObservation</c>: the hand number, the
/// acting seat and the per-seat action index. These are public turn coordinates. The context carries
/// no seed of any width, no digest and no other secret-derived value, so nothing an agent receives
/// from it can be inverted to the session seed or to the deck. Agent randomness is derived from the
/// agent's own observation instead, never from a per-turn secret.
/// </summary>
public readonly record struct AgentTurnContext(int HandNumber, int Seat, int ActionIndex);
