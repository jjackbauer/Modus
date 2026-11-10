using Wip.Tholdem.Agents.Learning.Refinement;

namespace Wip.Tholdem.Agents.Learning;

/// <summary>
/// The finished run: the options, every iteration in order, the surviving champion, the ledger entry
/// JSON exactly as persisted and, for an opt-in bootstrap run only, its data-only summary. A plain
/// run leaves <see cref="Bootstrap"/> null, so every existing call site keeps its exact behaviour.
/// </summary>
public sealed record TrainingRun(
    TrainingOptions Options,
    IReadOnlyList<TrainingIteration> Iterations,
    string FinalChampionId,
    IReadOnlyList<string> LedgerJson,
    HarnessBootstrapSummary? Bootstrap = null);
