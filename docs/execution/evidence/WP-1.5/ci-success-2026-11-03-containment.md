# WP-1.5 — security CI green with containment probe (2026-11-03)

Run [41644557144](https://github.com/jjackbauer/Modus/actions/runs/41644557144) on `4defd9f`:

- `wip-security` (Windows): **success**
- `wip-security (ubuntu)`: **success**
- `wip-build` Windows/Ubuntu and `wip-architecture`: success.

The 18-test suite is green on both platforms. The Linux containment test probes unprivileged user namespaces and skips where the host forbids them; WSL exercises the real chroot. Windows AppContainer is still not implemented, so C1 remains partially closed and G2 stays conditional.
