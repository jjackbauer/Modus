# WP-1.5 — Hostile-code / security suite (in progress)

**Status:** in progress; CI security jobs added, OS containment still open.
**Date:** 2026-11-03

## Coverage

16 tests in `tests/Wip.Sandbox.Worker.Tests`:

- handshake gate, 1.x version, hash mismatch, cache/ALC behavior;
- malformed JSON, oversized frame, stdout injection, error determinism;
- compile-time rejection of DllImport, File, Network, Process, Reflection;
- environment allowlist (no inherited sentinel);
- infinite-loop timeout + process-tree kill;
- Linux prlimit launcher shape.

CI jobs `wip-security` (Windows) and `wip-security-linux` (Ubuntu) build `Wip.slnx` and run this suite with TRX artifacts.

## Gaps

- C1 OS file/network/native containment is not implemented; the suite proves compile-time policy and resource kill, not OS isolation.
- No OS-level memory-bomb or no-orphan stress test yet.
- No independent pen test; G2 remains conditional/trusted-only.

## Acceptance

| Criterion | Status |
|---|---|
| File/network/process/reflection abused at compile surface | **PASS** — bounded compile rejection |
| Infinite loop killed | **PASS** — client timeout/kill test |
| Tests deterministic on Windows and Linux | **PARTIAL** — Windows + Ubuntu jobs added; CI result pending |
| No host file/network access under OS policy | **OPEN — C1** |
