using Wip.Bones.Domain;
using Wip.Bones.Identifiers;

namespace Wip.Bones.Engine;

public sealed record BonesScriptStrategyExecutionWarning(
    BonesStrategyId StrategyId,
    BonesMove RejectedMove,
    BonesMove AppliedMove,
    string Message);

public interface IBonesScriptStrategyExecutionWarningSink
{
    void Emit(BonesScriptStrategyExecutionWarning warning);
}
