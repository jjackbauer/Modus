using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Samples.TodoApp;
using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Sessions;
using Wip.Artifacts.Local;
using Wip.Bones.Agents.Enhance;
using Wip.Bones.Agents.Knowledge;
using Wip.Bones.Agents.Observe;
using Wip.Bones.Agents.Play;
using Wip.Bones.Agents.Ponder;
using Wip.Bones.Agents.Viewer;
using Wip.Bones.Agents.Workflow;
using Wip.Bones.Domain;
using Wip.Bones.Identifiers;
using Wip.ShellHost.Hosting;
using Xunit;

namespace Wip.Bones.E2E.Tests.E2E;

public sealed class BonesLearningShellE2EHarnessTests
{
    private const string ChecklistItem =
        "Add `Wip.Bones.E2E.Tests` shell transcript harness executing `init`, `use workflow workflow.bones.learning`, `session start`, `plan`, `run` and asserting stdout markers for each stage plus persisted artifacts for observe/play/ponder/enhance [depends on workflow wiring] [mandatory - E2E verification]";

    private const string BonesE2EEnvironmentVariable = "WIP_BONES_E2E";

    private const int GameCount = 1;
    private const int Seed = 4242;
    private const int TargetScore = 10;
    private static readonly BonesPlayerId LearningPlayer = new(1);

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task BonesLearningE2E_GivenShellTranscript_ExpectedWorkflowStagesReportedInStdout()
    {
        await using var run = await ExecuteLearningWorkflowTranscriptAsync();

        AssertSuccessfulRun(run);
        Assert.Equal(2, Regex.Matches(run.Result.StdOut, "- Run state=Editing transition=(applied|skipped)", RegexOptions.CultureInvariant).Count);
        Assert.Equal(2, Regex.Matches(run.Result.StdOut, "- Plan state=Editing transition=(applied|skipped)", RegexOptions.CultureInvariant).Count);

        var runArtifact = await ReadAgentRunResultArtifactAsync(run.RepositoryPath, run.SessionId);
        var stages = runArtifact.RootElement.GetProperty("Stages").EnumerateArray().ToArray();
        Assert.Equal(4, stages.Length);

        AssertStageCapability(stages[0], "Run", BonesObserveGamesCapability.Id.Value, typeof(BonesObserveGamesRequest).FullName, typeof(BonesObserveGamesResult).FullName);
        AssertStageCapability(stages[1], "Plan", BonesPonderCapability.Id.Value, typeof(BonesPonderRequest).FullName, typeof(BonesPonderResult).FullName);
        AssertStageCapability(stages[2], "Run", BonesPlayMatchCapability.Id.Value, typeof(BonesPlayMatchRequest).FullName, typeof(BonesPlayMatchResult).FullName);
        AssertStageCapability(stages[3], "Plan", BonesEnhanceStrategyCapability.Id.Value, typeof(BonesEnhanceStrategyRequest).FullName, typeof(BonesEnhanceStrategyResult).FullName);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task BonesLearningE2E_GivenCompletedRun_ExpectedStrategyAndMatchArtifactsExistOnDisk()
    {
        await using var run = await ExecuteLearningWorkflowTranscriptAsync();

        AssertSuccessfulRun(run);

        var artifactDirectory = await GetArtifactDirectoryAsync(run.RepositoryPath, run.SessionId);
        var artifactFiles = Directory
            .EnumerateFiles(artifactDirectory, "*", SearchOption.AllDirectories)
            .Where(static path => !path.EndsWith(".metadata.json", StringComparison.OrdinalIgnoreCase)
                && !path.EndsWith(".descriptor.json", StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFileName)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .ToArray();

        Assert.Contains(artifactFiles, static name => name!.Contains("-strategy-", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(artifactFiles, static name => name!.Contains("-observation-", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(artifactFiles, static name => name!.Contains("bones-player-", StringComparison.OrdinalIgnoreCase)
            && name.Contains("-match-", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(artifactFiles, static name => name!.Contains(BonesViewerMarkers.ViewerUrlArtifactFileName, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task BonesLearningE2E_GivenFourAgents_ExpectedDistinctStrategyArtifactPathsInTranscript()
    {
        await using var run = await ExecuteLearningWorkflowTranscriptAsync();

        AssertSuccessfulRun(run);

        var artifactDirectory = await GetArtifactDirectoryAsync(run.RepositoryPath, run.SessionId);
        var strategyFileNames = Directory
            .EnumerateFiles(artifactDirectory, "bones-player-*-strategy-*", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .ToArray();

        var seats = strategyFileNames
            .Select(name => Regex.Match(name!, @"bones-player-(\d+)-strategy-", RegexOptions.CultureInvariant))
            .Where(match => match.Success)
            .Select(match => int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))
            .Distinct()
            .Order()
            .ToArray();

        Assert.Equal(BonesTableConfig.FixedPlayerCount, seats.Length);
        Assert.Equal(Enumerable.Range(BonesPlayerId.MinSeat, BonesTableConfig.FixedPlayerCount), seats);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task BonesLearningE2E_GivenPlayStage_ExpectedStdoutContainsLegalMovePromptEvidence()
    {
        await using var run = await ExecuteLearningWorkflowTranscriptAsync();

        AssertSuccessfulRun(run);

        var artifactDirectory = await GetArtifactDirectoryAsync(run.RepositoryPath, run.SessionId);
        var matchTranscriptPath = Directory
            .EnumerateFiles(artifactDirectory, "bones-play-match-transcript-*.md", SearchOption.TopDirectoryOnly)
            .Single(static path => !path.EndsWith(".descriptor.json", StringComparison.OrdinalIgnoreCase));

        var transcriptMarkdown = await File.ReadAllTextAsync(matchTranscriptPath, CancellationToken.None);
        var combinedEvidence = run.Result.StdOut + transcriptMarkdown;

        Assert.True(
            combinedEvidence.Contains("AllowedMoves", StringComparison.OrdinalIgnoreCase)
            || combinedEvidence.Contains("Allowed moves", StringComparison.OrdinalIgnoreCase)
            || Regex.IsMatch(combinedEvidence, @"Turn \d+: seat \d+ played .+ on (Left|Right)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase),
            "Expected stdout or match transcript to contain legal-move prompt or played-move enumeration evidence.");
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task BonesLearningE2E_GivenEnhanceStage_ExpectedStdoutOrArtifactShowsStrategyVersionIncrement()
    {
        await using var run = await ExecuteLearningWorkflowTranscriptAsync();

        AssertSuccessfulRun(run);

        var artifactDirectory = await GetArtifactDirectoryAsync(run.RepositoryPath, run.SessionId);
        var enhancedStrategyPath = Directory
            .EnumerateFiles(artifactDirectory, "bones-player-1-strategy-initial-seat-1-v2*.md", SearchOption.TopDirectoryOnly)
            .Single();

        var markdown = await File.ReadAllTextAsync(enhancedStrategyPath, CancellationToken.None);
        Assert.Contains("## Enhancements", markdown, StringComparison.Ordinal);
        Assert.Contains("Refined tactics based on match outcome review", markdown, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task BonesLearningE2E_GivenFullScript_ExpectedExitCodeZeroAndSessionStateEditingOrCheckpointed()
    {
        await using var run = await ExecuteLearningWorkflowTranscriptAsync();

        AssertSuccessfulRun(run);
        Assert.Contains("Plan generated: workflow=workflow.bones.learning state=Editing", run.SetupStdOut, StringComparison.Ordinal);
        Assert.Contains("Active session state: Editing", run.Result.StdOut, StringComparison.Ordinal);

        using var sessionState = await ReadPersistedSessionStateAsync(run.RepositoryPath, run.SessionId);
        var state = sessionState.RootElement.GetProperty("State").GetString();
        Assert.True(
            string.Equals(state, SessionState.Editing.ToString(), StringComparison.Ordinal)
            || string.Equals(state, SessionState.Checkpointed.ToString(), StringComparison.Ordinal),
            $"Expected Editing or Checkpointed session state, but was '{state}'.");
    }

    private static void AssertSuccessfulRun(LearningWorkflowTranscriptRun run)
    {
        Assert.Equal(0, run.Result.ExitCode);
        Assert.DoesNotContain("Run failed:", run.Result.StdOut, StringComparison.Ordinal);
        Assert.Contains("Workflow executed: workflow.bones.learning", run.Result.StdOut, StringComparison.Ordinal);
    }

    private static async Task<LearningWorkflowTranscriptRun> ExecuteLearningWorkflowTranscriptAsync()
    {
        var fixture = await TempGitRepositoryFixture.CreateAsync();
        var taskDescription = BonesLearningWorkflowParameters.FormatTaskDescription(
            GameCount,
            Seed,
            TargetScore,
            LearningPlayer);

        var setupRun = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                "init",
                "use workflow workflow.bones.learning",
                $"session start \"{taskDescription}\"",
                "plan",
                "exit"
            ],
            CancellationToken.None,
            workingDirectory: fixture.RepositoryPath,
            environmentVariables: CreateBonesE2EEnvironment());

        Assert.Equal(0, setupRun.ExitCode);
        var sessionId = ExtractSessionId(setupRun.StdOut);
        await SeedOpponentStrategiesAsync(fixture.RepositoryPath, sessionId);

        var run = await ShellHostProcessDriver.ExecuteScriptAsync(
            [
                $"session attach {sessionId.Value}",
                "run",
                "artifacts",
                "exit"
            ],
            CancellationToken.None,
            workingDirectory: fixture.RepositoryPath,
            environmentVariables: CreateBonesE2EEnvironment());

        return new LearningWorkflowTranscriptRun(fixture, sessionId, setupRun.StdOut, run);
    }

    private static IReadOnlyDictionary<string, string?> CreateBonesE2EEnvironment()
        => new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [BonesE2EEnvironmentVariable] = "1"
        };

    private static async Task SeedOpponentStrategiesAsync(string repositoryPath, SessionId sessionId)
    {
        var artifactStore = new WipArtifactStoreLocal(repositoryPath);
        var knowledgeStore = new BonesPlayerKnowledgeStore(artifactStore, repositoryPath, sessionId);

        foreach (var seat in new[] { 2, 3, 4 })
        {
            var playerId = new BonesPlayerId(seat);
            await knowledgeStore.SaveStrategy(
                playerId,
                new BonesStrategyDocument(
                    new BonesStrategyId($"initial-seat-{seat}-v1"),
                    playerId,
                    markdown: $"""
                        # Seat {seat} Strategy

                        ## Rules
                        - Prefer first legal move from GetLegalMoves.
                        - Pass only when no playable tile matches open ends.
                        """),
                CancellationToken.None);
        }
    }

    private static void AssertStageCapability(
        JsonElement stage,
        string stageKind,
        string producerCapabilityId,
        string requestContractName,
        string resultContractName)
    {
        Assert.Equal(stageKind, stage.GetProperty("Stage").GetString());
        Assert.Equal(producerCapabilityId, stage.GetProperty("ProducerCapabilityId").GetString());
        Assert.Equal(requestContractName, stage.GetProperty("RequestContractName").GetString());
        Assert.Equal(resultContractName, stage.GetProperty("ResultContractName").GetString());
        Assert.False(string.IsNullOrWhiteSpace(stage.GetProperty("StoragePath").GetString()));
    }

    private static async Task<JsonDocument> ReadAgentRunResultArtifactAsync(string repositoryPath, SessionId sessionId)
    {
        var artifactDirectory = await GetArtifactDirectoryAsync(repositoryPath, sessionId);
        var runArtifactPath = Directory
            .EnumerateFiles(artifactDirectory, "agent-run-result-*.json", SearchOption.TopDirectoryOnly)
            .Single(static path => !path.EndsWith(".metadata.json", StringComparison.OrdinalIgnoreCase));

        return JsonDocument.Parse(await File.ReadAllTextAsync(runArtifactPath, CancellationToken.None));
    }

    private static async Task<JsonDocument> ReadStageExecutionArtifactAsync(
        string repositoryPath,
        SessionId sessionId,
        string producerCapabilityId)
    {
        var artifactDirectory = await GetArtifactDirectoryAsync(repositoryPath, sessionId);
        foreach (var path in Directory.EnumerateFiles(artifactDirectory, "stage-execution-*.json", SearchOption.TopDirectoryOnly))
        {
            if (path.EndsWith(".descriptor.json", StringComparison.OrdinalIgnoreCase))
                continue;

            var payload = await File.ReadAllTextAsync(path, CancellationToken.None);
            using var document = JsonDocument.Parse(payload);
            if (string.Equals(
                    document.RootElement.GetProperty("producerCapabilityId").GetString(),
                    producerCapabilityId,
                    StringComparison.Ordinal))
            {
                return JsonDocument.Parse(payload);
            }
        }

        throw new InvalidOperationException(
            $"Could not locate stage execution artifact for producer '{producerCapabilityId}'.");
    }

    private static async Task<string> GetArtifactDirectoryAsync(string repositoryPath, SessionId sessionId)
    {
        using var sessionState = await ReadPersistedSessionStateAsync(repositoryPath, sessionId);
        return sessionState.RootElement.GetProperty("ArtifactDirectory").GetString()
            ?? throw new InvalidOperationException("Persisted session state did not contain an artifact directory.");
    }

    private static async Task<JsonDocument> ReadPersistedSessionStateAsync(string repositoryPath, SessionId sessionId)
    {
        var statePath = Path.Combine(repositoryPath, ".wip", "sessions", sessionId.Value, "session-state.json");
        return JsonDocument.Parse(await File.ReadAllTextAsync(statePath, CancellationToken.None));
    }

    private static SessionId ExtractSessionId(string stdOut)
    {
        const string startedPrefix = "Session started: ";
        const string attachedPrefix = "Session attached: ";

        using var reader = new StringReader(stdOut);
        while (reader.ReadLine() is { } line)
        {
            foreach (var prefix in new[] { startedPrefix, attachedPrefix })
            {
                var markerIndex = line.IndexOf(prefix, StringComparison.Ordinal);
                if (markerIndex < 0)
                    continue;

                var remainder = line[(markerIndex + prefix.Length)..].TrimStart();
                var separatorIndex = remainder.IndexOf(' ');
                var value = separatorIndex >= 0 ? remainder[..separatorIndex] : remainder;
                return new SessionId(value);
            }
        }

        throw new InvalidOperationException($"Could not locate session id in shell output. StdOut: {stdOut}");
    }

    private sealed record ShellProcessResult(int ExitCode, string StdOut, string StdErr);

    private sealed class LearningWorkflowTranscriptRun : IAsyncDisposable
    {
        private readonly TempGitRepositoryFixture _fixture;

        public LearningWorkflowTranscriptRun(
            TempGitRepositoryFixture fixture,
            SessionId sessionId,
            string setupStdOut,
            ShellProcessResult result)
        {
            _fixture = fixture;
            SessionId = sessionId;
            SetupStdOut = setupStdOut;
            Result = result;
        }

        public string RepositoryPath => _fixture.RepositoryPath;

        public SessionId SessionId { get; }

        public string SetupStdOut { get; }

        public ShellProcessResult Result { get; }

        public ValueTask DisposeAsync() => _fixture.DisposeAsync();
    }

    private static class ShellHostProcessDriver
    {
        public static async Task<ShellProcessResult> ExecuteScriptAsync(
            IReadOnlyList<string> commands,
            CancellationToken cancellationToken,
            string? workingDirectory = null,
            IReadOnlyDictionary<string, string?>? environmentVariables = null)
        {
            var shellHostAssemblyPath = typeof(WipShellHost).Assembly.Location;
            var startInfo = new ProcessStartInfo("dotnet")
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = workingDirectory ?? Path.GetDirectoryName(shellHostAssemblyPath)
                    ?? throw new InvalidOperationException("Unable to resolve shell host working directory.")
            };
            startInfo.ArgumentList.Add(shellHostAssemblyPath);

            if (environmentVariables is not null)
            {
                foreach (var variable in environmentVariables)
                    startInfo.Environment[variable.Key] = variable.Value ?? string.Empty;
            }

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Failed to start shell host process.");

            foreach (var command in commands)
                await process.StandardInput.WriteLineAsync(command);

            process.StandardInput.Close();

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(2));

            var stdOutTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stdErrTask = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);

            return new ShellProcessResult(
                process.ExitCode,
                await stdOutTask,
                await stdErrTask);
        }
    }
}
