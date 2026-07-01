namespace Wip.Bones.Tests;

internal static class BonesScriptStrategiesRequirementsChecklistItems
{
    public const string TestsCoverage =
        "Add `Wip.Bones.Tests` coverage for script host compile success/failure, illegal move rejection, promotion accept/reject paths, and play tool LLM bypass for script seats [depends on all agent changes] [mandatory - unit and integration proof]";

    public const string DeepSeekPromptCoverage =
        "Add `Wip.Bones.ModelProviders.DeepSeek.Tests` coverage proving ponder/enhance prompts request C# `IBonesPlayerSlot` implementation [depends on prompt changes]";

    public const string HostPromotionIntegration =
        "Add `Wip.Bones.Host.Tests` integration proving one learning iteration leaves incumbent active when candidate loses evaluation, and promotes when candidate wins all evaluation matches [depends on host wiring]";

    public const string ComplianceRegistry =
        "Register `harness/requirements/Wip.Bones.ScriptStrategies.md` in `BehaviorProofComplianceRegistry` with owning assembly `Wip.Bones.Tests` [depends on test coverage]";

    public const string BehaviorProofPolicy =
        "Enforce absolute behavior-proof verification for every planned integration test [mandatory - behavior-proof policy]";
}