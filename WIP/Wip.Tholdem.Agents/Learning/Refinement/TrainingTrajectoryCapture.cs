using System.Globalization;
using Wip.Tholdem.Agents.Simulation;
using Wip.Tholdem.Engine;
using Wip.Tholdem.History;

namespace Wip.Tholdem.Agents.Learning.Refinement;

/// <summary>One captured training match: the unchanged report plus the hands it recorded.</summary>
public sealed record TrajectoryCapture(SimulationReport Report, IReadOnlyList<HandRecord> Hands);

/// <summary>
/// Writes one training match through the existing <see cref="SimulationOptions.HistoryDirectory"/>,
/// reads every <c>hand-*.json</c> back through <see cref="HandRecordJson.Parse"/>, and removes the
/// scratch directory in a <c>finally</c>. The simulator's public contract is not changed.
/// </summary>
public static class TrainingTrajectoryCapture
{
    public const string HarnessDirectoryName = "harness";

    public const string TrajectoryDirectoryName = "trajectory";

    /// <summary>The bounded, iteration-addressed scratch directory a capture uses.</summary>
    public static string ScratchDirectory(string dataDirectory, int iteration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        if (iteration < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(iteration),
                iteration,
                "A trajectory capture iteration must be positive.");
        }

        return Path.Combine(
            dataDirectory,
            HarnessDirectoryName,
            TrajectoryDirectoryName,
            "iteration-" + iteration.ToString("D5", CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Runs the match into a fresh scratch directory, parses every recorded hand in hand-number
    /// order, and deletes the scratch directory whether the match succeeds or throws.
    /// </summary>
    public static TrajectoryCapture Capture(
        string dataDirectory,
        int iteration,
        SimulationOptions options,
        IPokerAgent seat0,
        IPokerAgent seat1,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        return CaptureCore(
            dataDirectory,
            iteration,
            directory => MatchSimulator.Run(
                seat0,
                seat1,
                options with { HistoryDirectory = directory },
                cancellationToken));
    }

    /// <summary>
    /// Runs one reproducible two-to-six-seat match through the same scratch-directory discipline:
    /// the hand history is written below the iteration's scratch directory, every recorded hand is
    /// read back in hand-number order, and the scratch directory is deleted whether the match
    /// succeeds or throws.
    /// </summary>
    public static TrajectoryCapture Capture(
        string dataDirectory,
        int iteration,
        SimulationOptions options,
        IReadOnlyList<IPokerAgent> agents,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agents);
        ArgumentNullException.ThrowIfNull(options);

        return CaptureCore(
            dataDirectory,
            iteration,
            directory => MatchSimulator.Run(
                agents,
                options with { HistoryDirectory = directory },
                cancellationToken));
    }

    private static TrajectoryCapture CaptureCore(
        string dataDirectory,
        int iteration,
        Func<string, SimulationReport> run)
    {
        var directory = ScratchDirectory(dataDirectory, iteration);
        try
        {
            // A previous crash may have left the same iteration address behind; never read stale hands.
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }

            Directory.CreateDirectory(directory);
            return new TrajectoryCapture(run(directory), ReadHands(directory));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static IReadOnlyList<HandRecord> ReadHands(string directory) =>
        Directory
            .GetFiles(directory, "hand-*.json")
            .OrderBy(static path => path, StringComparer.Ordinal)
            .Select(static path => HandRecordJson.Parse(File.ReadAllText(path)))
            .OrderBy(static hand => hand.HandNumber)
            .ToArray();
}
