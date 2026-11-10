namespace Wip.Tholdem.Agents.Learning.Refinement;

/// <summary>
/// How an opt-in training run starts from a prior data directory: <see cref="None"/> is the legacy
/// fresh run, <see cref="Frozen"/> loads the source state and never writes memory, and
/// <see cref="Updating"/> loads the source state and keeps recording memory entries.
/// </summary>
public enum HarnessBootstrapMode
{
    None,
    Frozen,
    Updating,
}
