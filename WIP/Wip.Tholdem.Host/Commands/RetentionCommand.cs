using System.Globalization;
using System.Text;

namespace Wip.Tholdem.Host;

/// <summary>
/// Session-artifact retention: a read-only status report, and a preview-gated purge whose cutoff is
/// the <c>session.json</c> last-write time.
/// </summary>
internal static class RetentionCommand
{
    public static int Execute(CliOptions options, TextWriter output)
    {
        if (options.Positionals.Count != 0)
        {
            throw new CliUsageException("retention does not take positional arguments.");
        }

        var wantsStatus = options.Flag("status");
        var wantsPurge = options.Flag("purge");
        if (wantsStatus == wantsPurge)
        {
            throw new CliUsageException("retention requires exactly one of --status or --purge.");
        }

        var dataDirectory = CliPaths.ResolveDataDirectory(options);
        var sessionsRoot = Path.Combine(dataDirectory, "sessions");

        if (wantsStatus)
        {
            if (options.Flag("older-than-days") || options.Flag("yes"))
            {
                throw new CliUsageException("--older-than-days and --yes are only valid with --purge.");
            }

            var snapshot = RetentionSnapshot.Measure(sessionsRoot);
            if (options.Flag("json"))
            {
                output.WriteLine(snapshot.ToCanonicalJson());
                return TholdemCli.ExitOk;
            }

            output.WriteLine($"sessions: {snapshot.Sessions}");
            output.WriteLine($"hands: {snapshot.Hands}");
            output.WriteLine($"bytes: {snapshot.Bytes}");
            output.WriteLine(
                "oldest session: "
                + (snapshot.OldestSessionUtc is { } oldest
                    ? oldest.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture)
                    : "none"));
            return TholdemCli.ExitOk;
        }

        var olderThanDays = options.RequireInt("older-than-days", minimum: 0);
        var cutoff = DateTime.UtcNow.AddDays(-olderThanDays);
        var targets = FindExpiredSessions(sessionsRoot, cutoff);

        if (!options.Flag("yes"))
        {
            foreach (var target in targets)
            {
                output.WriteLine($"would delete: {target}");
            }

            output.WriteLine(
                $"preview: {targets.Count} session(s) older than {olderThanDays} day(s); "
                + "re-run with --yes to delete");
            return TholdemCli.ExitRefused;
        }

        foreach (var target in targets)
        {
            Directory.Delete(target, recursive: true);
            output.WriteLine($"deleted: {target}");
        }

        output.WriteLine($"purged: {targets.Count} session(s) older than {olderThanDays} day(s)");
        return TholdemCli.ExitOk;
    }

    private static IReadOnlyList<string> FindExpiredSessions(string sessionsRoot, DateTime cutoff)
    {
        if (!Directory.Exists(sessionsRoot))
        {
            return Array.Empty<string>();
        }

        return Directory
            .GetDirectories(sessionsRoot)
            .Where(directory =>
            {
                var sessionJson = Path.Combine(directory, "session.json");
                return File.Exists(sessionJson) && File.GetLastWriteTimeUtc(sessionJson) < cutoff;
            })
            .OrderBy(static directory => directory, StringComparer.Ordinal)
            .ToArray();
    }

    private sealed record RetentionSnapshot(int Sessions, int Hands, long Bytes, DateTime? OldestSessionUtc)
    {
        public static RetentionSnapshot Measure(string sessionsRoot)
        {
            if (!Directory.Exists(sessionsRoot))
            {
                return new RetentionSnapshot(0, 0, 0, null);
            }

            var sessions = Directory
                .GetDirectories(sessionsRoot)
                .Where(static directory => File.Exists(Path.Combine(directory, "session.json")))
                .ToArray();
            var hands = sessions.Sum(
                static directory => Directory.GetFiles(directory, "hand-*.json").Length);
            var bytes = Directory
                .EnumerateFiles(sessionsRoot, "*", SearchOption.AllDirectories)
                .Sum(static path => new FileInfo(path).Length);

            DateTime? oldest = null;
            foreach (var directory in sessions)
            {
                var written = File.GetLastWriteTimeUtc(Path.Combine(directory, "session.json"));
                if (oldest is null || written < oldest.Value)
                {
                    oldest = written;
                }
            }

            return new RetentionSnapshot(sessions.Length, hands, bytes, oldest);
        }

        public string ToCanonicalJson()
        {
            var builder = new StringBuilder(160);
            builder.Append("{\"schema\":\"tholdem.retention.v1\",\"sessions\":");
            builder.Append(CliJson.Number(Sessions));
            builder.Append(",\"hands\":");
            builder.Append(CliJson.Number(Hands));
            builder.Append(",\"bytes\":");
            builder.Append(CliJson.Number(Bytes));
            builder.Append(",\"oldestSession\":");
            builder.Append(
                OldestSessionUtc is { } oldest
                    ? CliJson.Text(oldest.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture))
                    : "null");
            builder.Append('}');
            return builder.ToString();
        }
    }
}
