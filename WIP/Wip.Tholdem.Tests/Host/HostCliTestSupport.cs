using System.Globalization;
using System.Text;
using Wip.Tholdem.Agents.Learning;
using Wip.Tholdem.Host;

namespace Wip.Tholdem.Tests.Host;

internal sealed record CliResult(int ExitCode, string Output, string Error);

/// <summary>
/// Drives <c>TholdemCli.Run</c> directly and keeps every artifact inside the test output directory,
/// never in <c>%TEMP%</c>.
/// </summary>
internal static class HostCliTestSupport
{
    public static string CreateDataDirectory(string prefix)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "test-temp", prefix + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    public static CliResult Run(params string[] arguments)
    {
        var output = new StringWriter(CultureInfo.InvariantCulture);
        var error = new StringWriter(CultureInfo.InvariantCulture);
        var exitCode = TholdemCli.Run(arguments, output, error);
        return new CliResult(exitCode, output.ToString(), error.ToString());
    }

    public static string[] SessionDirectories(string dataDirectory)
    {
        var sessionsRoot = Path.Combine(dataDirectory, "sessions");
        return Directory.Exists(sessionsRoot)
            ? Directory.GetDirectories(sessionsRoot).OrderBy(static path => path, StringComparer.Ordinal).ToArray()
            : Array.Empty<string>();
    }

    public static string WriteStrategy(string dataDirectory, StrategyArtifact artifact)
    {
        var directory = Path.Combine(dataDirectory, "strategies");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, artifact.Id + ".json");
        File.WriteAllText(path, artifact.ToCanonicalJson(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    public static string WriteChampionPointer(string dataDirectory, string strategyId, int iteration = 0)
    {
        Directory.CreateDirectory(dataDirectory);
        var path = Path.Combine(dataDirectory, "active-champion.json");
        var json = "{\"strategyId\":\"" + strategyId + "\",\"iteration\":"
            + iteration.ToString(CultureInfo.InvariantCulture) + "}";
        File.WriteAllText(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    public static string WriteCliEvaluation(
        string dataDirectory,
        EvaluationReport report,
        double marginBbPer100,
        int minSeeds)
    {
        var directory = Path.Combine(dataDirectory, "evaluations");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(
            directory,
            "cli-" + report.ChallengerId + "-vs-" + report.IncumbentId + ".json");
        var json = "{\"schema\":\"tholdem.cli-evaluation.v1\",\"criteria\":{\"marginBbPer100\":"
            + Number(marginBbPer100)
            + ",\"minSeeds\":"
            + minSeeds.ToString(CultureInfo.InvariantCulture)
            + "},\"report\":"
            + report.ToCanonicalJson()
            + "}";
        File.WriteAllText(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    public static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);
}
