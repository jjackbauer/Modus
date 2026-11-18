# Deferred: contributor README project-map rows for the factory projects

`src/WIP.Contributor-Architecture.README.md` is **outside this goal's baseline scope**, so the edit that
added these rows was reverted rather than committed. The content is preserved here so it is not lost.

To apply: paste these rows into the project map in `src/WIP.Contributor-Architecture.README.md`, in the
"A" position (after `Wip.Builder`, before `Wip.Modus`).

Note: the repository's own documentation gate (`WipContributorArchitectureReadmeTests`) is **already red at
`HEAD`** for 42 `Wip.*` projects that were never documented, including `Wip.Bones`, `Wip.Tholdem` and the
whole `Wip.Sandbox.*` family. Adding the factory rows alone will not make that gate pass; it needs the
full project map filled in. Recorded as OBS-007 / EVD-016.

| Project | Ownership | Runtime role | Extension seams |
|---|---|---|---|| Wip.Factory.Admission | WIP Factory Maintainers | Executable capability admission lifecycle: inspection, isolated health test, versioned promotion, drain and rollback | admission catalog, inspection and health contracts, promotion and drain seams |
| Wip.Factory.Adaptation | WIP Factory Maintainers | Session analytics, candidate profiles, evaluation and promotion decisions for the improvement plane | IAgentReflectionPolicy, IAgentEvolutionEvaluator, AgentEvolutionExperiment |
| Wip.Factory.Agents.Core | WIP Factory Maintainers | The nine factory role definitions and typed agent facades over the WIP agent executor | role facades implementing IAgent<TRequest,TResult> |
| Wip.Factory.Analysis | WIP Factory Maintainers | Bounded-process adapter to the RustLace systems-modelling CLI for advisory predictions | IRustLaceAnalysisCapability and the lovelace-run process adapter |
| Wip.Factory.Blackboard | WIP Factory Maintainers | Role-scoped context resolution across the six owned views with provenance and token budgets | IFactoryBlackboard.ResolveAsync, IFactoryBlackboard.PublishAsync, ContextQuery |
| Wip.Factory.Contracts | WIP Factory Maintainers | Factory domain identifiers, event, artifact, profile and evidence contracts independent of transport and host | WorkItemId, AgentProfileVersion, FactoryEventId, IWorkItemStore, IEventOutbox |
| Wip.Factory.Core | WIP Factory Maintainers | Factory work-state machine, dependency readiness and stage routing expressed as pure domain rules | work-state transition table, dependency readiness evaluation, routing rules |
| Wip.Factory.Host | WIP Factory Maintainers | Composition root, background event handlers and operator demo verbs for the factory alpha | factory composition root, background dispatch worker, operator verbs |
| Wip.Factory.Modus | WIP Factory Maintainers | Binds Modus-discovered plugin capabilities into the host WipBuilder through one contributor path | IFactoryCapabilityContributor and host-owned builder binding |
| Wip.Factory.Persistence | WIP Factory Maintainers | Transport-neutral durable work and event store with atomic state-plus-event commit, inbox and leases | IWorkItemStore, IEventOutbox, IEventInbox, local durable store implementation |
| Wip.Factory.Telemetry | WIP Factory Maintainers | Structured traces, outcome attribution and anomaly detection for factory evidence | IAgentSessionTraceStore, IOutcomeAttributionStore, anomaly detectors |
| Wip.Factory.Wip | WIP Factory Maintainers | Adapters from factory dispatch to WIP session, agent executor, governance and worktrees | IFactoryAgentDispatcher, role facade over IAgentExecutor, RunAgentAsync dispatch path |
