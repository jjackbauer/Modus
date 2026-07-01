using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Identifiers;
using Wip.Bones.Agents.Knowledge;
using Wip.Bones.Domain;
using Wip.Bones.Engine;
using Wip.Bones.Identifiers;

namespace Wip.Bones.Agents.Play;

public enum BonesStrategyPlayerSlotDispatchKind
{
    Script,
    MarkdownLlm,
    FirstLegalMoveFallback,
}

public sealed record BonesStrategyPlayerSlotFactoryContext(
    CapabilityContext CapabilityContext,
    string ModelId = "bones-play-turn");

public sealed class BonesStrategyPlayerSlotFactory
{
    private readonly BonesStrategyScriptHost _scriptHost;
    private readonly IModelProvider<BonesPlayTurnRequest, BonesPlayTurnResult>? _playModelProvider;

    public BonesStrategyPlayerSlotFactory(
        BonesStrategyScriptHost? scriptHost = null,
        IModelProvider<BonesPlayTurnRequest, BonesPlayTurnResult>? playModelProvider = null)
    {
        _scriptHost = scriptHost ?? new BonesStrategyScriptHost();
        _playModelProvider = playModelProvider;
    }

    public BonesStrategyPlayerSlotDispatchKind ResolveDispatchKind(BonesStrategyArtifact strategy)
    {
        if (strategy.Kind == BonesStrategyKind.Script)
            return BonesStrategyPlayerSlotDispatchKind.Script;

        return _playModelProvider is null
            ? BonesStrategyPlayerSlotDispatchKind.FirstLegalMoveFallback
            : BonesStrategyPlayerSlotDispatchKind.MarkdownLlm;
    }

    public IBonesPlayerSlot CreatePlayerSlot(
        BonesStrategyArtifact strategy,
        BonesStrategyPlayerSlotFactoryContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(strategy);

        if (strategy.Kind == BonesStrategyKind.Script)
        {
            return new BonesScriptStrategyPlayerSlot(
                _scriptHost,
                strategy.StrategyId,
                strategy.Source);
        }

        if (_playModelProvider is not null && context is not null)
        {
            var strategyDocument = new BonesStrategyDocument(
                strategy.StrategyId,
                strategy.PlayerId,
                strategy.Source);

            return new BonesMarkdownStrategyPlayerSlot(
                strategyDocument,
                _playModelProvider,
                context.CapabilityContext,
                context.ModelId);
        }

        return new BonesFirstLegalMovePlayerSlot();
    }
}