using Wip.Abstractions.Capabilities;
using Wip.Abstractions.Identifiers;
using Wip.Factory.Adaptation;
using Wip.Factory.Contracts.Identifiers;
using Wip.Factory.Contracts.Profiles;
using Wip.Factory.ControlApi.Infrastructure;
using Wip.Factory.Host.Factory;
using Wip.Factory.Telemetry;

namespace Wip.Factory.ControlApi.Services;

/// <summary>
/// The adapter's composition root. It owns exactly one factory instance and reuses the factory's public runtime,
/// registry, evidence stores and telemetry stores. It never reads the factory journal directly.
/// </summary>
public sealed class FactoryService : IDisposable
{
    private readonly CancellationTokenSource _shutdown = new();

    public FactoryService(ControlApiOptions options, ApiAuditStore? audit = null)
    {
        Options = options;
        var root = options.Factory;
        root.EnsureCreated();

        var (runner, runnerName) = CreateRunner(options);
        var candidates = options.ExecutionEnabled ? new DotnetRunCandidateHost() : null;
        var builds = options.ExecutionEnabled ? new DotnetBuildVerifier() : null;
        var merges = options.ExecutionEnabled ? new GitMergeEngine() : null;

        RunnerName = runnerName;
        Runtime = new FactoryRuntime(root, runner, candidates, builds, merges);

        Templates = new FactoryTemplateRegistry(root.Root);
        ReviewEvidence = new ReviewEvidenceStore(Path.Combine(root.Root, "verify", "review-evidence.log"));
        ReleaseVerifications = new ReleaseVerificationStore(Path.Combine(root.Root, "verify", "release-verifications.log"));

        var toolCatalog = new CapabilityCatalog(
            RealModelImplementationRunner.PinnedToolIds.Select(static id => new CapabilityCatalogEntry(id, new CapabilityVersion("1.0.0"))));
        Profiles = new LocalAgentProfileRegistry(
            Path.Combine(root.Root, "registry"),
            toolCatalog,
            FactoryRolePack.Definitions);
        Traces = new LocalAgentSessionTraceStore(Path.Combine(root.Root, "telemetry"));

        Audit = audit ?? new ApiAuditStore(options.AuditDirectory);
        Planner = new PlanningStore(options.AuditDirectory);
        Dispatch = new DispatchManager(this);
        Serving = new ServeManager(this);
        ReadModel = new ReadModels.FactoryReadModel(this);
    }

    public ControlApiOptions Options { get; }
    public FactoryRuntime Runtime { get; }
    public FactoryTemplateRegistry Templates { get; }
    public ReviewEvidenceStore ReviewEvidence { get; }
    public ReleaseVerificationStore ReleaseVerifications { get; }
    public LocalAgentProfileRegistry Profiles { get; }
    public LocalAgentSessionTraceStore Traces { get; }
    public ApiAuditStore Audit { get; }
    public PlanningStore Planner { get; }
    public DispatchManager Dispatch { get; }
    public ServeManager Serving { get; }
    public ReadModels.FactoryReadModel ReadModel { get; }
    public string RunnerName { get; }
    public CancellationToken ShutdownToken => _shutdown.Token;

    private (IImplementationStageRunner? Runner, string Name) CreateRunner(ControlApiOptions options)
    {
        switch (options.Implementation)
        {
            case ImplementationMode.DeepSeek when options.ModelBaseUrl is not null && options.ModelApiKey is not null:
                return (new RealModelImplementationRunner(options.Factory, options.ModelBaseUrl, options.ModelApiKey), "deepseek-real-model");
            case ImplementationMode.DeterministicDemo:
                return (new DemoImplementationRunner(options.Factory), "deterministic-demo");
            default:
                return (null, "unavailable");
        }
    }

    public void Dispose()
    {
        Serving.StopAll();
        _shutdown.Cancel();
        _shutdown.Dispose();
    }
}
