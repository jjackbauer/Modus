using Wip.Bones.Identifiers;

namespace Wip.Bones.Engine;

public sealed class BonesCompiledStrategy
{
    internal BonesCompiledStrategy(BonesStrategyId strategyId, string sourceHash, IBonesPlayerSlot playerSlot)
    {
        StrategyId = strategyId;
        SourceHash = sourceHash;
        PlayerSlot = playerSlot;
    }

    public BonesStrategyId StrategyId { get; }

    public string SourceHash { get; }

    public IBonesPlayerSlot PlayerSlot { get; }
}
