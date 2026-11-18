# Goal — Wip.Factory Operator Alpha v0.3

## Verbatim user request

`/stretch-goal-harness` followed by the **Wip.Factory Operator Alpha — Requirements Specification v0.3**,
whose full normative text is committed at `harness/requirements/Wip.Factory.Operator.Alpha.v0.3.md`.
Ratified approvals in that request: **D-08 A** (exactly one new edge `DeliveryValidation → NeedsRework`,
pre-merge only) and **D-09** (policy approval only for explicitly preapproved immutable templates, with
human fallback).

## 0. Completeness rule (supersedes the v1 contract)

This contract **incorporates the v0.3 normative body clause by clause**. The eight PA gates are the
*terminal product proof*, not a menu: a clause in v0.3 §§0–15 that has no PA gate is **still binding**,
and its named supporting test (§12.2–§12.3) is its proof. Nothing is satisfied by being named.

An earlier version of this file failed an independent intent audit by dropping two mandatory mechanisms
and by allowing a failed gate to close the goal. That audit is at `audit-intent.md`; its findings are
adopted in full and corrected here.

## 1. One-sentence outcome

The unchanged Modus/WIP factory host accepts two independently withheld, behaviourally different
supported requirements through its public CLI, builds attributable applications with real model tool
calls, passes immutable independently approved HTTP contracts plus existing regression gates,
authorises and reconciles an actual merge, verifies the merged revision, and can supervise it with
`factory serve` — while rejecting a deliberately incorrect candidate.

## 2. Definition of done — PA-01 … PA-08 with explicit pass predicates

| Gate | Pass predicate (all conjuncts required) |
|---|---|
| **PA-01** | `factory requirement submit` accepts an unfamiliar supported requirement; production agents produce a non-empty attributable diff; build + existing regressions green; frozen contract green on the pre-merge candidate; separate merge approval; real merge; **post-merge** contract green on the merged revision; work item reaches `Released`; full manifest emitted. **Serve is not part of PA-01** (see DEC-002). |
| **PA-02** | A second, behaviourally different requirement passes PA-01's predicate using the **same binary and configuration**, with no source edit, no app-specific adapter, and no contract reuse beyond the template policy. |
| **PA-03** | `git diff base..head` non-empty; durable chain `workItemId → sessionId → profileVersion → stageAttemptId → baseSha → headSha → diffDigest` resolvable from the store; merged SHA/tree contains the reviewed change (byte-identical diff digest). |
| **PA-04** | A deliberately incorrect candidate whose own tests pass fails ≥1 frozen scenario; work item does not reach `ReadyForDelivery`; no merge, no release, named category recorded. |
| **PA-05** | Kills injected during implementation, during merge, and during verification; after restart: no duplicate merge, no corrupt state, **no false `Released`**, and **no lost failure record**. |
| **PA-06** | Each approval-bound category mutated independently (repo/target/base/head, requirement+contract digest, review digest+four check results, policy/guidelines versions, profile+tool runner versions, approval actor/time/window) → merge refused with `approval-stale`. |
| **PA-07** | `serve` owns the process tree for the pinned release artifact on a factory-allocated loopback port; PID **and start-time** bound; served SHA reported and equal to the merged SHA; frozen contract+health probe green on the served process; a stale listener on a reused port is refused as an impersonator. |
| **PA-08** | Refused with named categories: a tool outside the pinned profile; absolute/traversal/symlink-or-junction path escapes; a `dotnet` subcommand outside the §6.1 allowlist; missing OS resource controls (`isolation-unavailable`); and any attempt by an agent to write, reorder, drop or skip a frozen contract scenario. |

## 3. Mechanism commitments (all 18, full fidelity)

Every row is binding. "Proof" names the command or test that falsifies it.

| # | Mechanism | Proof |
|---|---|---|
| 1 | PA-01 … PA-08 as above | manifest per gate, §1.2 fields |
| 2 | **Six tools exactly**: `list_files`, `read_file`, `search_text`, `apply_patch`, `write_file`, `run_command` | tests 26–34, 75–76; a missing tool fails the suite; an extra tool fails the ceiling test |
| 3 | `run_command` = trusted `dotnet` binary + argument vector, subcommands `new/restore/build/test/run/publish` only | tests 32, 33, 34 |
| 4 | Frozen contract: canonical bytes + digest in control root, digest-pinned before implementation, agent-unwritable | tests 6, 8, 42, 52, 59–61 |
| 5 | Contract approval ≠ merge approval | tests 49, 58; PA-06 |
| 6 | D-09 policy approval: only exact preapproved immutable template+version+validated params; human fallback; never merge | tests 45–48, 53–58; 8 negative cases |
| 7 | `run` verifies and stops; `serve` supervises and blocks; `run` never claims deployed | tests 39–40, 72–74; PA-07 |
| 8 | Effectively-once: persisted reservation, `stageAttemptId`+`effectOrdinal` operation IDs, `pending→applied/failed`, reconciliation table, cross-process store lock | tests 21–25, 62–67; PA-05 |
| 9 | `Recorded`/`ProgressWithoutTransition` stage outcome carrying durable facts without a state change | tests 42, 43 |
| 10 | Fail-closed isolation: `IsolationApplied=false` ⇒ `isolation-unavailable` **even with** `--trusted-environment`; the flag acknowledges MSBuild/filesystem/network residual risk and never relaxes resource bounds | tests 31, 77; PA-08 |
| 11 | **D-06 control root** `<controlRoot>/…` outside target repo and all worktrees, symlink/junction-proof; refuses `control-root-inside-target` | test 14; start-up check |
| 12 | **D-04 three model-driven roles** (Requirements, Implementations, Review) by default; nine available; `ArchitectureRefined`/`ReadyForImplementation` reached by deterministic checks, not extra LLM turns | test 20; dispatch log shows exactly three model roles on the default path |
| 13 | Operator CLI surface as §8 incl. `requirement submit`, `contract show/approve/reject`, `run --until-idle`, `status`, `show`, `approve`, `serve`, `template *`; stable exit categories | tests 11–15, 43–44, 78–79 |
| 14 | Evidence manifest §1.2 with per-gate pass/**fail**/inconclusive, host SHA, model/provider ids, digests, operation ids, timings | every PA gate emits one; a missing field is not a pass |
| 15 | Durable intake: `factory.requirement.submitted.v1` published and consumed; proposal persisted without transition | tests 1–5, 41 |
| 16 | D-08 A: exactly one new edge, pre-merge only; post-merge failure ⇒ `Failed`, never `NeedsRework` | tests 47, 48; adjacency contract test |
| 17 | Review evidence integrity: four checks bound to factory-executed results, never an agent `Passed=true` | tests 68–71; PA-06 |
| 18 | No second model loop / tool gateway / worktree engine / merge policy (N-02); no sandbox or exactly-once claims (N-09) | architecture tests stay 5/5; docs contain no such claim |

## 4. Hard constraints

- **C-01** Existing factory suites stay green, re-run and recorded per N-01; fresh baseline is 86 passing,
  build clean (EVD-008).
- **C-02** No mechanism may be satisfied by naming it: each has an executable test that fails under a
  deliberately introduced representative defect.
- **C-03** The control root never resolves inside the target repo or a worktree, including via symlinks.
- **C-04** `--trusted-environment` never disables resource bounds and never converts a failed isolation
  into a success.
- **C-05** No new `FactoryWorkState` member; no new edge other than D-08 A.
- **C-06** Secrets never persisted; redaction failures recorded, no claim of provable absence.
- **C-07** Typed identities for new domain IDs (N-06).

## 5. Interpretations adopted (from the audit)

- **DEC-002 — PA-01's pass predicate ends at `Released`.** v0.3:58 mentions "`serve` runs and proves the
  intended release" while v0.3:43 states `Released` is explicitly *not* evidence of a persistent process
  and v0.3:44/116 reserve liveness for `OperationallyValidated`. One gate cannot span two terminal states.
  Resolution: PA-01 = merged and post-merge verified (`Released`); liveness is PA-07. This keeps serve
  mandatory overall via commitment 7 and PA-07 rather than dropping it.
- **DEC-003 — PA-06 and PA-08 are proven through harness-driven injection seams** (mutating a bound
  field, requesting an out-of-profile tool), consistent with v0.3:479 "CLI and runtime". The seam is a
  test-only capability that cannot be reached from the operator CLI; it exists so these gates are
  falsifiable at all.
- **DEC-004 — A failed PA gate means NOT ACCEPTED.** v1 allowed "honestly recording a failing gate" as a
  stop condition; that is withdrawn. Reporting a failure is permitted and required, but it never
  constitutes acceptance, and the goal is not complete.

## 6. Stop criteria

1. **Accepted** only when all eight PA gates pass with manifests, and C-01 holds.
2. If a gate fails, the goal is **not** met: report the failing gate as a blocker with its evidence. That
   is a status report, never a completion.
3. Round cap 30. Stop on acceptance — not because the cap approached.
4. Declare `blocked` only after the same concrete condition blocks three consecutive rounds.

## 7. Open questions

- **OQ-001** (open, user-owned, needed at freeze only): the two blind PA-01/PA-02 requirements must be
  supplied after the implementation is frozen. Until then PA-01/PA-02 stay **unclaimed**; development
  fixtures do not substitute.
