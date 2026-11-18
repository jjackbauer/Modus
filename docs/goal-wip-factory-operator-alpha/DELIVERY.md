# Wip.Factory Operator Alpha — delivery record

**Status:** implementer side complete; **product acceptance not met** (PA-01/PA-02 need requirements withheld from me).
Baseline: `feature/wip-factory-alpha`, frozen for acceptance. Spec: `harness/requirements/Wip.Factory.Operator.Alpha.v0.3.md`.

---

## What was built

An operator submits a requirement document; the factory builds the application, verifies it against a frozen
HTTP acceptance contract it did not write tests for, obtains a separate merge authorisation, merges, re-verifies the
**merged** revision, and can supervise it. No per-application C#.

Four distinct paths now run end to end through the public CLI:

| Requirement shape | Outcome |
|---|---|
| Book catalog (CRUD: 201/200/204/404) | `Released` → `OperationallyValidated` |
| Job queue (FIFO, state transitions, 204 empty set, 404 unknown) | `Released` v13 → served → `OperationallyValidated` v15 |
| Ping service (single endpoint, explicit contract) | `Released` → `OperationallyValidated` |
| Ping service via **approved template** (`template: ping-service@1`) | `contract approve --policy` → `OperationallyValidated` v15 |

## Operator commands

```text
factory requirement submit --file <req.md> --repo <dir> [--control-root <dir>]
factory contract show   --work-item <id>
factory contract approve --work-item <id> --digest <sha256:...>      # human
factory contract approve --work-item <id> --policy                   # D-09 template policy
factory run --until-idle
factory status
factory approve --work-item <id> --review-digest <sha256:...>
factory serve --work-item <id> [--duration-seconds N]
factory template register --file <manifest.json>
factory template approve --id <id> --version <n> --digest <sha256:...>
```

## Gate status

| Gate | Status | Basis |
|---|---|---|
| **PA-03** diff = merged revision | **demonstrated** | recorded `mergedSha` equals `git rev-parse HEAD`; post-merge contract on the merged tree |
| **PA-04** wrong candidate rejected | **demonstrated** | oracle rejected a real model mistake (`POST` where the contract required `GET`) and a wrong stored value |
| **PA-05** kill/resume, no duplicate effects | **demonstrated** | killed pre-merge (0 merges → resume 1) and post-merge (1 merge → resume still 1, no false release) |
| **PA-06** approval binding | **met** | 8 bound facts, each refusing independently; matching evidence accepted |
| **PA-07** served identity + contract | **demonstrated** | factory-allocated port, owned pid, served sha == merged sha, scenarios passing while served |
| **PA-08** tool refusals | **demonstrated at tool level** | 9 negative tests: absolute/traversal paths, patch conflict atomicity, subcommand allowlist, literal metacharacters, no-worktree, fail-closed isolation |
| **PA-01 / PA-02** | **NOT MET — blocked** | require two requirements **withheld from the implementer**, supplied after freeze |

## Structural gaps found by auditing my own delivery, and closed

Each was discovered by measurement, not assertion — see `named-test-map.md`:

1. **No host lock** — single-writer safety was claimed and unenforced. Now an exclusive `FileShare.None` handle; a second run exits `host-lock-held`.
2. **No claim reservation** — `ClaimUnpublishedAsync` returned events without recording ownership. Now durable subscriber, deadline and attempt count, replayed on load.
3. **No operation ledger** — reconciliation lived ad hoc in the merge engine. Now a durable `pending → applied | failed` record on the merge path.
4. **D-09 template policy** — implemented, tested against eight deviations, and reachable from the CLI (`template register|approve`, `contract approve --policy`).
5. **D-08 A edge absent** — the state machine rejected `DeliveryValidation → NeedsRework`. Now legal and taken on pre-merge failure.

## Honest limitations

- **26 of 80 named tests exist** by their specified name (measured via `dotnet test --list-tests`). The gap is mapped in `named-test-map.md`: roughly 6 are renames, the rest were missing mechanisms — most now built, but the names are still absent. **This is not acceptance.**
- **PA-05** tested the post-merge and pre-merge kill windows; a kill *during* the git merge itself is untested.
- **PA-08** is demonstrated at the tool boundary; the spec's full refusal set is broader.
- **Template policy** approves contracts, never merges — asserted by test.
- The review validity window is unconfigured (v0.3 marks it optional).
- Application class is narrow by design: single-repo ASP.NET Core HTTP on .NET 10.

## Reproducing

```powershell
dotnet build Wip.slnx                     # 0 errors
dotnet test tests/Wip.Architecture.Tests  # 5/5
dotnet test WIP/Wip.Factory.Core.Tests    # 21/21
dotnet test WIP/Wip.Factory.Agents.Tests  # 69/69
dotnet test WIP/Wip.Factory.Host.Tests    # 68/68   → 163 passing
```

A full operator run needs a target git repository, a requirement `.md`, an optional `contract.json`, and model
credentials in `.secrets/deepseek.env` (or `DEEPSEEK_API_KEY` / `DEEPSEEK_BASE_URL`).

## What acceptance requires

Two requirement files I have not seen, run against this binary with **no source or configuration change**:

```markdown
---
title: <anything>
supportedClass: aspnetcore-http-net10
acceptanceContract: contract.json      # optional; template: <id>@<version> also works
---

<body>

## Acceptance expectations
- <observable behaviour>
```

plus an optional `contract.json` (§4.1 schema: `method`, `path`, `request.json`, `expect.status`,
`expect.jsonSubset`, `capture`).

Those two runs are PA-01 and PA-02. Until they exist, the honest answer to "is this a usable factory?" is
**unknown** — the machinery works, and generalisation is unproven.
