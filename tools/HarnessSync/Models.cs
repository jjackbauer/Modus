namespace HarnessSync;

public sealed record HarnessBody(
    string Name,
    string RelativePath,
    string Kind,
    string? Mode,
    string Description,
    IReadOnlyList<string> Subagents,
    IReadOnlyList<string> Composes,
    string? AssertMdc);

public sealed record SyncIssue(string Code, string Message);

public enum SyncMode
{
    Generate,
    Check
}