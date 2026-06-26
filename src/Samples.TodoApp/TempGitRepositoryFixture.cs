using System.Diagnostics;
using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Sessions;

namespace Samples.TodoApp;

public sealed class TempGitRepositoryFixture : IAsyncDisposable
{
    private TempGitRepositoryFixture(string repositoryPath)
    {
        RepositoryPath = repositoryPath;
    }

    public string RepositoryPath { get; }

    public static async ValueTask<TempGitRepositoryFixture> CreateAsync()
    {
        var repositoryPath = Path.Combine(Path.GetTempPath(), $"modus-wip-shell-e2e-{Guid.NewGuid():N}");
        Directory.CreateDirectory(repositoryPath);

        var fixture = new TempGitRepositoryFixture(repositoryPath);
        await fixture.RunGitAsync("init", "--initial-branch=main", ".");
        await fixture.RunGitAsync("config", "user.email", "wip-shell-e2e@example.test");
        await fixture.RunGitAsync("config", "user.name", "Wip Shell E2E Tests");

        await File.WriteAllTextAsync(Path.Combine(repositoryPath, "README.md"), "base\n", CancellationToken.None);
        await File.WriteAllTextAsync(
            Path.Combine(repositoryPath, "ValidationProbe.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
                <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFramework>net10.0</TargetFramework>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <Nullable>enable</Nullable>
                </PropertyGroup>
            </Project>
            """,
            CancellationToken.None);
        await File.WriteAllTextAsync(
            Path.Combine(repositoryPath, "Program.cs"),
            "Console.WriteLine(\"validation probe\");\n",
            CancellationToken.None);
        await fixture.RunGitAsync("add", ".");
        await fixture.RunGitAsync("commit", "-m", "initial commit");

        return fixture;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (Directory.Exists(RepositoryPath))
            {
                Directory.Delete(RepositoryPath, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup only.
        }

        await ValueTask.CompletedTask;
    }

    public ValueTask<string> GetHeadCommitAsync()
        => RunGitAsync("rev-parse", "--verify", "HEAD");

    public async ValueTask MarkPersistedSessionStateAsync(SessionId sessionId, SessionState state)
    {
        var statePath = Path.Combine(RepositoryPath, ".wip", "sessions", sessionId.Value, "session-state.json");
        if (!File.Exists(statePath))
        {
            throw new InvalidOperationException(
                $"Expected persisted session state at '{statePath}', but no state file was found.");
        }

        var json = await File.ReadAllTextAsync(statePath, CancellationToken.None);
        var previousToken = "\"State\":\"Approved\"";
        var nextToken = $"\"State\":\"{state}\"";

        if (!json.Contains(previousToken, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Expected persisted state token '{previousToken}' in '{statePath}'. Actual payload: {json}");
        }

        json = json.Replace(previousToken, nextToken, StringComparison.Ordinal);
        await File.WriteAllTextAsync(statePath, json, CancellationToken.None);
    }

    public async ValueTask<string> RunGitAsync(params string[] args)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = RepositoryPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start git process.");

        var stdOutTask = process.StandardOutput.ReadToEndAsync();
        var stdErrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(CancellationToken.None);
        var stdOut = (await stdOutTask).Trim();
        var stdErr = (await stdErrTask).Trim();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Git command failed ({string.Join(" ", args)}). ExitCode={process.ExitCode}. StdOut={stdOut}. StdErr={stdErr}");
        }

        return stdOut;
    }
}