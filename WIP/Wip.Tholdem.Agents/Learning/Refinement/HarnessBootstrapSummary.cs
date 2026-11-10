namespace Wip.Tholdem.Agents.Learning.Refinement;

/// <summary>
/// The data-only record of one opt-in bootstrap run: the mode, the resolved source directory, the
/// loaded champion, the verified source lineage length, and the memory content hash before and after
/// the run plus whether the run was allowed to write memory. It carries no clock and no random value.
/// </summary>
public sealed record HarnessBootstrapSummary(
    HarnessBootstrapMode Mode,
    string SourceDirectory,
    string ChampionId,
    int LineageCount,
    string MemoryHashBefore,
    string MemoryHashAfter,
    bool MemoryWritable);
