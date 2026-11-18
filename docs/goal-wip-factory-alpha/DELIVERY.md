# Wip.Factory Functional Alpha — Delivery

**Status: delivered and verified.** All nine definition-of-done dimensions are met. The durable goal
record remains open only because of a harness defect described at the end — not because of unfinished work.

Date: 2026-11-18 · Goal `goal-599bfa8a` · Baseline commit `0b497ae`

---

## What was built

Fifteen projects: twelve implementation assemblies and four test assemblies under `WIP/`.

| Project | Role |
|---|---|
| `Wip.Factory.Contracts` | Typed identifiers, events, work/artifact/profile/blackboard/telemetry/scheduling/admission contracts |
| `Wip.Factory.Core` | Work-state machine, dependency readiness, dispatch worker, operator surface |
| `Wip.Factory.Persistence` | Local durable store: one append-only log, inbox, leases/fencing, schedule occurrences |
| `Wip.Factory.Blackboard` | Six-view role-scoped context resolution with provenance and budgets |
| `Wip.Factory.Wip` | Dispatcher, worktree provisioner, review/approval/merge governance, rework policy, bounded process runner, delivery environment, role runner |
| `Wip.Factory.Modus` | Plugin → `WipBuilder` capability binding, `IPluginScheduler` schedule source, telemetry signal source |
| `Wip.Factory.Admission` | Executable capability admission lifecycle with pinned sessions |
| `Wip.Factory.Adaptation` | Role pack, immutable profile registry, improvement plane, gate-write guard |
| `Wip.Factory.Telemetry` | Sanitized session traces, failure clusters, outcome attribution |
| `Wip.Factory.Analysis` | Bounded `lovelace-run` adapter and prediction/observation comparison |
| `Wip.Factory.Agents.Core` | Role library seam |
| `Wip.Factory.Host` | Composition root and the five demo verbs |
| `Wip.Factory.Core.Tests`, `.Agents.Tests`, `.Host.Tests` | 81 behavior tests plus the compliance gate |

---

## Verification

```
dotnet build Wip.slnx                        → 0 errors
tests/Wip.Architecture.Tests                 → 5 / 5   (six factory-* layering rules)
WIP/Wip.Factory.Core.Tests                   → 18 / 18
WIP/Wip.Factory.Agents.Tests                 → 58 / 58
WIP/Wip.Factory.Host.Tests                   → 5 / 5   (includes the compliance gate)
                                    total    → 86 tests, 0 failures
```

The ten pre-existing core WIP suites remain at **460 passed / 3 failed** — byte-for-byte the recorded
baseline from before this work began. The three failures are pre-existing (two `Wip.Modus` documentation
tests, one flaky `Wip.Runtime` cancellation test) and are not caused by this goal.

**The compliance gate** parses `harness/requirements/Wip.Factory.Alpha.md`, extracts all 82 named tests,
and reflects over four built assemblies requiring each name to resolve to a real xUnit test. It is green.

---

## The five demos

| Verb | Exit | What it proves |
|---|---|---|
| `factory demo requirement-to-merge` | 0 | Requirement → worktree → real `dotnet` build/test → four-check review → digest-bound approval → real `git merge`; implementation present on `main` |
| `factory demo nine-role-dispatch` | 0 | 9/9 roles dispatched, schedule occurrence and telemetry signal triggers, release rollback genuinely exercised (v3 refused, v1 restored and re-probed) |
| `factory demo improvement-cycle` | 0 | Reflection → immutable hypothesis → candidate without pointer move → `Promoted` (sample 12, CI [165,595]) → gate-regressing candidate rejected → escalation denied |
| `factory demo analysis-comparison` | 0 | Real `lovelace-run` child process; bottleneck `implementation`, bound `0.0556` decoded from the exact repeating decimal `0.0(5)`; signed comparison artifact |
| `factory demo admission-lifecycle` | 0 | inspect → resolve → admit → health → activate; pinned session keeps its version across promotion; lease-blocked drain; rollback retains; self-install refused |

Run any of them with:

```powershell
dotnet run --project WIP/Wip.Factory.Host -- factory demo <verb> <output-directory>
```

`factory demo real-model-role` additionally drives one role through the **real DeepSeek provider**
(`deepseek-chat`, 17 input / 3 output tokens) via the production execution port.

---

## Design commitments, and where they are enforced

| Commitment | Enforcement |
|---|---|
| No second model loop / tool gateway / worktree engine / merge policy | Dispatch delegates to `WipRuntimeOrchestrator.RunAgentAsync`; worktrees via `WipWorkspaceProviderGit`; merges via the guarded gate — six `factory-*` architecture rules are green |
| No permanent supervisor agent | `FactoryDefinitionGuard` rejects any definition holding dispatch authority (test 81); the dispatcher is a deterministic outbox worker |
| Durable state authorizes work | State and events commit in one atomic append; inbox, leases and monotonic fencing tokens; torn records remove both halves |
| Agents improve methods, not authority | Profile publish refuses permissions outside the definition ceiling; approval binds seven fields; reflection cannot write gates, holdouts or its own ceiling |
| Untrusted execution bounded | Real job-object memory and process limits plus wall time (30 s sleep killed at 2 s; 300 MB refused under 64 MB) |
| Reuse over reimplementation | The isolation runner composes WIP's existing `WindowsJobObject`; no second job object, model loop or merge policy exists |

---

## Known limitations, recorded rather than hidden

- **OQ-005** — `analysis-comparison` reports `ExplanationHeld=false`. The mechanism is real and reports
  honestly, but the demo fixture's observed throughput (0.42/s) is physically impossible given its
  service demand (18 s on one server), so a *successful* explanation is not demonstrated. Tuning the
  fixture until it agreed was rejected.
- **RISK-005** — poison-event attempt counts are in memory, so a restart resets the threshold;
  quarantine itself is durable.
- **RISK-008** — five occasions where a new identity field was written as `string`; every one was
  caught by the typed-identifier gate, none by review.
- **Deferred** — the contributor README project-map rows for the twelve new projects are preserved in
  [`contributor-readme-patch.md`](contributor-readme-patch.md) rather than applied, because
  `src/WIP.Contributor-Architecture.README.md` lies outside this goal's scope. The repository's own
  documentation gate is already red at `HEAD` for 42 undocumented `Wip.*` projects.

---

## Why the goal record is still open

`update_goal action=complete` is refused twice over:

1. **`baseline_scope` is immutable after creation.** `action=edit` accepts only `objective`,
   `maxGoalRounds` and `dependsOn`; a scope edit is silently ignored. The declared scope was written
   relative to the wrong root and cannot be corrected.
2. **The scope check counts files outside this goal.** Of 903 changed/untracked paths it rejects, 798 are
   pre-existing untracked experiment data under `docs/goal-tholdem-enhancement/experiments/**/data/`
   from an earlier, unrelated goal — present in the very first `git status` of this session. Only 105 are
   this goal's own files, all of them inside the declared scope roots. The check also rejects paths
   plainly inside `WIP/` while `WIP/` is itself in the scope, which suggests prefix matching is not
   applied.

Deleting another goal's data, staging it, or editing `.gitignore` would all be scope violations or
destructive acts taken to satisfy a checker. The work is complete and reproducible from the commands
above; only the bookkeeping flag cannot be set.

**Suggested harness fixes:** resolve `baseline_scope` against the repository root rather than the session
working directory, apply directory-prefix matching, make scope editable via `action=edit`, and exclude
paths that are untracked but belong to other goals.
