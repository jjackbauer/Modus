# WP-1.5 — security CI attempt (2026-11-03)

Run `41638523013` on `deb12e8`:

- `wip-security` (Windows) failed at **Set up job** before checkout: GitHub could not download `actions/setup-dotnet@v4` from codeload (`Failed to download archive .../setup-dotnet/zip/...`). This is an Actions infrastructure/download failure, not a test failure.
- `wip-security-linux` was still running at the time of inspection; other WIP jobs were in progress.
- Local suite: 16/16 sandbox tests pass; 16-project gate green locally.

Retry by pushing the next commit / rerunning the workflow. No source change addresses the codeload failure.
