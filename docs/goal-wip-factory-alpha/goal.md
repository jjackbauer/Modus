# Goal: Wip.Factory Functional Alpha — build to completion

## Verbatim request

> "/stretch-goal-harness build it till completion"

Context, recorded because it defines "it": the deliverable is the requirement set at
`harness/requirements/Wip.Factory.Alpha.md` — 89 checklist items (F-A…F-L), 66 named behavior-proof
tests, increments Alpha-0/1/2 — which the same user scoped on 2026-11-18 with approved decisions
D-01 (full minimal improvement loop), D-02 (transport-neutral durable store + local implementation),
D-03 (all nine roles dispatchable), D-04 (RustLace in alpha with one real prediction).

## Outcome (one sentence)

A `Wip.Factory` alpha exists in this repository in which all 105 requirements are implemented, all
82 named behavior-proof tests are discovered and passing, the six `factory-*` architecture rules are
live, and the nine named roles plus the bounded improvement loop, the executable-capability admission
lifecycle and the RustLace advisory prediction execute through real Modus/WIP seams — proven by
`dotnet test Wip.slnx` exiting 0 with no pre-existing test removed, weakened or skipped.

## Definition of done — falsifiable checks

| ID | Check | Command | Expected observable |
|---|---|---|---|
| **D1** | The eleven factory projects exist and the solution builds | `dotnet build Wip.slnx -v q --nologo` | exit `0`; `0 Error(s)`; the built project set contains `Wip.Factory.Contracts`, `.Core`, `.Persistence`, `.Blackboard`, `.Wip`, `.Modus`, `.Adaptation`, `.Telemetry`, `.Host`, `.Agents.Core`, `.Analysis` |
| **D2** | The architecture gate is live and factory-aware | `dotnet test tests/Wip.Architecture.Tests --nologo` | exit `0`; all six `factory-*` rule ids present in `ArchitectureRules.Rules`; the project-count assertion is derived from the solution file, not the literal `48` |
| **D3** | Every named test exists, is real, and passes | `dotnet test WIP/Wip.Factory.Host.Tests --nologo` (plus `Wip.Factory.Core.Tests`, `Wip.Factory.Agents.Tests`) | exit `0`; a compliance test parses `Wip.Factory.Alpha.md`, extracts all 82 backticked test names, and asserts each resolves by reflection to an `[Fact]`/`[Theory]` in one of the **three** declared factory test assemblies; **0 missing, 0 skipped**; and a companion scan rejects any of the 82 whose body contains no assertion on a value produced by executed code |
| **D4** | No **new** regression against the recorded red baseline | `dotnet test` per core suite + `dotnet test Wip.slnx --nologo` | total passed `>=` 460 (EVD-017); total skipped `<=` baseline skipped; and the set of failing tests is a **subset** of the three recorded baseline failures — `Wip.Runtime.Tests.DeepSeekModelProviderTests.DeepSeekProvider_GivenCancellationRequested_ExpectedRequestAbortedWithoutBackgroundContinuation`, `Wip.Modus.Tests…WipContributorArchitectureReadme_GivenRepositoryStructure_MapsEveryWipProjectToRuntimeRole`, `Wip.Modus.Tests…WipContributorArchitectureReadme_GivenArchitectureMap_ContributorsCanTraceSessionCommandToRuntimeComponents` (EVD-015, DEC-004) |
| **D5** | The Alpha-0 spine runs end to end | `dotnet run --project WIP/Wip.Factory.Host -- factory demo requirement-to-merge` | exit `0`; a work item reaches an operator-approved merge; the evidence directory contains the requirement, architecture, implementation, review, approval and merge artifacts with a populated provenance chain, and the review artifact names all three independent checks (validation, coverage, conformance) plus the security gate |
| **D6** | The improvement loop is conjunctive, not disjunctive | `dotnet run --project WIP/Wip.Factory.Host -- factory demo improvement-cycle` | exit `0`; **all** of: a reflection artifact; a candidate profile version; an evaluation decision reporting sample size, confidence interval **and** missing-downstream-data flags; a **promotion that takes effect**; a **second candidate that is rejected or rolled back**; and one denied escalation |
| **D7** | RustLace prediction is compared against observed traces | `dotnet run --project WIP/Wip.Factory.Host -- factory demo analysis-comparison` | exit `0`; a prediction artifact and a signed comparison artifact are produced by a real `lovelace-run` child process invocation, and the analysis capability is resolved through the capability catalog by an analytical profile |
| **D8** | All nine roles dispatch, not six | `dotnet run --project WIP/Wip.Factory.Host -- factory demo nine-role-dispatch` | exit `0`; each of the nine roles is dispatched from its own trigger and each writes its declared artifact, including Deliver Analyst, Release Manager (with an exercised rollback), Strategic Architecture Analyst (from a scheduled occurrence) and Operations Analyst (from a telemetry signal) |
| **D9** | Executable-capability admission lifecycle works | `dotnet run --project WIP/Wip.Factory.Host -- factory demo admission-lifecycle` | exit `0`; a candidate package is inspected (hash, deps, API version), health-tested in isolation, admitted, promoted, drained with a live lease, and rolled back to the prior version — and a reflection self-install attempt is refused and recorded |

D1–D4 are the build/regression floor. D5–D7 are the "functional" and "alpha" claims. A check may
not be satisfied by a stub: see the anti-stub rules below.

## Hard constraints

| ID | Constraint | Provenance |
|---|---|---|
| K1 | Nine roles dispatchable; improvement loop; RustLace with one real prediction; transport-neutral durable store with a local implementation | user (D-01…D-04) |
| K2 | Reuse WIP/Modus: no second model loop, tool gateway, worktree engine or merge policy | user/source (M-19) |
| K3 | `Wip.Factory.*` may not reference `Wip.Bones*` | agent (C-03), accepted because Bones' evaluator takes a game simulator by constructor |
| K4 | Operator approval mandatory for merge and release; no unattended merge | agent (C-02), from source §9.2 |
| K5 | `lovelace-run` invoked only as a bounded child process | agent (C-04) + repo (no RustLace project reference exists) |
| K6 | No database dependency; durable store is local | repo (no DB dependency in solution) + user (D-02) |
| K7 | All text files UTF-8 **without BOM**; new C# follows `readonly record struct` identifier rule | repo (`AGENTS.md`, `harness/rules/no-primitive-obsession.md`) |
| K8 | xUnit naming `MethodName_GivenScenario_ExpectedResult` | repo (`AGENTS.md`) |
| K9 | A passing `dotnet build`/`dotnet test` reported by an agent is a hypothesis until the orchestrator re-runs it | skill axiom 5 |
| K10 | A scripted model provider is a unit-test double and is never evidence that a model drove the factory | skill, and `Wip.Factory.Alpha.md` test 66 |

## Anti-stub rules (how D3–D7 could be faked, and what stops it)

| Fake | Detected by |
|---|---|
| Test exists but asserts only `NotNull` / metadata | D3 companion scan rejects a test body with no `Assert` on a value derived from executed code |
| Test passes because the implementation is a stub | Each gate test must fail if its target body is replaced with `throw new NotImplementedException()`; spot-checked per round |
| Demo command prints a fabricated report | D5–D7 require artifact files on disk with provenance fields the orchestrator opens and re-derives |
| Model output replaced by a scripted provider | At least one D5/D6 path resolves the real `IChatModel` from `deepseek` config; the trace must record a non-zero output-token count and a real model id |
| Requirements doc edited to drop a test | D3 parses the *committed* doc; `git diff` on the doc is reviewed per round |

## Capability probe

| ID | Capability | Check | Result |
|---|---|---|---|
| CAP-001 | .NET SDK for `net10.0` | `dotnet --list-sdks` | present — `10.0.103` |
| CAP-002 | Real model provider credentials | `.secrets/deepseek.env` key names only | present — `DEEPSEEK_API_KEY` (non-empty, `sk-` prefix, value never printed), `DEEPSEEK_BASE_URL` (non-empty) |
| CAP-003 | RustLace queueing engine | `F:\dev\RustLace\target\debug\lovelace-run.exe --functions` | present — 10 `Systems` builtins, `protocolVersion` 2 |
| CAP-004 | Git worktree support | baseline repo state | present — branch `WIP` at `0b497ae` |
| CAP-005 | xUnit harness | existing test projects | present — 48-project solution, full suite runs |

No capability required by D5–D7 is missing.

## Stop criteria

1. **Converged** when D1–D7 all pass on a fresh run by the orchestrator, with G1–G5 green and zero Falsified rows.
2. **Blocked** only after the *same* concrete condition blocks three consecutive rounds; the condition is named in `blocked_reason`.
3. **Round cap:** 30 rounds. Hitting the cap without convergence is reported as incomplete, never as done.

## Sequencing

Rounds follow the requirement document's own increments, because every later item depends on the
spine: **Alpha-0** (F-A, F-B, F-C, F-D, F-E, F-F → D1, D2, D5) → **Alpha-1** (F-G, F-H, F-I, F-L,
F-N → D8) → **Alpha-2** (F-J, F-K → D6, D7) → **Alpha-3** (F-M → D9) → **D3/D4 closeout**. Narrowing a
*round* is permitted; narrowing the goal is not.

## Named-mechanism anchor

The independent Intent Auditor (`audit-intent.md`) returned **41 KEPT / 2 REPLACED / 1 REMOVED**
against the first version of this contract. That audit was correct. The defects and their fixes:

| Defect found | Fix |
|---|---|
| **M-08 "invent or modify capabilities" was REMOVED** with no user exclusion | Asked the user; **D-05 approved** full §4.3 admission lifecycle. Requirements **F-M.1–F-M.8**, gate **D9**, tests 67–74 |
| M-30 Modus scheduling hooks were replaced by a factory-owned timer | **F-N.3** requires `IPluginScheduler` as the trigger source with factory-owned durable occurrence identity; test 77 |
| M-35 review "runs tests, probes, rule checks, requirement coverage, and architectural conformance" was reduced to build+test | **F-N.1** requires all three independent checks in the review artifact; test 75 |
| Security was absent as a gate | **F-N.2** restores it as a mandatory hard gate; test 76 |
| Duplicate merge/release/promotion suppression was unasserted | **F-N.4** idempotency/reconciliation; test 78 |
| Untrusted work execution was unbounded | **F-N.5**; test 79 |
| Blackboard's six owned views were not required | **F-N.6**; test 80 |
| "No supervisor agent" was unproven, only unbuilt | **F-N.7** forbids dispatch authority in any definition; test 81 |
| RustLace was not reachable from a profile | **F-N.8**; test 82 |

After A1 every named mechanism is `KEPT`:

| Mechanism | Status | Where it lands |
|---|---|---|
| Modus substrate / WIP runtime | KEPT | F-A.5, F-B, F-E.2–F-E.4 |
| `Wip.Factory` layer | KEPT | F-A.1–F-A.6, D1 |
| Durable event/work state | KEPT | F-C, D5 |
| No supervisor agent; deterministic dispatcher | KEPT (now gated) | F-N.7, test 81 |
| Blackboard across six owned views | KEPT (now gated) | F-H, F-N.6, test 80 |
| Immutable definitions + profiles | KEPT | F-D |
| Nine roles, all dispatchable | KEPT | F-G, **D8** |
| Improvement plane | KEPT | F-J, D6 |
| **"Invent or modify capabilities" / admission lifecycle** | **KEPT (restored)** | **F-M, D9** |
| Cross-session trace / experience / attribution | KEPT | F-I |
| RustLace advisory prediction, reachable by an agent | KEPT | F-K, F-N.8, D7 |
| Operator visibility + controls | KEPT | F-L |
| Executable review (validation + coverage + conformance) | KEPT (restored) | F-F, F-N.1, D5 |
| Security as a non-regressing hard gate | KEPT (restored) | F-N.2 |
| Modus scheduling hooks as trigger source | KEPT (restored) | F-N.3 |
| Transport-neutral store, local implementation | KEPT | F-C.1–F-C.2, K6 |
| Untrusted execution bounded | KEPT | F-N.5 |

Project set is now **12 implementation projects + 3 test assemblies** (the auditor correctly noted
that D3's original `Wip.Factory.Gates.Tests` was an unauthorised twelfth project; the compliance
gate now lives in `Wip.Factory.Host.Tests` and scans all three declared test assemblies).
