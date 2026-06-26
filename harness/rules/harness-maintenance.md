---
kind: rule
mode: agent
description: Maintain the harness as the single source of truth for agent capabilities and keep generated IDE stubs in sync.
---

# Rule: Harness Maintenance

## Purpose

The `harness/` directory is the **canonical source of truth** for all agent capabilities in this repository. Generated stubs under IDE-specific paths must never be edited by hand.

## Directory Layout

| Path | Purpose |
|---|---|
| `harness/skills/` | Atomic capabilities (falsify-claims, test-standards, journal-observe, …) |
| `harness/workflows/` | Multi-step orchestrators (requirements-gathering, iterative-implementation, codebase-exploration, …) |
| `harness/rules/` | Policy presets and always-on rules |
| `harness/schemas/` | Reference-only schemas (journal, distilled knowledge, codebase patterns, requirements template) |
| `harness/requirements/` | Active requirements docs and new transition-proofs/evidence |
| `harness/archive/journals/` | Dormant exploration journals |
| `harness/archive/distilled/` | Dormant distilled knowledge documents |
| `harness/archive/lovelace/` | Archived Lovelace migration context (not active) |

## Generated Outputs (Never Hand-Edit)

HarnessSync generates these from canonical bodies:

- `.cursor/commands/*.md`
- `.claude/commands/*.md`
- `.github/prompts/*.prompt.md` (active prompts only; excludes `archived-lovelace/`)

## Hand-Authored Exceptions

These files are maintained separately and are **not** overwritten by HarnessSync:

- `.cursor/rules/*.mdc` — glob auto-attach rules (e.g. `no-primitive-obsession.mdc` asserted by `harness/rules/no-primitive-obsession.md`)
- `.claude/agents/*.md` — subagent definitions for tools that support named subagents

## Canonical Body Frontmatter

Every harness capability file begins with YAML frontmatter:

```yaml
---
kind: skill | workflow | rule | schema
mode: plan | agent          # omit for schema
description: <short summary>
subagents:                  # optional
  - <name>
composes:                   # optional — relative paths to composed bodies
  - ../skills/falsify-claims.md
assertMdc: <name>            # optional — only for rules that assert a .mdc rule
---
```

### Field Rules

- **kind** — required; one of `skill`, `workflow`, `rule`, `schema`.
- **mode** — required for skill/workflow/rule; `plan` for planning-only flows, `agent` for execution flows. Omit for schema reference docs.
- **description** — required; one-line summary used in generated stub frontmatter.
- **subagents** — optional list of named subagent roles (e.g. `falsifier-a`, `execution`, `verifier`). Document tool-neutral fallback in the body: use parallel subagents when available; otherwise run independent passes.
- **composes** — optional list of **relative paths** from the current file to other harness bodies that were previously `#file:` includes.
- **assertMdc** — optional; when set, HarnessSync must ensure a matching `.cursor/rules/<name>.mdc` exists and stays aligned.

## Add or Modify a Capability

1. Edit the canonical body at `harness/<kind>/<name>.md`.
2. Update `composes` when adding or removing composed references.
3. Run code generation:
   ```bash
   dotnet run --project tools/HarnessSync -- generate
   ```
4. Verify sync state before committing:
   ```bash
   dotnet run --project tools/HarnessSync -- --check
   ```
5. Commit **only when `--check` passes**. Include both canonical harness changes and generated stub updates in the same commit.

## Path Conventions in Bodies

When editing harness bodies, use these paths (not legacy `.github/prompts` paths):

| Legacy | Canonical |
|---|---|
| `.github/requirements/` | `harness/requirements/` |
| `.github/journals/` | `harness/archive/journals/` |
| `.github/distilled/` | `harness/archive/distilled/` |
| `.github/prompts/codebase-patterns.md` | `harness/schemas/codebase-patterns.md` |
| `.github/prompts/journal-schema.md` | `harness/schemas/journal-schema.md` |
| `.github/prompts/distilled-knowledge-schema.md` | `harness/schemas/distilled-knowledge-schema.md` |

Frozen transition-proof history under `.github/requirements/transition-proofs/` is immutable — do not rewrite archived proofs when migrating active docs.

## Invoke Capabilities

Use `/name` slash commands in Cursor, Claude Code, or GitHub Copilot (e.g. `/falsify-claims`, `/requirements-gathering`). Slash command names derive from harness file names (without directory prefix).

## Evidence and Exploration

- **Active requirements/evidence**: `harness/requirements/`
- **Frozen history**: `.github/requirements/transition-proofs/` (immutable)
- **Archived exploration**: `harness/archive/`
- **Convergence metrics**: `.github/state/convergence-metrics.md`

## Quality Gates Before Merge

1. HarnessSync `--check` passes.
2. No hand-edits in generated stub directories.
3. New requirements docs follow `harness/schemas/requirements-doc-template.md` structure.
4. Behavior-proof and format-gate sections are present for requirements outputs produced by workflows.
