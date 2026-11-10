namespace Wip.Tholdem.Agents.Learning.Refinement;

/// <summary>
/// One named arm of the ablation. The four flags are the only dimensions the runner varies; every
/// arm receives the same seed, iteration count, training hands and evaluation shape.
/// </summary>
public sealed record HarnessAblationConfiguration(
    string Name,
    bool HarnessRefinement,
    bool SkillRefinement,
    string Note);

/// <summary>
/// The fixed six-configuration table, in the canonical report order. <c>memory</c> is write-only and
/// <c>lineage</c> is read-only by design, so for the current loop both choose exactly the challengers
/// of <c>signatures</c>; the table records that as a finding rather than hiding it.
/// </summary>
public static class HarnessAblationConfigurations
{
    public const string None = "none";

    public const string Signatures = "signatures";

    public const string Memory = "memory";

    public const string Lineage = "lineage";

    public const string Skills = "skills";

    public const string All = "all";

    /// <summary>The six arms in their fixed order.</summary>
    public static IReadOnlyList<HarnessAblationConfiguration> Ordered { get; } =
        Array.AsReadOnly(
            new[]
            {
                new HarnessAblationConfiguration(
                    None,
                    HarnessRefinement: false,
                    SkillRefinement: false,
                    "legacy mode; harness refinement and skill refinement are off"),
                new HarnessAblationConfiguration(
                    Signatures,
                    HarnessRefinement: true,
                    SkillRefinement: false,
                    "harness refinement on; signature-driven proposals only"),
                new HarnessAblationConfiguration(
                    Memory,
                    HarnessRefinement: true,
                    SkillRefinement: false,
                    "memory is write-only; it never reaches the proposal, so challengers match signatures"),
                new HarnessAblationConfiguration(
                    Lineage,
                    HarnessRefinement: true,
                    SkillRefinement: false,
                    "lineage is read-only; the trainer never consults it, so challengers match signatures"),
                new HarnessAblationConfiguration(
                    Skills,
                    HarnessRefinement: true,
                    SkillRefinement: true,
                    "harness refinement on with skill edits; the policy may edit the bluff skill"),
                new HarnessAblationConfiguration(
                    All,
                    HarnessRefinement: true,
                    SkillRefinement: true,
                    "harness refinement with skill edits; memory and lineage remain observational"),
            });
}
