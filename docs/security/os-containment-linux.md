# Linux OS containment (WP-1.4/1.5)

**Status:** implemented and tested on WSL Ubuntu 24.04; Windows AppContainer pending.

## Mechanism

`tools/sandbox/linux-worker-sandbox.sh` runs under:

~~~text
unshare --user --map-root-user --net --pid --mount --fork
~~~

and then `chroot` into a per-run root containing only:

- read-only .NET runtime directory;
- read-only worker output directory (`/app`);
- read-only `/usr/lib`, `/usr/lib64`, `/usr/share` for the runtime loader/ICU;
- tmpfs `/tmp`, `/proc`, and `/dev/null|urandom|random`.

The host repository, home directory, credentials, and workspace are **not mounted**. The network namespace has no interfaces. The PID namespace hides host processes. The worker runs as namespace root but has no host path to escape through.

## Tests

`Client_GivenOsContainment_ExpectedWorkerRunsInChroot` starts the client with `RequireOsContainment=true`, performs handshake/compile/chooseMove, and passes in WSL. The full sandbox suite is **18/18** on WSL/Windows (the Windows containment test returns because AppContainer is not wired).

## Residual risk

- No seccomp/Landlock syscall filtering; model code can call allowed syscalls in the chroot.
- User namespaces must be enabled; otherwise OS containment fails closed.
- System libraries/locales are exposed read-only.
- **Windows AppContainer is not implemented**, so C1 is not closed on Windows.
- The client must opt in; production routing is still blocked by G2/C1.
