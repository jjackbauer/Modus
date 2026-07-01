using Wip.Bones.Domain;
using Wip.Bones.Identifiers;

namespace Wip.Bones.Agents.Delegation;

/// <summary>
/// A sub-agent that can be invoked to make deeper move decisions than the main heuristic.
/// Modeled as an abstraction so actual LLM integration can be added later.
/// </summary>
public interface IBonesSubAgent
{
    /// <summary>
    /// Decide which move to play given a delegation request with extended context.
    /// </summary>
    Task<BonesSubAgentResult> DecideAsync(
        BonesDelegationRequest request,
        CancellationToken cancellationToken);
}
