using Wip.Abstractions.Descriptors;
using Wip.Abstractions.Identifiers;

namespace Wip.Agent.Basic;

public sealed record AgentExecutionContext(
    SessionId SessionId,
    WorkflowId WorkflowId,
    string Task,
    string RepositoryPath,
    string WorktreePath,
    IReadOnlyList<ICapabilityDescriptor> Tools,
    IReadOnlyList<ICapabilityDescriptor> Validators,
    PolicyId PolicyId);
