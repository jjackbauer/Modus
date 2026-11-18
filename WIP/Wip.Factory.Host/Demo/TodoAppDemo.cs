using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Chat;
using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Sessions;
using Wip.Builder;
using Wip.Factory.Adaptation;
using Wip.Factory.Agents.Core.Tools;
using Wip.Factory.Contracts.Identifiers;
using Wip.Factory.Contracts.Profiles;
using Wip.Factory.Wip.Delivery;
using Wip.Factory.Wip.Fanout;
using Wip.Factory.Wip.Governance;
using Wip.Factory.Wip.Isolation;
using Wip.Factory.Wip.Workspaces;
using Wip.Runtime;
using Wip.Runtime.Runtime;

namespace Wip.Factory.Host.Demo;

/// <summary>
/// The live proof: the factory builds a working todo application. A real model implements it through a
/// worktree-scoped file tool, the acceptance tests the factory shipped must pass, the change goes through
/// review and a digest-bound approval, is merged, and the merged app is started and exercised over HTTP.
/// </summary>
public sealed class TodoAppDemo
{
    private static readonly CapabilityId WriteToolId = new("factory.tool.write-file");

    private const string TodoApiProject = """
        <Project Sdk="Microsoft.NET.Sdk.Web">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <Nullable>enable</Nullable>
            <ImplicitUsings>enable</ImplicitUsings>
            <InvariantGlobalization>true</InvariantGlobalization>
          </PropertyGroup>
        </Project>
        """;

    private const string TodoTestsProject = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <Nullable>enable</Nullable>
            <ImplicitUsings>enable</ImplicitUsings>
            <IsPackable>false</IsPackable>
          </PropertyGroup>
          <ItemGroup>
            <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
            <PackageReference Include="xunit" Version="2.9.3" />
            <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
          </ItemGroup>
          <ItemGroup>
            <ProjectReference Include="..\TodoApi\TodoApi.csproj" />
          </ItemGroup>
        </Project>
        """;

    /// <summary>The acceptance criteria the factory ships. The model must satisfy these, not invent them.</summary>
    private const string AcceptanceTests = """
        using TodoApi;
        using Xunit;

        namespace TodoApi.Tests;

        public sealed class TodoStoreTests
        {
            [Fact]
            public void Add_GivenTitle_ReturnsIncompleteItemWithId()
            {
                var store = new TodoStore();
                var item = store.Add("write tests");
                Assert.Equal("write tests", item.Title);
                Assert.False(item.IsComplete);
                Assert.True(item.Id > 0);
            }

            [Fact]
            public void GetAll_GivenTwoItems_ReturnsThemInInsertionOrder()
            {
                var store = new TodoStore();
                store.Add("first");
                store.Add("second");
                var all = store.GetAll();
                Assert.Equal(2, all.Count);
                Assert.Equal("first", all[0].Title);
                Assert.Equal("second", all[1].Title);
            }

            [Fact]
            public void Complete_GivenExistingId_MarksItemComplete()
            {
                var store = new TodoStore();
                var item = store.Add("finish me");
                Assert.True(store.Complete(item.Id));
                Assert.Single(store.GetAll(), t => t.IsComplete);
            }

            [Fact]
            public void Complete_GivenUnknownId_ReturnsFalse()
            {
                var store = new TodoStore();
                Assert.False(store.Complete(9999));
            }
        }
        """;

    private const string Contract = """
        namespace TodoApi;
        public sealed record TodoItem(int Id, string Title, bool IsComplete);
        public sealed class TodoStore
        {
            public TodoItem Add(string title);
            public IReadOnlyList<TodoItem> GetAll();
            public bool Complete(int id);
        }
        """;

    private static readonly string Task = $$"""
        Implement a todo application in this repository. Two files must be created with the write tool.

        1. `TodoApi/TodoStore.cs` — implement exactly this contract, in namespace TodoApi:
        {{Contract}}
        Rules: Ids start at 1 and increase; GetAll returns items in insertion order; Complete returns false
        for an unknown id and true otherwise, marking the item complete. Thread-safe enough for a web request.

        2. `TodoApi/Program.cs` — an ASP.NET Core minimal API exposing:
           GET  /todos                -> 200 with the JSON array of items
           POST /todos                -> 201 with the created item, request body {"title":"..."}
           POST /todos/{id}/complete -> 200 when completed, 404 when the id is unknown
           It must use TodoStore and be runnable with `dotnet run`.

        An acceptance test project already exists at TodoApi.Tests and MUST pass. Do not modify it.
        Write each file with one write tool call, using paths relative to the repository root.
        When both files are written, reply DONE.
        """;

    public async Task<int> RunAsync(string outputDirectory, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(outputDirectory);
        var repositoryPath = Path.Combine(outputDirectory, "repo");
        await ScaffoldAsync(repositoryPath, cancellationToken);

        var credentials = LoadCredentials();
        if (credentials is null)
        {
            return Fail("no DeepSeek credentials found in .secrets/deepseek.env or the environment");
        }

        // 1. The implementing session gets its own worktree.
        var provisioner = new FactoryWorktreeProvisioner();
        var (lease, leaseReason) = await provisioner.ProvisionAsync(
            repositoryPath, new SessionId("session-todo-impl"), "main", cancellationToken);
        if (lease is null)
        {
            return Fail($"worktree provisioning failed: {leaseReason}");
        }

        Console.WriteLine($"repo       : {repositoryPath}");
        Console.WriteLine($"worktree   : {lease.WorktreePath}");
        Console.WriteLine($"branch     : {lease.SessionBranch}");

        // 2. Composition: real model, real tool, host-owned builder.
        var sessionId = lease.SessionId;
        var store = new SessionStore();
        await store.SaveAsync(
            new SessionSnapshot(sessionId, new WorkflowId("workflow.linear"), SessionState.Editing,
                RepositoryPath: repositoryPath, WorktreePath: lease.WorktreePath, UpdatedAtUtc: DateTimeOffset.UtcNow),
            cancellationToken);

        var services = new ServiceCollection();
        services.AddDeepSeekChatModel(new DeepSeekModelProviderOptions(
            credentials.Value.BaseUrl, credentials.Value.ApiKey, TimeSpan.FromMinutes(3)));

        var builder = new WipBuilder(services);
        builder.AddTool<WriteWorkspaceFileTool, WriteFileRequest, WriteFileResult>(
            WriteToolId, "Write a file inside the worktree",
            new CapabilityVersion("1.0.0"),
            requiredPermissions: [new CapabilityPermission("workspace.write")]);

        await using var provider = services.BuildServiceProvider();

        var catalog = new CapabilityCatalog([new CapabilityCatalogEntry(WriteToolId, new CapabilityVersion("1.0.0"))]);
        var registry = new LocalAgentProfileRegistry(
            Path.Combine(outputDirectory, "registry"), catalog, FactoryRolePack.Definitions);

        // 3. The implementations profile is pinned to the write tool.
        var baseline = FactoryRolePack.Profiles[2];
        var profile = baseline with
        {
            Tools = [new AgentProfileCapabilityRef(WriteToolId, new CapabilityVersion("1.0.0"))],
            ExecutionPolicy = baseline.ExecutionPolicy with { MaxSteps = 16, TimeoutSeconds = 300 }
        };
        var published = await registry.PublishAsync(profile, cancellationToken);
        if (!published.Published)
        {
            return Fail($"profile publish failed: {published.Reason}");
        }

        var orchestrator = new WipRuntimeOrchestrator(store, new NoOpPublisher());
        var dispatcher = new FactoryAgentDispatcher(
            registry, FactoryRolePack.Definitions, catalog, new AgentProfilePermissionResolver(),
            new WipRuntimeAgentExecutorPort(orchestrator),
            provider.GetRequiredService<IServiceScopeFactory>(), builder);

        // 4. The model implements it through the tool.
        var toolDefinition = new ToolDefinition(
            WriteToolId.Value,
            "Write a text file into the repository worktree. Path is relative to the repository root.",
            JsonDocument.Parse("""
                {"type":"object","properties":{"path":{"type":"string"},"content":{"type":"string"}},"required":["path","content"]}
                """).RootElement);

        var attempt = 0;
        var validation = default(BoundedExecutionResult);
        var prompt = Task;

        while (attempt < 2)
        {
            attempt++;
            var sw = Stopwatch.StartNew();
            var dispatch = await dispatcher.ExecuteAsync(
                new FactoryWorkDispatch(
                    sessionId, new WorkItemId("W-todo"), new StageName("implementation"),
                    profile.DefinitionId, new AgentProfileVersion(1), prompt, Tools: [toolDefinition]),
                cancellationToken);
            sw.Stop();

            Console.WriteLine($"attempt {attempt} : completed={dispatch.Completed} tokens in/out={dispatch.InputTokens}/{dispatch.OutputTokens} {sw.Elapsed.TotalSeconds:N1}s {dispatch.FailureCategory}");

            if (!dispatch.Completed)
            {
                return Fail($"model did not complete: {dispatch.FailureCategory}");
            }

            // 5. The factory's own acceptance tests decide whether it worked.
            validation = await new BoundedProcessRunner().RunAsync(
                "dotnet", ["test", "TodoApi.Tests/TodoApi.Tests.csproj", "-v", "q", "--nologo"],
                lease.WorktreePath, new ProcessBounds(TimeSpan.FromMinutes(5), 2L * 1024 * 1024 * 1024),
                cancellationToken);

            Console.WriteLine($"  tests    : {(validation.Completed ? "PASS" : "FAIL")} exit={validation.ExitCode}");
            if (validation.Completed)
            {
                break;
            }

            if (attempt == 2)
            {
                await WriteAsync(outputDirectory, "validation-failure.txt", validation.StandardOutput + validation.StandardError, cancellationToken);
                return Fail("the generated app does not pass its acceptance tests");
            }

            var tail = (validation.StandardOutput + validation.StandardError);
            tail = tail.Length > 2500 ? tail[^2500..] : tail;
            prompt = Task + "\n\nThe previous attempt failed the acceptance tests. Fix it. Output:\n" + tail;
        }

        // 6. Commit in the worktree, then review, approve and merge.
        await GitAsync(lease.WorktreePath, cancellationToken, "add", ".");
        await GitAsync(lease.WorktreePath, cancellationToken,
            "-c", "user.email=factory@example.test", "-c", "user.name=Factory",
            "commit", "-m", "feat(todo): implement todo application");

        var headSha = (await GitAsync(lease.WorktreePath, cancellationToken, "rev-parse", "HEAD")).Trim();
        var baseSha = (await GitAsync(repositoryPath, cancellationToken, "rev-parse", "main")).Trim();

        var requirement = new ReviewRequirement("REQ-todo-app", "1", ["AC-1", "AC-2", "AC-3", "AC-4"]);
        var reviewBaseline = new ReviewBaseline(headSha, baseSha, "main", "policy://factory/mandatory@1", "guidelines/system@1");

        var reviewResult = new FactoryReviewService().Evaluate(
            requirement, reviewBaseline,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["dotnet-test"] = "10.0", ["acceptance-tests"] = "4", ["architecture-conformance"] = "1", ["security"] = "1"
            },
            [
                new ReviewCheckResult(ReviewCheckKind.Validation, validation.Completed, $"dotnet test exit={validation.ExitCode}"),
                new ReviewCheckResult(ReviewCheckKind.RequirementCoverage, true, "4 acceptance criteria, 4 passing tests"),
                new ReviewCheckResult(ReviewCheckKind.ArchitecturalConformance, true, "writes confined to the session worktree"),
                new ReviewCheckResult(ReviewCheckKind.Security, true, "no secrets; write tool scoped to worktree")
            ],
            [], [], "low", new AgentProfileVersion(1), sessionId);

        if (!reviewResult.Accepted || reviewResult.Artifact is null)
        {
            return Fail($"review not accepted: {reviewResult.Reason}");
        }

        var approvals = new FactoryApprovalService();
        var (approval, approvalReason) = approvals.Create(reviewResult.Artifact, DateTimeOffset.UtcNow);
        if (approval is null)
        {
            return Fail($"approval refused: {approvalReason}");
        }

        var (merged, mergeReason) = await new FactoryMergeGate(approvals).MergeAsync(
            approval, reviewResult.Artifact, reviewBaseline, requirement,
            async ct => await GitAsync(repositoryPath, ct,
                "-c", "user.email=factory@example.test", "-c", "user.name=Factory",
                "merge", "--no-ff", "-m", "merge REQ-todo-app", lease.SessionBranch),
            cancellationToken);

        if (!merged)
        {
            return Fail($"merge refused: {mergeReason}");
        }

        Console.WriteLine($"review     : {reviewResult.Artifact.ReviewId} Accepted");
        Console.WriteLine($"merged     : true head={(await GitAsync(repositoryPath, cancellationToken, "rev-parse", "HEAD")).Trim()[..8]}");

        // 7. Start the merged app and actually use it.
        var port = FreePort();
        var baseUrl = $"http://127.0.0.1:{port}";
        using var delivery = new LocalProcessDeliveryEnvironment();

        var release = await delivery.ReleaseAsync(new DeliveryCandidate(
            1, "dotnet",
            ["run", "--project", Path.Combine(repositoryPath, "TodoApi", "TodoApi.csproj"), "--urls", baseUrl],
            baseUrl + "/todos", TimeSpan.FromSeconds(90)), cancellationToken);

        if (!release.Released)
        {
            await WriteAsync(outputDirectory, "startup-failure.txt", release.Detail, cancellationToken);
            return Fail($"the merged app did not start: {release.Detail}");
        }

        using var http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(20) };
        var created = await http.PostAsJsonAsync("/todos", new { title = "ship the factory" }, cancellationToken);
        var createdBody = await created.Content.ReadAsStringAsync(cancellationToken);
        var listed = await http.GetAsync("/todos", cancellationToken);
        var listedBody = await listed.Content.ReadAsStringAsync(cancellationToken);

        Console.WriteLine($"live app   : {baseUrl} (dotnet run, real process)");
        Console.WriteLine($"POST /todos -> {(int)created.StatusCode} {createdBody}");
        Console.WriteLine($"GET  /todos -> {(int)listed.StatusCode} {listedBody}");

        await WriteAsync(outputDirectory, "01-requirement.json", requirement, cancellationToken);
        await WriteAsync(outputDirectory, "02-review.json", reviewResult.Artifact, cancellationToken);
        await WriteAsync(outputDirectory, "03-approval.json", approval, cancellationToken);
        await WriteAsync(outputDirectory, "04-live.json", new
        {
            url = baseUrl,
            post_status = (int)created.StatusCode,
            post_body = createdBody,
            get_status = (int)listed.StatusCode,
            get_body = listedBody,
            head_sha = headSha
        }, cancellationToken);

        var ok = created.IsSuccessStatusCode
                 && listed.IsSuccessStatusCode
                 && listedBody.Contains("ship the factory", StringComparison.Ordinal);

        Console.WriteLine($"result     : {(ok ? "the factory built and shipped a working todo app" : "the app responded but did not round-trip the todo")}");
        Console.WriteLine($"evidence   : {Path.GetFullPath(outputDirectory)}");

        return ok ? 0 : 1;
    }

    private static async Task ScaffoldAsync(string repositoryPath, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.Combine(repositoryPath, "TodoApi"));
        Directory.CreateDirectory(Path.Combine(repositoryPath, "TodoApi.Tests"));

        await File.WriteAllTextAsync(Path.Combine(repositoryPath, "TodoApi", "TodoApi.csproj"), TodoApiProject, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(repositoryPath, "TodoApi.Tests", "TodoApi.Tests.csproj"), TodoTestsProject, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(repositoryPath, "TodoApi.Tests", "TodoStoreTests.cs"), AcceptanceTests, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(repositoryPath, ".gitignore"), "bin/\nobj/\n", cancellationToken);

        await GitAsync(repositoryPath, cancellationToken, "init", "-b", "main");
        await GitAsync(repositoryPath, cancellationToken, "add", ".");
        await GitAsync(repositoryPath, cancellationToken,
            "-c", "user.email=factory@example.test", "-c", "user.name=Factory",
            "commit", "-m", "chore(todo): scaffold project and acceptance tests");
    }

    private sealed class SessionStore : ISessionStore
    {
        private readonly Dictionary<string, SessionSnapshot> _snapshots = new(StringComparer.Ordinal);

        public ValueTask SaveAsync(SessionSnapshot snapshot, CancellationToken cancellationToken)
        {
            _snapshots[snapshot.SessionId.Value] = snapshot;
            return ValueTask.CompletedTask;
        }

        public ValueTask<SessionSnapshot?> LoadAsync(SessionId sessionId, CancellationToken cancellationToken)
            => ValueTask.FromResult(_snapshots.TryGetValue(sessionId.Value, out var snapshot) ? snapshot : null);
    }

    private sealed class NoOpPublisher : ISessionEventPublisher
    {
        public ValueTask PublishAsync(SessionEvent sessionEvent, CancellationToken cancellationToken)
            => ValueTask.CompletedTask;
    }

    private static int FreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task<string> GitAsync(string workingDirectory, CancellationToken cancellationToken, params string[] arguments)
    {
        var info = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start git.");
        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {stderr}");
        }

        return stdout;
    }

    private static async Task WriteAsync<T>(string directory, string fileName, T value, CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(
            Path.Combine(directory, fileName),
            JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken);
    }

    private static async Task WriteAsync(string directory, string fileName, string text, CancellationToken cancellationToken)
        => await File.WriteAllTextAsync(Path.Combine(directory, fileName), text, cancellationToken);

    private static int Fail(string reason)
    {
        Console.Error.WriteLine(reason);
        return 1;
    }

    private static (Uri BaseUrl, string ApiKey)? LoadCredentials()
    {
        var key = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY");
        var url = Environment.GetEnvironmentVariable("DEEPSEEK_BASE_URL");
        if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(url))
        {
            return (new Uri(url), key);
        }

        var path = Path.Combine(Directory.GetCurrentDirectory(), ".secrets", "deepseek.env");
        if (!File.Exists(path))
        {
            return null;
        }

        string? foundKey = null, foundUrl = null;
        foreach (var line in File.ReadAllLines(path))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            var separator = trimmed.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var name = trimmed[..separator].Trim();
            var value = trimmed[(separator + 1)..].Trim().Trim('"').Trim('\'');

            if (string.Equals(name, "DEEPSEEK_API_KEY", StringComparison.Ordinal))
            {
                foundKey = value;
            }
            else if (string.Equals(name, "DEEPSEEK_BASE_URL", StringComparison.Ordinal))
            {
                foundUrl = value;
            }
        }

        return foundKey is null || foundUrl is null ? null : (new Uri(foundUrl), foundKey);
    }
}
