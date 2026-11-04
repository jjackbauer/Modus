using Xunit;

namespace Wip.Bones.Tests;

/// <summary>
/// Serialized collection for tests that mutate the process-global workflow stage-map registry
/// (<c>WorkflowStageMapAdapterRuntime</c> / <c>BonesLearningWorkflowMapRuntime</c>). Resets in one
/// class raced with mappings in another under parallel execution and produced the P1-03
/// "No runtime map adapter binding exists" failure. WP-2.4 removes the global registry; until then
/// every mutator must share this non-parallel collection.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class BonesWorkflowMapRuntimeCollection
{
    public const string Name = "BonesWorkflowMapRuntime";
}