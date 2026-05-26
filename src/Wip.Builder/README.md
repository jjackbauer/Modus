# Wip.Builder Contributor README

Wip.Builder is the registration surface for runtime capabilities, policies, and workflows. Contributors should register execution components here instead of mutating runtime orchestrator internals directly.

## Registration APIs

| API | Behavior contract | Registration constraints |
|---|---|---|
| AddAgent<TAgent, TRequest, TResult>(CapabilityId, string) | Registers a typed agent descriptor and singleton capability service. | TAgent must implement IAgent<TRequest, TResult>. TRequest and TResult are non-nullable generic arguments. |
| AddTool<TTool, TRequest, TResult>(CapabilityId, string) | Registers a typed tool descriptor and singleton capability service. | TTool must implement ITool<TRequest, TResult>. TRequest and TResult are non-nullable generic arguments. |
| AddValidator<TValidator, TRequest, TResult>(CapabilityId, string) | Registers a typed validator descriptor and singleton capability service. | TValidator must implement IValidator<TRequest, TResult>. TRequest and TResult are non-nullable generic arguments. |
| AddAgent<TAgent>(CapabilityId, string) / AddTool<TTool>(CapabilityId, string) / AddValidator<TValidator>(CapabilityId, string) | Infers request/result signatures from implemented generic interfaces and registers a descriptor plus singleton service. | Capability type must implement exactly one matching interface signature; zero or multiple matches throw InvalidOperationException. |
| EnableCapabilityReplacement(bool enabled = true) | Enables or disables duplicate capability replacement mode for capability ID collisions. | Replacement is disabled by default. |
| AddPolicy<TPolicy, TRequest>(PolicyId) | Registers policy metadata and singleton policy service. | TPolicy must implement IPolicy<TRequest>. TRequest is non-nullable generic argument. |
| AddWorkflow<TWorkflow, TRequest, TResult>(WorkflowId, string) | Registers workflow metadata, typed workflow descriptor, and singleton workflow service. | TWorkflow must implement IWorkflow<TRequest, TResult>. displayName cannot be null, empty, or whitespace. |

## Duplicate Capability Replacement

By default, capability IDs are unique. Registering the same capability ID twice throws InvalidOperationException.

When replacement mode is enabled via EnableCapabilityReplacement(), the second registration replaces the first registration for that capability ID:

1. The previous descriptor is removed from CapabilityDescriptors.
2. Existing DI registrations for the previous capability implementation type are removed.
3. The replacement capability descriptor and singleton registration are added.

This behavior is deterministic and intended for controlled overwrite scenarios such as test wiring or targeted plugin swaps.

## Workflow and Policy Registration Constraints

- Policies are registered through AddPolicy<TPolicy, TRequest>(PolicyId) as singleton services and tracked in PolicyRegistrations.
- Workflows are registered through AddWorkflow<TWorkflow, TRequest, TResult>(WorkflowId, string) as singleton services and tracked in WorkflowRegistrations.
- Workflow display names are required contributor-facing metadata and must contain non-whitespace content.
- Both APIs enforce typed generic contracts at compile time through IPolicy<TRequest> and IWorkflow<TRequest, TResult> constraints.

## Contributor Notes

- Use explicit typed overloads when the capability has more than one matching interface signature.
- Keep CapabilityId, PolicyId, and WorkflowId stable for backward-compatible manifests.
- Add behavior-proof tests in tests/Wip.Builder.Tests whenever registration semantics change.