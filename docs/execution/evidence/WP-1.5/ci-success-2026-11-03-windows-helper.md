# WP-1.5 — Windows AppContainer helper verified (2026-11-03)

Run [41655112634](https://github.com/jjackbauer/Modus/actions/runs/41655112634) on `619db2e`:

- **Install AppContainer profile and helper — success**
- **Run sandbox hostile/protocol suite (Windows) — success**
- **wip-security (ubuntu) — success**
- **wip-build Windows/Ubuntu — success**
- **wip-architecture — success**

The Windows helper runs as a SYSTEM scheduled task, validates the per-install token and worker/temp paths, launches the worker with an AppContainer token, and the client verifies **TokenIsAppContainer** before accepting the process. The Linux job uses unshare+chroot where user namespaces are available.

C1 (OS file/network/native containment) is now implemented on both platforms for the tested paths. Remaining security residuals: P/Invoke/native APIs are still permitted (constrained by the AppContainer token or chroot/namespaces), and kernel/runtime exploits remain out of scope. Full G2 still needs independent re-review; no release.
