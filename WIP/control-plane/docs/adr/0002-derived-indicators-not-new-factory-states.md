# ADR 0002: Derived operator indicators, not new factory states

## Status
Accepted.

## Context
Operators need richer signals such as "awaiting contract approval" and "verifying". Adding values to `FactoryWorkState` would change factory behavior and make the control plane a second state machine.

## Decision
The adapter derives `derivedIndicator` and stage/health indicators from factory events and the canonical state. The Rust domain renders labels and tones for those derived values. The UI always distinguishes the authoritative state chip from derived indicators. Planning metadata is stored separately and marked `authoritative: false`.

## Consequences
- Derived indicators can evolve without changing factory persisted states.
- No control-plane transition can advance the factory by itself.
- The UI must never present a derived indicator as a command or acceptance decision.