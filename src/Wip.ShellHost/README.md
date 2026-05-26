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