using Wip.Abstractions.Capabilities;
using Wip.Bones.Agents.Enhance;
using Wip.Bones.Agents.Play;
using Wip.Bones.Agents.Ponder;
using Wip.Bones.ModelProviders.DeepSeek;
using Wip.Runtime.Runtime;
using Xunit;

namespace Wip.Bones.ModelProviders.DeepSeek.Tests.Assembly;

public sealed class BonesDeepSeekAssemblyTests
{
    private const string ChecklistItem = BonesDeepSeekRequirementsChecklistItems.AdapterAssembly;

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BonesDeepSeekAssembly_GivenLoadedAdapterAssembly_ExpectedReportsCanonicalAssemblyName()
    {
        var assembly = typeof(BonesDeepSeekModelProvidersAssembly).Assembly;

        Assert.Equal(BonesDeepSeekModelProvidersAssembly.Name, assembly.GetName().Name);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BonesDeepSeekAssembly_GivenProjectReferenceChain_ExpectedDeepSeekModelProviderTypeResolvable()
    {
        var providerType = BonesDeepSeekModelProvidersAssembly.DeepSeekModelProviderType;

        Assert.Equal(typeof(DeepSeekModelProvider), providerType);
        Assert.True(typeof(IModelProvider<DeepSeekChatCompletionRequest, DeepSeekChatCompletionResult>).IsAssignableFrom(providerType));
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BonesDeepSeekAssembly_GivenProjectReferenceChain_ExpectedBonesModelProviderContractTypesResolvable()
    {
        Assert.Equal(typeof(BonesStrategyAuthoringRequest), BonesDeepSeekModelProvidersAssembly.StrategyAuthoringRequestType);
        Assert.Equal(typeof(BonesPlayTurnRequest), BonesDeepSeekModelProvidersAssembly.PlayTurnRequestType);
        Assert.Equal(typeof(BonesStrategyEnhancementRequest), BonesDeepSeekModelProvidersAssembly.StrategyEnhancementRequestType);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void BonesDeepSeekAssembly_GivenLoadedAdapterAssembly_ExpectedReferencesAbstractionsAgentsAndRuntime()
    {
        var referencedAssemblyNames = typeof(BonesDeepSeekModelProvidersAssembly).Assembly
            .GetReferencedAssemblies()
            .Select(static name => name.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("Wip.Abstractions", referencedAssemblyNames);
        Assert.Contains("Wip.Bones.Agents", referencedAssemblyNames);
        Assert.Contains("Wip.Runtime", referencedAssemblyNames);
    }
}