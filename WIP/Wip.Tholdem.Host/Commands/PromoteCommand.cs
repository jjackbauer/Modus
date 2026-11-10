using Wip.Tholdem.Agents.Learning;

namespace Wip.Tholdem.Host;

/// <summary>
/// Re-verifies a persisted CLI evaluation and, only with explicit confirmation, moves the champion
/// pointer. The verifier is always consulted: a policy verdict the report does not justify is
/// refused, and nothing is ever merged or deployed.
/// </summary>
internal static class PromoteCommand
{
    public const string ConfirmationMessage = "promotion requires explicit confirmation; re-run with --yes";

    public static int Execute(CliOptions options, TextWriter output)
    {
        var challengerId = CliPaths.SafeStrategyId(options.Require("challenger"));
        var dataDirectory = CliPaths.ResolveDataDirectory(options);
        var incumbentId = options.Get("incumbent") is { Length: > 0 } configuredIncumbent
            ? CliPaths.SafeStrategyId(configuredIncumbent)
            : StrategyArtifactStore.ReadChampionStrategyId(dataDirectory);
        var confirmed = options.Flag("yes");

        var path = EvaluationFileStore.PathFor(dataDirectory, challengerId, incumbentId);
        var evaluation = EvaluationFileStore.Read(path);

        var decision = new StatisticalPromotionPolicy().Decide(evaluation.Report, evaluation.Criteria);
        var verified = PromotionVerifier.Verify(
            decision,
            evaluation.Report,
            evaluation.Criteria,
            out var reason);

        if (!confirmed)
        {
            output.WriteLine(ConfirmationMessage);
        }

        if (!verified || !decision.Promoted)
        {
            output.WriteLine($"promotion refused: {decision.Reason}");
            output.WriteLine($"verifier: {reason}");
            return TholdemCli.ExitRefused;
        }

        if (!confirmed)
        {
            return TholdemCli.ExitRefused;
        }

        var pointerPath = StrategyArtifactStore.WriteChampionPointer(dataDirectory, challengerId);
        output.WriteLine($"promoted: {challengerId} replaces {incumbentId}");
        output.WriteLine($"champion pointer: {pointerPath}");
        return TholdemCli.ExitOk;
    }
}
