using Wip.Abstractions.Descriptors;
using Wip.Abstractions.Identifiers;

namespace Wip.Runtime.Runtime;

public sealed record AgentExecutionContext(
    SessionId SessionId,
    WorkflowId WorkflowId,
    string Task,
    string RepositoryPath,
    string WorktreePath,
    IReadOnlyList<ICapabilityDescriptor> Tools,
    IReadOnlyList<ICapabilityDescriptor> Validators,
    PolicyId PolicyId);