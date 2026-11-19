# ADR 0003: Durable SSE cursors with explicit resync

## Status
Accepted.

## Context
Operators need live activity that survives refresh, gateway reconnects and temporary adapter restarts. Polling creates load and cannot prove ordering. SSE without a durable cursor loses events.

## Decision
The adapter exposes monotonically increasing factory event sequence numbers. `/v1/events/stream` accepts `Last-Event-ID`/`after`, emits heartbeat frames and sends an explicit resync envelope when the requested cursor is older than the retained stream window or ahead of the store. The Rust gateway relays those frames and preserves the cursor. The browser uses EventSource, reconnects automatically and shows connected/reconnecting/stale/disconnected states. Initial snapshots are fetched over normal HTTP and merged with events by sequence number.

## Consequences
- Refresh and reconnect are resumable.
- Duplicate delivery is harmless because events carry sequence numbers.
- The stream is bounded; lost history is recovered through a page query and resync, not silent gaps.