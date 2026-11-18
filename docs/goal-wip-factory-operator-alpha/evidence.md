# Evidence — verified by the orchestrator

| ID | Fact | Source | How verified |
|---|---|---|---|
| EVD-001 | `FactoryWorkState` has exactly 16 members (`Proposed`…`Closed`) | `WIP/Wip.Factory.Contracts/Work/WorkContracts.cs` | Read the enum block |
| EVD-002 | Shipped adjacency does **not** allow `DeliveryValidation → NeedsRework` | `WIP/Wip.Factory.Core/WorkStates.cs` | Read the `Adjacency` dictionary |
| EVD-003 | `ClaimUnpublishedAsync` does not reserve claims | `WIP/Wip.Factory.Persistence/LocalFactoryStore.cs` | Read the method body: filters `!Published`, returns, no write |
| EVD-004 | Dispatch worker never touches leases while the store exposes them | `FactoryDispatchWorker.cs` (0 refs) vs `LocalFactoryStore.cs` (69 refs) | Counted matches |
| EVD-005 | Merge gate dedupes approvals in a process-local `HashSet` | `WIP/Wip.Factory.Wip/Governance/ReviewAndApproval.cs` | Read the field and `MergeAsync` |
| EVD-006 | `LocalFactoryStore` is not referenced by the host | `WIP/Wip.Factory.Host/**` | Searched for `LocalFactoryStore` in the host: 0 matches |
| EVD-007 | `IStageDispatchHandler` is implemented only in tests | repository-wide search | 6 matches, all in `DispatchWorkerTests.cs` / contracts || EVD-008 | Fresh baseline re-run: build clean, 86 factory tests passing | `dotnet build Wip.slnx`; four factory suites | Ran all four now: architecture 5/5, Core 18/18, Agents 58/58, Host 5/5 = 86 passing, 0 failed; build 0 errors. Recorded per N-01, which discounts the earlier historical counts. |