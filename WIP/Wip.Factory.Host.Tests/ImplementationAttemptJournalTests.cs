using System.Text.Json;
using Wip.Factory.Core.Io;
using Wip.Factory.Host.Factory;
using Xunit;

namespace Wip.Factory.Host.Tests;

/// <summary>
/// The original acceptance failure replaced the first agent failure with a retry provisioning failure. These cases
/// prove the attempt journal keeps both facts separately, links them, and fails closed on interior corruption.
/// </summary>
public sealed class ImplementationAttemptJournalTests
{
    [Fact]
    public async Task Journal_GivenOriginalAndRetryFailures_PreservesBothSeparately()
    {
        using var root = new TempRoot();
        var journal = new ImplementationAttemptJournal(ImplementationAttemptJournal.PathFor(root.Path, "W-1"));

        var original = await journal.AppendAsync(Record("W-1", "implementation-agent-failed:model-unavailable", "provider refused"));
        var retry = await journal.AppendAsync(Record("W-1", "worktree-failed:worktree-partial", "branch already exists"));

        var records = journal.ReadAll();

        Assert.Equal(2, records.Count);
        Assert.Equal(1, original.AttemptNumber);
        Assert.Equal(ImplementationAttemptRecord.OriginalFailureRole, original.FailureRole);
        Assert.Equal(2, retry.AttemptNumber);
        Assert.Equal(ImplementationAttemptRecord.RetryFailureRole, retry.FailureRole);
        Assert.Equal(original.AttemptId, retry.PreviousAttemptId);
        Assert.Equal("implementation-agent-failed:model-unavailable", records[0].FailureCategory);
        Assert.Equal("worktree-failed:worktree-partial", records[1].FailureCategory);
    }

    [Fact]
    public async Task Journal_GivenSuccessAfterFailure_KeepsOriginalFailureAndDoesNotRewriteIt()
    {
        using var root = new TempRoot();
        var journal = new ImplementationAttemptJournal(ImplementationAttemptJournal.PathFor(root.Path, "W-2"));

        var failure = await journal.AppendAsync(Record("W-2", "implementation-agent-failed:model-unavailable", "first"));
        var success = await journal.AppendAsync(Record("W-2", outcome: ImplementationAttemptRecord.SucceededOutcome));

        var records = journal.ReadAll();

        Assert.Equal(2, records.Count);
        Assert.Equal(failure.AttemptId, records[0].AttemptId);
        Assert.Equal("implementation-agent-failed:model-unavailable", records[0].FailureCategory);
        Assert.Null(success.FailureRole);
        Assert.Equal(failure.AttemptId, success.PreviousAttemptId);
    }

    [Fact]
    public async Task Journal_GivenConcurrentAppends_NoRecordLostOrRepeated()
    {
        using var root = new TempRoot();
        var journal = new ImplementationAttemptJournal(ImplementationAttemptJournal.PathFor(root.Path, "W-3"));

        var tasks = Enumerable.Range(0, 12)
            .Select(index => Task.Run(() => journal.AppendAsync(Record("W-3", "retry-failure-" + index, "d" + index)).AsTask()))
            .ToArray();

        await Task.WhenAll(tasks);
        var records = journal.ReadAll();

        Assert.Equal(12, records.Count);
        Assert.Equal(Enumerable.Range(1, 12), records.Select(static record => record.AttemptNumber).OrderBy(static number => number));
        Assert.Equal(12, records.Select(static record => record.AttemptId).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task Journal_GivenTornTrailingRecord_RecoversCommittedPrefix()
    {
        using var root = new TempRoot();
        var journal = new ImplementationAttemptJournal(ImplementationAttemptJournal.PathFor(root.Path, "W-4"));
        await journal.AppendAsync(Record("W-4", "original-failure", "kept"));

        await File.AppendAllTextAsync(journal.Path, "{\"checksum\":\"broken\",\"payload\":", CancellationToken.None);

        var records = journal.ReadAll();
        Assert.Single(records);
        Assert.Equal("original-failure", records[0].FailureCategory);
    }

    [Fact]
    public async Task Journal_GivenInteriorCorruption_FailsClosed()
    {
        using var root = new TempRoot();
        var journal = new ImplementationAttemptJournal(ImplementationAttemptJournal.PathFor(root.Path, "W-5"));
        await journal.AppendAsync(Record("W-5", "first", "kept"));
        await journal.AppendAsync(Record("W-5", "second", "kept"));

        var lines = (await File.ReadAllTextAsync(journal.Path, CancellationToken.None)).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines[0] = lines[0].Replace("\"payload\":", "\"payload-tampered\":", StringComparison.Ordinal);
        await File.WriteAllTextAsync(journal.Path, string.Join('\n', lines) + "\n", CancellationToken.None);

        Assert.Throws<JournalCorruptionException>(() => journal.ReadAll());
    }

    [Fact]
    public async Task Runner_GivenProvisioningFailure_PersistsOriginalFailureAndRetrySeparately()
    {
        using var root = new TempRoot();
        var repository = Path.Combine(root.Path, "repository");
        Directory.CreateDirectory(repository);
        await RunGitAsync(repository, "init", "--initial-branch=main", ".");
        await RunGitAsync(repository, "config", "user.email", "attempt-tests@example.test");
        await RunGitAsync(repository, "config", "user.name", "Attempt Tests");
        await File.WriteAllTextAsync(Path.Combine(repository, "readme.txt"), "base\n", CancellationToken.None);
        await RunGitAsync(repository, "add", ".");
        await RunGitAsync(repository, "commit", "-m", "initial");
        var target = (await RunGitAsync(repository, "rev-parse", "HEAD")).Trim();

        var controlRoot = Path.Combine(root.Path, "control");
        var sessionWorktree = Path.Combine(controlRoot, "worktrees", "session-impl-w-journal");
        Directory.CreateDirectory(sessionWorktree);
        await File.WriteAllTextAsync(Path.Combine(sessionWorktree, "draft.txt"), "preserved draft", CancellationToken.None);

        var runner = new RealModelImplementationRunner(
            new FactoryControlRoot(controlRoot, repository),
            new Uri("http://127.0.0.1:9/"),
            "test-key");
        var request = new ImplementationRequest("W-journal", repository, "requirement", ["AC-1"]);

        var first = await runner.RunAsync(request, CancellationToken.None);
        var second = await runner.RunAsync(request, CancellationToken.None);

        Assert.False(first.Succeeded);
        Assert.False(second.Succeeded);
        Assert.Contains("worktree-failed:worktree-partial", first.FailureCategory, StringComparison.Ordinal);
        Assert.Contains("worktree-failed:worktree-partial", second.FailureCategory, StringComparison.Ordinal);

        var journal = new ImplementationAttemptJournal(ImplementationAttemptJournal.PathFor(controlRoot, "W-journal"));
        var records = journal.ReadAll();

        Assert.Equal(2, records.Count);
        Assert.Equal(ImplementationAttemptRecord.OriginalFailureRole, records[0].FailureRole);
        Assert.Equal(ImplementationAttemptRecord.RetryFailureRole, records[1].FailureRole);
        Assert.Equal(records[0].AttemptId, records[1].PreviousAttemptId);
        Assert.Equal(target, records[0].TargetCommit);
        Assert.Equal(target, records[1].TargetCommit);
        Assert.Equal(sessionWorktree, records[0].WorktreePath);
        Assert.Equal("preserved draft", await File.ReadAllTextAsync(Path.Combine(sessionWorktree, "draft.txt"), CancellationToken.None));
    }

    private static ImplementationAttemptRecord Record(string workItemId, string? category = null, string? detail = null, string outcome = ImplementationAttemptRecord.FailedOutcome)
        => new(
            Guid.NewGuid().ToString("N"),
            workItemId,
            0,
            "session-impl-" + workItemId.ToLowerInvariant(),
            outcome,
            category,
            null,
            detail,
            RepositoryPath: null,
            WorktreePath: null,
            TargetCommit: null,
            BaseSha: null,
            HeadSha: null,
            PreviousAttemptId: null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

    private static async Task<string> RunGitAsync(string workingDirectory, params string[] args)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        foreach (var argument in args)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = System.Diagnostics.Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start git.");
        var stdout = await process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderr = await process.StandardError.ReadToEndAsync(CancellationToken.None);
        await process.WaitForExitAsync(CancellationToken.None);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"git {string.Join(' ', args)} failed: {stderr}");
        }

        return stdout.Trim();
    }

    private sealed class TempRoot : IDisposable
    {
        public TempRoot()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "factory-attempt-journal-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
