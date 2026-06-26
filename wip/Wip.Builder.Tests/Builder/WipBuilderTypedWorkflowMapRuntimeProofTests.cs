using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Samples.TodoApp.WipAgents;
using Wip.Abstractions.Identifiers;
using Wip.Abstractions.Sessions;
using Wip.Runtime.Runtime;
using Xunit;

namespace Wip.Builder.Tests.Builder;

public sealed class WipBuilderTypedWorkflowMapRuntimeProofTests
{
    private const string ChecklistItem = "Add explicit runtime proof that builder workflows with typed Map<TFrom,TTo> stage adapters preserve concrete stage request/result contracts from external plugin projects [depends on BS-003 typed workflow mapping parity]";

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public void CompileLinear_GivenExternalPluginWorkflowRegistration_PreservesConcreteMapContracts()
    {
        var services = new ServiceCollection();
        var builder = services.AddWipCapabilities();
        builder.AddTodoAppWipAgents();

        var workflowRegistration = Assert.Single(
            builder.WorkflowRegistrations,
            static registration => registration.WorkflowId.Value == "todoapp.workflow.delivery");

        var compilation = WorkflowBuilderStageCompiler.CompileLinear(workflowRegistration);

        var runStage = Assert.Single(
            compilation.StageDescriptors,
            static descriptor => descriptor.Stage == WorkflowStageKind.Run);
        var validateStage = Assert.Single(
            compilation.StageDescriptors,
            static descriptor => descriptor.Stage == WorkflowStageKind.Validate);

        Assert.Equal(typeof(TodoWorkflowRequest), runStage.RequestType);
        Assert.Equal(typeof(TodoWorkflowResult), runStage.ResultType);
        Assert.Equal(typeof(TodoWorkflowRequest).FullName, runStage.RequestContractName);
        Assert.Equal(typeof(TodoWorkflowResult).FullName, runStage.ResultContractName);

        var planToRunMap = Assert.Single(
            compilation.MapAdapters,
            static map => map.FromStage == WorkflowStageKind.Plan && map.ToStage == WorkflowStageKind.Run);
        Assert.Equal(typeof(PlanStageResult), planToRunMap.SourceType);
        Assert.Equal(typeof(TodoWorkflowRequest), planToRunMap.TargetType);
        Assert.Equal(typeof(PlanStageResult).FullName, planToRunMap.SourceContractName);
        Assert.Equal(typeof(TodoWorkflowRequest).FullName, planToRunMap.TargetContractName);

        var runToValidateMap = Assert.Single(
            compilation.MapAdapters,
            static map => map.FromStage == WorkflowStageKind.Run && map.ToStage == WorkflowStageKind.Validate);
        Assert.Equal(typeof(TodoWorkflowResult), runToValidateMap.SourceType);
        Assert.Equal(typeof(ValidateStageRequest), runToValidateMap.TargetType);
        Assert.Equal(typeof(TodoWorkflowResult).FullName, runToValidateMap.SourceContractName);
        Assert.Equal(typeof(ValidateStageRequest).FullName, runToValidateMap.TargetContractName);

        Assert.Equal(typeof(ValidateStageRequest).FullName, compilation.ResolveMappedInputContractName(validateStage.Stage));
        Assert.All(compilation.MapAdapters, static map =>
        {
            Assert.NotEqual(typeof(object), map.SourceType);
            Assert.NotEqual(typeof(object), map.TargetType);
        });
    }

    [Fact]
    [Trait("ChecklistItem", ChecklistItem)]
    public async Task RunWorkflowAsync_GivenExternalPluginWorkflow_EmitsMappedRuntimeContractsForTypedStages()
    {
        var services = new ServiceCollection();
        var builder = services.AddWipCapabilities();
        builder.AddTodoAppWipAgents();

        var sessionStore = new InMemorySessionStore();
        var publisher = new CollectingSessionEventPublisher();
        var orchestrator = new WipRuntimeOrchestrator(sessionStore, publisher);

        var root = Path.Combine(Path.GetTempPath(), "wip-builder-map-proof-" + Guid.NewGuid().ToString("N"));
        var worktreePath = Path.Combine(root, "worktree");
        var artifactsPath = Path.Combine(root, "artifacts");
        Directory.CreateDirectory(root);

        try
        {
            var started = await orchestrator.StartSessionAsync(
                new SessionStartRequest(
                    WorkflowId: new WorkflowId("todoapp.workflow.delivery"),
                    TaskDescription: "prove map contract preservation",
                    RepositoryPath: root,
                    WorktreePath: worktreePath,
                    ArtifactDirectory: artifactsPath),
                CancellationToken.None);

            var result = await orchestrator.RunWorkflowAsync(
                started.SessionId,
                builder,
                selectedWorkflowId: new WorkflowId("todoapp.workflow.delivery"),
                cancellationToken: CancellationToken.None);

            var runStage = Assert.Single(
                result.Stages,
                static stage => stage.Descriptor.Stage == WorkflowStageKind.Run);
            var validateStage = Assert.Single(
                result.Stages,
                static stage => stage.Descriptor.Stage == WorkflowStageKind.Validate);

            Assert.Equal(typeof(TodoWorkflowRequest), runStage.Descriptor.RequestType);
            Assert.Equal(typeof(TodoWorkflowResult), runStage.Descriptor.ResultType);
            Assert.Equal(typeof(TodoWorkflowRequest).FullName, runStage.MappedInputContractName);

            Assert.Equal(typeof(ValidateStageRequest), validateStage.Descriptor.RequestType);
            Assert.Equal(typeof(ValidateStageResult), validateStage.Descriptor.ResultType);
            Assert.Equal(typeof(ValidateStageRequest).FullName, validateStage.MappedInputContractName);

            Assert.Contains(
                publisher.Events,
                static sessionEvent => sessionEvent.Kind == SessionEventKind.WorkflowExecuted);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private sealed class InMemorySessionStore : ISessionStore
    {
        private readonly ConcurrentDictionary<SessionId, SessionSnapshot> _sessions = new();

        public ValueTask SaveAsync(SessionSnapshot snapshot, CancellationToken cancellationToken)
        {
            _sessions[snapshot.SessionId] = snapshot;
            return ValueTask.CompletedTask;
        }

        public ValueTask<SessionSnapshot?> LoadAsync(SessionId sessionId, CancellationToken cancellationToken)
        {
            if (_sessions.TryGetValue(sessionId, out var snapshot))
            {
                return ValueTask.FromResult<SessionSnapshot?>(snapshot);
            }

            return ValueTask.FromResult<SessionSnapshot?>(null);
        }
    }

    private sealed class CollectingSessionEventPublisher : ISessionEventPublisher
    {
        public List<SessionEvent> Events { get; } = [];

        public ValueTask PublishAsync(SessionEvent sessionEvent, CancellationToken cancellationToken)
        {
            Events.Add(sessionEvent);
            return ValueTask.CompletedTask;
        }
    }
}
