# Checklist Transition Proof: Lifetime-Specific Base Class Migration

Date: 2026-05-24
Checklist item:

`Migrate interface-only sample plugins to lifetime-specific base classes (SingletonPlugin<T>, ScopedPlugin<T>, or TransientPlugin<T>) with explicit runtime ownership semantics [depends on compile isolation]`

## Baseline Unchecked Source Text Evidence

The baseline unchecked checklist text used by this orchestration run is:

`- [ ] Migrate interface-only sample plugins to lifetime-specific base classes (SingletonPlugin<T>, ScopedPlugin<T>, or TransientPlugin<T>) with explicit runtime ownership semantics [depends on compile isolation]`

Verifier-gap context observed at run start:

- The requirements file was present but untracked in git working tree (`?? .github/requirements/all the projects.md`), so transition proof could not be derived from tracked repository state.

## Checked Completion Evidence

Current checked checklist line in requirements document:

`- [x] Migrate interface-only sample plugins to lifetime-specific base classes (SingletonPlugin<T>, ScopedPlugin<T>, or TransientPlugin<T>) with explicit runtime ownership semantics [depends on compile isolation] [transition-proof: .github/requirements/transition-proofs/checklist-item-lifetime-base-class-migration-transition-proof-2026-05-24.md]`

Runtime ownership semantics evidence in migrated sample plugins:

- `src/SamplePlugins/Plugin.Payments.Gateway/PaymentsGatewayPlugin.cs`: `public sealed class PaymentsGatewayPlugin : SingletonPlugin<PaymentsGatewayPlugin>`
- `src/SamplePlugins/Plugin.Orders.Fulfillment/OrdersFulfillmentPlugin.cs`: `public sealed class OrdersFulfillmentPlugin : ScopedPlugin<OrdersFulfillmentPlugin>`

Deterministic local proof note:

- This artifact is linked directly from the checked checklist line and captures both baseline `[ ]` text and current `[x]` text for the exact same item.

## Build/Test Execution Evidence

Strict build command run for requested project:

- `dotnet build src/Modus.Host/Modus.Host.csproj -c Release -v minimal`
- Result: success (`Modus.Core` and `Modus.Host` built successfully; overall build succeeded).

Strict test command run for requested test project:

- `dotnet test tests/Modus.Host.IntegrationTests/Modus.Host.IntegrationTests.csproj -c Release -v minimal`
- Result: success (`total: 353, failed: 0, succeeded: 353, skipped: 0`).

Note:

- A preliminary `--no-build` test probe executed during evidence capture showed stale failures in in-flight inventory tests. The strict full build+test gate above is the authoritative result for this repair round.