# G2 re-review packet — 2026-11-03

**Status:** pending human approval. The previous G2 decision was conditional/trusted-only; C1 has since been implemented on both platforms.

## Evidence

| Condition | Evidence |
|---|---|
| Linux OS containment | `unshare --user --map-root-user --net --pid --mount` + `chroot`; host repo/home unmounted; 18/18 WSL |
| Windows OS containment | SYSTEM helper + AppContainer worker; `TokenIsAppContainer` verified before use; CI run [41655112634](https://github.com/jjackbauer/Modus/actions/runs/41655112634) |
| Windows security job | installer + helper + sandbox suite **success** |
| Ubuntu security job | **success** |
| Builds/architecture | Windows/Ubuntu success |
| Worker isolation | separate process, cleared environment, curated refs/deny-list, bounded frames, source hashes |
| Limits/kill | Job Object/prlimit, timeouts, process-tree kill, memory-bomb cap |
| Host assembly load | worker owns compilation/loading; host does not load model assembly |

## Residual risk

- P/Invoke/native APIs remain permitted; they execute under the AppContainer token or Linux chroot/namespaces, but kernel/runtime exploits are out of scope.
- Windows containment depends on the elevated installer/helper task; if absent, `RequireOsContainment` fails closed.
- Linux containment depends on unprivileged user namespaces; where forbidden, the containment test skips and production fails closed.
- The independent security review has not re-signed the new OS containment; this packet requests that review/approval.

## Decision requested

Approve **full G2** (permitting WP-1.3+ work and later release steps) with the above residual risk, or keep G2 conditional/trusted-only.
