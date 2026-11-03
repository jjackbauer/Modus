# WP-1.5 — security CI green (2026-11-03)

Run [41641244173](https://github.com/jjackbauer/Modus/actions/runs/41641244173) on `bfbb62f`:

- `wip-security` (Windows): **success**
- `wip-security (ubuntu)`: **success**
- Both run the 17-test sandbox suite; Windows/Ubuntu builds and architecture passed.
- The offline 16-project gate was still running at inspection time.

The successful Linux run used the 30s CPU / 8GB virtual-memory / 4096 nproc floors. The client move timeout still kills a runaway move at 1s.

C1 OS file/network/native containment remains open; this is a test-policy boundary, not an OS sandbox.
