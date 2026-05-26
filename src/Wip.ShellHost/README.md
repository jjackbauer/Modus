# Wip.ShellHost Contributor README

Wip.ShellHost is the startup composition root for interactive shell execution. It resolves effective configuration, creates the runtime and diagnostics bridge, and wires host-only commands into the shell loop.

Dependency note: shell command dispatch semantics are owned by Wip.Shell. See src/Wip.Shell/README.md for command and failure contracts.

## Host Startup Composition

`Program.cs` creates host options using:

`WipShellHostOptions.FromArgs(args, Directory.GetCurrentDirectory())`

Factory wiring in `WipShellHostFactory` then composes:

1. `ModusWipBridge` for plugin loading and diagnostics access.
2. `WipRuntimeOrchestrator` with in-memory session store and no-op publisher for shell host runtime.
3. `WipShellCommandLoop` with a host custom-command handler.
4. `WipShellEngine` + `WipShellHostContainer` for lifecycle management.

## Usage

### Quick Start

Run the shell host from repository root:

```powershell
dotnet run --project src/Wip.ShellHost/Wip.ShellHost.csproj
```

Run with an explicit plugins path (first non-option argument):

```powershell
dotnet run --project src/Wip.ShellHost/Wip.ShellHost.csproj -- .\plugins
```

Run with startup mode override:

```powershell
dotnet run --project src/Wip.ShellHost/Wip.ShellHost.csproj -- --startup-mode=autoload
```

Supported startup mode values:

- `explicit`, `explicit-command`, `explicit-command-only`, `manual`
- `autoload`, `auto-load`, `auto`

Startup behavior:

1. Host resolves effective configuration from defaults, optional `.wip/config.json`, and CLI overrides.
2. In `autoload` mode, plugin loading is attempted before interactive loop starts.
3. In explicit mode, plugin loading occurs only via shell command path.
4. `Ctrl+C` requests graceful shutdown and returns exit code `0`.

### Typical Interactive Flow

After startup, use a contributor-friendly flow:

1. `help`
2. `config`
3. `plugins`
4. `workflows`
5. `start <workflow-id> <repository-path> <worktree-path>`
6. `status`
7. `transition <Created|Editing|Validating|AwaitingApproval|Approved|Merged>`
8. `detach`
9. `exit`

For command syntax and deterministic shell-level error contracts, see `src/Wip.Shell/README.md`.

## Host Command Extensions

The host injects two aliases handled by `TryHandleHostCommandAsync`:

| Command | Usage | Output contract |
|---|---|---|
| `config` | `config` | Prints effective configuration fields (source, policy, plugins path, workspace root, validation commands). |
| `effective-config` | `effective-config` | Alias of `config` with identical output shape. |

Failure contract:

- `config` or `effective-config` with extra arguments prints `Usage: config` and does not mutate runtime state.

## Configuration Precedence

`WipShellHostOptions.FromArgs` applies deterministic precedence in this order:

1. Defaults:
   - `pluginsPath = <currentDirectory>/plugins`
   - `workspaceRoot = <currentDirectory>`
   - `policyId = local-safe`
   - `validationCommands = [dotnet build, dotnet test]`
2. Repository config override from `.wip/config.json` when the file exists.
3. Explicit CLI plugin path override via first non-option argument.

Effective config output always includes:

- `source: <(defaults) or absolute .wip/config.json path>`
- `policy: <effective policy id>`
- `pluginsPath: <effective plugin path after precedence resolution>`
- `workspaceRoot: <effective workspace root>`
- `validationCommands: <joined command list>`

Contributors changing precedence semantics or output field names must update this README and tests in tests/Wip.Shell.E2E.Tests.