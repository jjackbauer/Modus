# Wip.Abstractions Contributor README

Wip.Abstractions defines typed contracts shared by builder, runtime, shell, policies, and integrations. Contributors should evolve these contracts before adding behavior in higher layers.

## Public Interfaces and Behavior Contracts

### Capability contracts

| Interface | Request/Result behavior contract | Invalid-input expectations |
|---|---|---|
| ICapability<TRequest, TResult> | ExecuteAsync accepts a typed request and capability context, and returns a typed TResult asynchronously. | TRequest and TResult are constrained to notnull. Capability descriptors reject object as request or result type to prevent untyped payload contracts. |
| IAgent<TRequest, TResult> | Specialization of ICapability for agent execution paths with the same typed ExecuteAsync contract. | Same as ICapability contract constraints. |
| ITool<TRequest, TResult> | Specialization of ICapability for tool execution paths with the same typed ExecuteAsync contract. | Same as ICapability contract constraints. |
| IValidator<TRequest, TResult> | Specialization of ICapability for validator execution paths with the same typed ExecuteAsync contract. | Same as ICapability contract constraints. |

### Workflow contracts

| Interface | Request/Result behavior contract | Invalid-input expectations |
|---|---|---|
| IWorkflow<TRequest, TResult> | WorkflowId identifies the workflow contract. ExecuteAsync receives typed request and WorkflowContext and returns typed TResult. | TRequest and TResult are constrained to notnull. WorkflowId constructor rejects null, empty, or whitespace values. |
| IWorkflowDescriptor | Exposes metadata for workflow discovery and typed request/result reflection. | WorkflowDescriptor<TRequest, TResult> rejects whitespace display names and rejects object request/result generic arguments. |

### Policy contracts

| Interface/Type | Request/Result behavior contract | Invalid-input expectations |
|---|---|---|
| IPolicy<TRequest, TResult> | EvaluateAsync receives typed request and PolicyContext and returns typed TResult asynchronously. | TRequest and TResult are constrained to notnull. Policy descriptors reject object request/result generic arguments. |
| IPolicy<TRequest> | Compatibility alias for policies that return PolicyDecision. | Inherits the same typed request constraints and behavior contract through IPolicy<TRequest, PolicyDecision>. |
| IPolicyDescriptor | Exposes policy metadata for discovery and typed request/result reflection. | PolicyDescriptor<TRequest, TResult> requires policy type to implement IPolicy<TRequest, TResult>. |
| PolicyDecision | Allow returns allowed decision with empty reason. Deny returns denied decision with explicit reason. | Deny throws ArgumentException when reason is null, empty, or whitespace. |

### Session contracts

| Interface/Type | Request/Result behavior contract | Invalid-input expectations |
|---|---|---|
| ISessionStore | SaveAsync persists SessionSnapshot. LoadAsync returns matching SessionSnapshot or null when missing. | SessionId and WorkflowId constructors used in snapshots reject null, empty, or whitespace values. |
| SessionSnapshot | Captures typed session identity, workflow identity, state, and persisted paths/timestamp. | Typed identifier constructors prevent invalid identifier values at snapshot creation call sites. |

## Typed Identifiers

The abstraction layer uses value-object style typed identifiers:

- CapabilityId
- ArtifactId
- SessionId
- WorkflowId
- PolicyId

All typed identifiers enforce the same invariant:

- constructor argument value cannot be null, empty, or whitespace;
- invalid values throw ArgumentException with paramName value;
- ToString returns the original stored value.

## Contributor Notes

- Prefer introducing new semantic identifiers as typed record structs instead of raw string parameters.
- Keep request/result payloads strongly typed and avoid object-based execution payload contracts.
- When changing a contract, add or update behavior-proof tests in tests/Wip.Abstractions.Tests.