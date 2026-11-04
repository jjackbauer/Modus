using Wip.Bones.Agents.Knowledge;

namespace Wip.Bones.Web.Tests;

/// <summary>
/// Mutable pool holder for web-host tests. The strategy pool is a DI service now, so tests
/// publish the pool they want the host to see through this holder instead of a process-wide static.
/// </summary>
public sealed class StrategyPoolHolder
{
    public StrategyPool? Pool { get; set; }
}