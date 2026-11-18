using System.Text;
using Wip.Abstractions.Capabilities;

namespace Wip.Factory.Agents.Core.Tools;

public sealed record PatchHunkRequest(string Path, string UnifiedDiff);

public sealed record PatchHunkResult(string Path, int HunkCount, int AddedLines, int RemovedLines);

/// <summary>
/// Applies a unified diff atomically (v0.3 §6.1): every hunk is matched against the current file first, and
/// if any hunk does not apply the file is left untouched. A partial patch is never committed.
/// </summary>
public sealed class ApplyPatchTool : ITool<PatchHunkRequest, PatchHunkResult>
{
    public const string ContextNotFoundCategory = "patch-context-not-found";
    public const string UnsupportedFormCategory = "patch-unsupported-form";
    public const string TargetMissingCategory = "patch-target-missing";

    public async ValueTask<PatchHunkResult> ExecuteAsync(
        PatchHunkRequest request,
        CapabilityContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var absolute = WorkspacePaths.Resolve(context.WorktreePath!, request.Path);
        if (!File.Exists(absolute))
        {
            throw new InvalidOperationException($"{TargetMissingCategory}: '{request.Path}' does not exist.");
        }

        var original = await File.ReadAllTextAsync(absolute, cancellationToken).ConfigureAwait(false);
        var (updated, hunks, added, removed) = Apply(original, request.UnifiedDiff);

        await File.WriteAllTextAsync(absolute, updated, cancellationToken).ConfigureAwait(false);

        return new PatchHunkResult(
            WorkspacePaths.Relative(context.WorktreePath!, absolute),
            hunks,
            added,
            removed);
    }

    /// <summary>Pure patch application; separated so it can be tested without a filesystem.</summary>
    public static (string Updated, int Hunks, int Added, int Removed) Apply(string original, string unifiedDiff)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(unifiedDiff);

        var source = original.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var lines = unifiedDiff.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        var output = new List<string>();
        var cursor = 0;
        var hunks = 0;
        var added = 0;
        var removed = 0;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            if (!line.StartsWith("@@", StringComparison.Ordinal))
            {
                continue; // file headers and diff noise are ignored
            }

            var header = ParseHeader(line);
            if (header is null)
            {
                throw new InvalidOperationException($"{UnsupportedFormCategory}: '{line}'.");
            }

            var (oldStart, oldCount) = header.Value;
            var target = Math.Max(0, oldStart - 1);

            if (target < cursor || target > source.Length)
            {
                throw new InvalidOperationException($"{ContextNotFoundCategory}: hunk at line {oldStart} is out of order.");
            }

            while (cursor < target)
            {
                output.Add(source[cursor++]);
            }

            var consumed = 0;
            var body = new List<string>();

            for (i++; i < lines.Length; i++)
            {
                var bodyLine = lines[i];
                if (bodyLine.StartsWith("@@", StringComparison.Ordinal))
                {
                    i--;
                    break;
                }

                if (bodyLine.Length == 0)
                {
                    continue;
                }

                var marker = bodyLine[0];
                var text = bodyLine.Length > 1 ? bodyLine[1..] : string.Empty;

                switch (marker)
                {
                    case ' ':
                        body.Add(text);
                        consumed++;
                        break;
                    case '-':
                        body.Add("-" + text);
                        consumed++;
                        removed++;
                        break;
                    case '+':
                        body.Add("+" + text);
                        added++;
                        break;
                    case '\\':
                        break;
                    default:
                        throw new InvalidOperationException($"{UnsupportedFormCategory}: '{bodyLine}'.");
                }
            }

            if (consumed != oldCount)
            {
                throw new InvalidOperationException(
                    $"{UnsupportedFormCategory}: hunk at line {oldStart} declares {oldCount} source lines but carries {consumed}.");
            }

            // Match the removed/context lines against the source before emitting anything.
            var sourceSlice = source.Skip(cursor).Take(consumed).ToArray();
            var expected = body.Where(static b => b[0] != '+').Select(static b => b[1..]).ToArray();

            if (!sourceSlice.SequenceEqual(expected, StringComparer.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{ContextNotFoundCategory}: source does not match the hunk at line {oldStart}.");
            }

            foreach (var bodyLine in body)
            {
                if (bodyLine[0] != '-')
                {
                    output.Add(bodyLine[1..]);
                }
            }

            cursor += consumed;
            hunks++;
        }

        while (cursor < source.Length)
        {
            output.Add(source[cursor++]);
        }

        return (string.Join('\n', output), hunks, added, removed);
    }

    private static (int Start, int Count)? ParseHeader(string line)
    {
        var match = System.Text.RegularExpressions.Regex.Match(line, @"^@@\s+-(\d+)(?:,(\d+))?\s+\+\d+(?:,\d+)?\s+@@");
        if (!match.Success)
        {
            return null;
        }

        var start = int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        var count = match.Groups[2].Success
            ? int.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture)
            : 1;

        return (start, count);
    }
}
