using Wip.Abstractions.Capabilities;
using Wip.Bones.Agents.Enhance;
using Wip.Bones.Agents.Play;
using Wip.Bones.Agents.Ponder;
using Wip.Runtime.Runtime;

namespace Wip.Bones.ModelProviders.DeepSeek;

/// <summary>
/// Marks the DeepSeek Bones model-provider adapter assembly and its foundational dependencies.
/// </summary>
public static class BonesDeepSeekModelProvidersAssembly
{
    public const string Name = "Wip.Bones.ModelProviders.DeepSeek";

    public static Type DeepSeekModelProviderType => typeof(DeepSeekModelProvider);

    public static Type ModelProviderOpenGenericType => typeof(IModelProvider<,>);

    public static Type StrategyAuthoringRequestType => typeof(BonesStrategyAuthoringRequest);

    public static Type PlayTurnRequestType => typeof(BonesPlayTurnRequest);

    public static Type StrategyEnhancementRequestType => typeof(BonesStrategyEnhancementRequest);
}