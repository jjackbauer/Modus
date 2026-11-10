using Wip.Tholdem.Engine;

namespace Wip.Tholdem.Agents;

/// <summary>
/// Raised by the simulator when an agent throws or returns an action the engine rejects. The
/// simulator never substitutes a fallback action.
/// </summary>
public sealed class IllegalAgentActionException : InvalidOperationException
{
    public IllegalAgentActionException(
        string agentName,
        int handNumber,
        PokerAction action,
        string reason,
        Exception? innerException = null)
        : base($"Agent '{agentName}' produced an illegal action in hand {handNumber}: {reason}", innerException)
    {
        AgentName = agentName;
        HandNumber = handNumber;
        Action = action;
    }

    public string AgentName { get; }

    public int HandNumber { get; }

    public PokerAction Action { get; }
}