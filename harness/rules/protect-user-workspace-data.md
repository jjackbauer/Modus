---
kind: rule
mode: agent
description: Never delete or destroy user workspace data. Check before running destructive commands. Use temp directories for test artifacts.
---

# Rule: Protect User Workspace Data

## Absolute Prohibition

**Never delete, overwrite, or destroy user workspace data.** This includes:

- `Remove-Item -Recurse` on the project's data directories
- `rm -rf` on any path inside the workspace
- `git clean`, `git reset --hard`, or any command that discards user changes
- Deleting session data, database files, or runtime artifacts the user has accumulated

## Required Checks Before Any Destructive Command

1. **Read the path first** — use `ls` or `read` to confirm what will be affected.
2. **Ask the user** if the command will delete anything they care about.
3. **Use temp directories** for test artifacts — never dump test output into the user's working data directory.
4. **Prefix test commands with safeguards** — e.g., only delete data within a temp path you created, not the user's configured data directory.

## When Running Integration Tests

- If the app has a `DataDirectory` config (e.g., `bones-data`), **never delete it**. The user may have days of accumulated session data.
- Instead, set a different `DataDirectory` via environment variable or config override for your test run.
- If you must clean up, only delete what you explicitly created in a temp location.

## Violation Example (DO NOT DO)

```powershell
# WRONG — this deletes the user's accumulated data
cd WIP/Wip.Bones.Host
Remove-Item "bones-data" -Recurse -Force
dotnet run
```

## Correct Approach

```powershell
# RIGHT — use a temp directory for the test run
$env:BonesHost__DataDirectory = "$env:TEMP/bones-test-data"
dotnet run
# Cleanup only the temp data after
Remove-Item "$env:TEMP/bones-test-data" -Recurse -Force
```
