# Intent Audit — Wip.Factory Functional Alpha

> Produced by an independent Intent Auditor subagent (70f91b7f), persisted verbatim by the orchestrator.
> Counts: **41 KEPT / 2 REPLACED / 1 REMOVED**. Most serious gap: M-08 "invent or modify capabilities" absent.
> `goal.md` = `docs/goal-wip-factory-alpha/goal.md`; `req` = `harness/requirements/Wip.Factory.Alpha.md`.

Text-only audit; no build or test executed. ❓ marks inference.

### 1. Mechanism inventory

Inventory scope: source §Executive summary (ll. 15–27), §Architectural commitments (ll. 29–38), D-01…D-04 (req ll. 48–51).

| ID | Mechanism (quoted or closely paraphrased) | Source |
|---|---|---|
| M-01 | "Modus should remain the generic, versioned plugin and hosting substrate"; reuse "plugin identity, lifecycle, registration, dependency injection, discovery, and scheduling hooks" | §Central thesis:11; §Exec:21 |
| M-02 | "WIP should remain the governed agent runtime"; typed abstractions, model–tool execution, session state, memory, streaming, worktrees, artifacts, validation, review, approval, merge governance | §11; §Exec:22 |
| M-03 | "A new `Wip.Factory` application layer should turn agent execution into event-driven software production" | §11; §Exec:23 |
| M-04 | "Agent packs (new): nine logical production/analysis roles configured as reusable agent definitions and bound to WIP capabilities registered through Modus-aware composition" | §Exec:24 |
| M-05 | "Improvement plane (new): agent-level Observe → Diagnose → Hypothesize → Experiment → Evaluate → Promote/Reject" | §Exec:25 |
| M-06 | "each agent to observe the results of its own historical sessions" | §Exec:17 |
| M-07 | "correlate those results with downstream outcomes" | §Exec:17 |
| M-08 | "**invent or modify capabilities**" | §Exec:17 |
| M-09 | "evaluate candidate methods" | §Exec:17 |
| M-10 | "promote improvements" | §Exec:17 |
| M-11 | "create, review, release, maintain, and improve software" | §Exec:17 |
| M-12 | "observing and improving the process that produces it" | §Exec:17 |
| M-13 | "does not introduce a permanent supervisor agent" | §Exec:19 |
| M-14 | "a heavyweight centralized LLM orchestrator" (not introduced) | §Exec:19 |
| M-15 | "a dedicated integration-controller organization" (not introduced) | §Exec:19 |
| M-16 | "mandatory fragmentation of requirements into tiny tasks" (not introduced) | §Exec:19 |
| M-17 | "durable work-item events" | §Exec:23 |
| M-18 | "typed domain artifacts" | §Exec:23 |
| M-19 | "dependency-aware readiness" | §Exec:23 |
| M-20 | "blackboard indexing" | §Exec:23 |
| M-21 | "immutable agent-profile versions" | §Exec:23 |
| M-22 | "cross-session telemetry" | §Exec:23 |
| M-23 | "evidence provenance" | §Exec:23 |
| M-24 | "stage-specific event handlers" | §Exec:23 |
| M-25 | improvement plane "with factory-wide metrics … as evidence sources" | §Exec:25 |
| M-26 | improvement plane with "RustLace modeling as evidence sources" | §Exec:25 |
| M-27 | First production milestone: requirement event → WIP sessions "through the Modus composition root" → governed PR → "independently executable evidence" → "advances through delivery in an event-driven fashion" | §Exec:27 |
| M-28 | "Every action is attributable to a versioned agent profile" | §Exec:27 |
| M-29 | First self-improvement milestone: bounded experiment rejectable/rollbackable "without changing permissions or mandatory acceptance gates" | §Exec:27 |
| M-30 | Modus "scheduling hooks" reused for periodic strategic/reflection triggers | §Exec:21 |
| M-31 | "Events trigger work; durable state authorizes work. An event is a notification of a committed fact, not the source of truth for mutable state." | §Arch:31 |
| M-32 | "The blackboard is a logical collaboration interface, not a monolithic database. Agents see a unified workspace of references, artifacts, decisions, and observations." | §Arch:32 |
| M-33 | "Dependencies express real ordering constraints. A coherent work item is not split for artificial parallelism" | §Arch:33 |
| M-34 | "incompatible changes are still caught by fresh-base CI before acceptance" | §Arch:33 |
| M-35 | "Verification is substantive and executable. Review runs tests, probes, rule checks, requirement coverage, and architectural conformance checks" | §Arch:34 |
| M-36 | "approval binds to exact artifact/code/policy versions" | §Arch:34 |
| M-37 | "Roles are agent definitions, not nine always-running processes… using a pinned profile and bounded WIP session" | §Arch:35 |
| M-38 | "The Session Store is operational memory. Cross-session learning and downstream attribution extend WIP's current per-session memory, rather than conflating the two." | §Arch:36 |
| M-39 | "Agents can improve their methodologies, not their own authority. Evaluation and authorization remain independent of the version being evaluated." | §Arch:37 |
| M-40 | "Reuse beats reimplementation. Factory code does not implement a second model loop, tool gateway, worktree engine, or merge policy." | §Arch:38 |
| M-41 | D-01: "full minimal improvement loop: reflection → candidate profile → hard-gate evaluation → promote / reject / rollback" | req:48 |
| M-42 | D-02: "transport-neutral interface with a local durable implementation"; PostgreSQL outbox/inbox "a later adapter, not an alpha dependency" | req:49 |
| M-43 | D-03: "All nine roles are dispatchable in the alpha, not authored-only" | req:50 |
| M-44 | D-04: "RustLace is in the alpha with one real prediction compared against observed traces" | req:51 |

### 2. Verdict table

| ID | Verdict | goal.md | Requirement | Enforced by |
|---|---|---|---|---|
| M-01 | KEPT | goal.md:41, :93 | F-B.1–F-B.6 (req:262–267), F-E.4 (req:296) | host-owned `WipBuilder`, `PluginOrigin`+`CapabilityVersion`, one container |
| M-02 | KEPT | goal.md:41, :93 | F-E.2 (req:294), F-E.3 (req:295), F-F.1 (req:305), F-F.5 (req:309) | distinct execution/gov paths listed |
| M-03 | KEPT | goal.md:25 (D1), :94 | F-A.1–F-A.6 (req:253–258) | eleven projects, six layering rules |
| M-04 | KEPT | goal.md:98 | F-D.1–F-D.2 (req:283–284), F-B.1–F-B.2 (req:262–263), F-G.1–F-G.9 (req:315–323) | definitions+profiles authored, all bound in the one host builder |
| M-05 | KEPT | goal.md:30 (D6), :99 | F-J.1–F-J.10 (req:346–355) | full six-step loop |
| M-06 | KEPT | goal.md:100 | F-I.1 (req:337), F-J.2 (req:347) | trace store + session-batch selection |
| M-07 | KEPT | goal.md:100 | F-I.3 (req:339), F-I.6 (req:342) | attribution chain with uncertainty |
| M-08 | **REMOVED** | goal.md:88–89 asserts KEPT but no artifact lands it; D6 (goal.md:30) carries only "a candidate profile version" | nearest mechanisms: F-D.3 (req:285), F-J.4 (req:349) = profile/instruction revision only. No F-* item for a new tool/validator/script candidate, isolated build/test, or plugin admission. Not excluded at req:62–70 or req:693–703 | requirement text confirms: "admission" appears only as *catalog lookup* (F-D.4 req:286, F-D.7 req:289, F-E.8 req:300) |
| M-09 | KEPT | goal.md:30, :99 | F-J.5 (req:350), F-J.6 (req:351), F-J.8 (req:353) | experiment record + independent evaluator |
| M-10 | KEPT | goal.md:30 | F-J.7 (req:352) | pointer swap + retained history |
| M-11 | KEPT | goal.md:29–30 | F-G.1–F-G.9 (req:315–323), F-J.1–F-J.10 | create/review/release/ops roles + improvement |
| M-12 | KEPT | goal.md:30, :101 | F-L.4 (req:372), F-K.3 (req:361), F-J.1 (req:346) | process metrics + stage-network derivation |
| M-13 | KEPT | goal.md:95 | F-C.6 (req:276), F-E.2 (req:294) | deterministic worker + no model loop in factory code |
| M-14 | KEPT | goal.md:41 (K2), :95 | F-A.3 (req:255), F-E.2 (req:294) | routing in pure Core; no LLM in factory |
| M-15 | KEPT (by omission) | goal.md:15–19 | no F-* adds one; F-F.5 (req:309) routes merge through WIP | nothing creates an integration controller |
| M-16 | KEPT (by omission) | goal.md:15–19 | F-A.3 (req:255) single aggregate; F-C.8 (req:278) | no fragmenter required |
| M-17 | KEPT | goal.md:95 | F-C.1 (req:271), F-C.2 (req:272) | durable event store |
| M-18 | KEPT | goal.md:94 | F-A.1 (req:253), F-H.2 (req:331), F-I.4 (req:340) | typed ids + typed artifacts |
| M-19 | KEPT | goal.md:94–95 | F-C.8–F-C.9 (req:278–279), F-A.3 (req:255) | acyclic deps, wake-on-complete |
| M-20 | KEPT | goal.md:96 | F-H.1 (req:330) | — (index only implied ❓) |
| M-21 | KEPT | goal.md:97 | F-D.2–F-D.3 (req:284–285) | digest + promotion pointer |
| M-22 | KEPT | goal.md:100 | F-I.1 (req:337), F-I.5 (req:341) | sanitized cross-session traces |
| M-23 | KEPT | goal.md:29 (D5 "populated provenance chain") | F-I.4 (req:340), F-H.2 (req:331) | per-artifact producer identity |
| M-24 | KEPT | goal.md:95 | F-C.6 (req:276), F-A.1 `SubscriberId` (req:253), F-C.3 (req:273) | per-subscriber inbox; single worker permitted |
| M-25 | KEPT (partial) | goal.md:100, :102 | F-L.4 (req:372), F-K.3 (req:361) | metrics exist; not required as a reflection/evaluation input ❓ |
| M-26 | KEPT | goal.md:31 (D7), :101 | F-K.1–F-K.7 (req:359–365) | prediction + signed comparison |
| M-27 | KEPT | goal.md:29 (D5) | F-G.1–F-G.7 (req:315–321), F-F.2–F-F.5 (req:306–309) | — (D5 stops at merge; see 3.1) |
| M-28 | KEPT | goal.md:29 | F-I.4 (req:340), F-L.4 (req:372) | profile version on artifacts and traces |
| M-29 | KEPT | goal.md:30 (D6) | F-J.6 (req:351), F-J.7 (req:352), F-J.9 (req:354), F-D.6 (req:288) | gates read-only; escalation denied |
| M-30 | **REPLACED BY** factory-owned `BackgroundService` + `ScheduleOccurrence` | goal.md:95 (names F-C, D5; never a scheduler) | F-C.6 (req:276), F-G.8 (req:322), F-L.2 (req:370) | no checklist item or test references `IPluginScheduler`; only the baseline claim at req:80 |
| M-31 | KEPT | goal.md:95 | F-C.2 (req:272), F-C.5 (req:275) | atomic state+event; CAS |
| M-32 | KEPT (narrowed) | goal.md:96 | F-H.1–F-H.4 (req:330–333) | no requirement names the six owned views (see 3.6) |
| M-33 | KEPT | goal.md:95 | F-C.8 (req:278), F-A.3 (req:255) | dependency semantics |
| M-34 | KEPT | goal.md:15–19 | F-F.4 (req:308), F-F.7 (req:311) | base-commit binding + invalidation |
| M-35 | **REPLACED BY** `dotnet build`/`dotnet test` validation plus recorded tool versions | goal.md:103 claims KEPT | F-F.2 (req:306), F-F.3 (req:307); coverage/conformance survive only as gate names in F-J.8 (req:353) | no review-time probe, coverage or conformance check required |
| M-36 | KEPT | goal.md:103 | F-F.4 (req:308) | four bound fields invalidate approval |
| M-37 | KEPT | goal.md:98 | F-E.1 (req:293), F-E.5 (req:297), F-G.1–F-G.9 | per-trigger instantiation, pinned profile |
| M-38 | KEPT | goal.md:100 | F-I.1 (req:337), F-I.2 (req:338) | separate trace + experience stores |
| M-39 | KEPT | goal.md:99, :103 | F-D.5–F-D.6 (req:287–288), F-J.6 (req:351), F-J.9 (req:354) | evaluator independence + ceiling |
| M-40 | KEPT | goal.md:41 (K2) | F-E.2 (req:294), F-E.4 (req:296), F-F.1 (req:305), F-F.5 (req:309) | — (absence proven by presence test; see 3.4) |
| M-41 | KEPT | goal.md:30 ("promotion **or** rollback") | F-J.7 (req:352), F-J.8 (req:353), F-J.9 (req:354) | both promote and rollback tested (test 51, req:590) |
| M-42 | KEPT | goal.md:45 (K6), :104 | F-C.1–F-C.2 (req:271–272) | interfaces identical; storage local |
| M-43 | KEPT | goal.md:40 (K1), :98 | F-G.1–F-G.9 (req:315–323); test 37 (req:544) | — (goal gates only the six-role spine; see 3.1) |
| M-44 | KEPT | goal.md:31 (D7) | F-K.1–F-K.5 (req:359–363) | real child process + signed comparison |

### 3. Contradictions and gaps

**3.1 "All nine roles dispatchable" — dispatch survives in the requirement set; the goal contract does not gate it.**
- Requirement text is genuine dispatch, not authoring: F-G.1–F-G.9 each read "is dispatchable from <role-specific trigger> and publishes <artifact>" (req:315–323); F-G.6/F-G.7 demand real probes and an *actually exercised* rollback path (req:321); test 37 dispatches each role from its own trigger (req:544).
- The goal weakens the evidence. D5 (goal.md:29) — the only spine gate — requires artifacts for "requirement, architecture, implementation, review, approval and merge": six roles. No D-check's observable mentions Deliver Analyst, Release Manager, Strategic Architecture Analyst or Operations Analyst, yet goal.md:98 cites "F-G, D5" for all nine. D5 cannot produce that evidence.
- Alpha-1's exit condition ("All nine roles dispatch; … delivery/release/release-rollback and scheduled/operations triggers are observable", req:133) appears in no D-check. ❓ An implementation dispatching six roles and leaving four as resolvable profiles passes D1–D7 provided test 37 exists.
- Second hole: source §8's per-role "Required capability composition" column (ll. 488–496) is dropped. F-D.1 requires only "objective, responsibilities, permission ceiling and allowed evaluation-policy ids" (req:283); F-G.11 carries only §8's *evidence* column (req:325). ❓ A role profile may reference zero role-specific tools and still pass every F-G/F-D test — "dispatchable" then means "a session starts", not "the role can do its job".

**3.2 Improvement loop — end-to-end, not descriptive; the goal gate is disjunctive.**
- End-to-end in the requirement set: bounded reflection (F-J.2) → immutable hypothesis (F-J.3) → candidate version with pointer untouched (F-J.4) → experiment record (F-J.5) → independent evaluator with read-only gates/holdouts (F-J.6) → atomic promote *and* rollback with retained history (F-J.7, test 51) → gate-regressing candidate rejected (F-J.8, test 49) → escalation denied (F-J.9, test 52). This is a real loop, not a description.
- D6 (goal.md:30) requires "a promotion **or** rollback". A build that only rolls back satisfies D6, while D-01 (req:48) names promote/reject/rollback and calls itself the "full minimal improvement loop". The requirement set still requires both, so the loss is at the goal's gate — which is the contract the harness converges against.
- Minor: D6 omits F-J.10's "missing-downstream-data flags" (req:355) even though it cites sample size and CI.

**3.3 At-least-once semantics — survive; two soft spots.**
- Survive intact: transport-neutral interfaces (F-C.1), atomic state+event append (F-C.2), inbox keyed `(SubscriberId, FactoryEventId)` acknowledged only after results persist (F-C.3), monotonic fencing token with stale-write rejection and no event (F-C.4), optimistic CAS (F-C.5), restart recovery with exactly one request per missed occurrence (F-C.6), poison quarantine + individual replay (F-C.7). Tests 12–18 (req:461–478) are negative-path and assert *absence of side effects*, which is stronger than the source prose.
- D-02 does not soften them: it authorises local durable storage behind the same interfaces, not weaker semantics; F-C.2's single atomic append and test 12's torn-read probe preserve the invariant. K6 (goal.md:45) is consistent.
- Soft spot 1 — tool side effects: source §5.3 requires "idempotency keys **or** explicit reconciliation" (l. 316) and forbids duplicate "PR merges, releases or capability promotions". "idempotency" appears in the requirement set only inside the M-20 summary row (req:40), never in a checklist item. Reconciliation exists only as test 16 (req:473) and one scenario row (req:653, "covered by" 12/16 — neither test targets orphan reconciliation). Duplicate release and duplicate promotion are nowhere asserted suppressed; only duplicate transitions (test 13) and merge invalidation (F-F.4/F-F.7) are.
- Soft spot 2 — no exactly-once claim is made, matching source §10.1. ✔

**3.4 "No permanent supervisor agent" and "deterministic dispatcher" — honoured by construction, unproven.**
- Structure exists: state machine, readiness and routing sit in `Wip.Factory.Core` with no infrastructure reference (F-A.3); dispatch is a `BackgroundService` over the outbox (F-C.6); factory code may hold no model loop (F-E.2) — matching §5.5's "dispatcher is deterministic infrastructure, not an LLM supervisor" (l. 361).
- But the mechanism is asserted, not required. "supervisor" occurs only in the requirement doc's own M-13 row (req:33), whose "Landed in" cells (F-C.6, F-E.1) contain no such text. goal.md:95 repeats it citing F-C and D5; D5's observable (goal.md:29) says nothing about supervisors.
- ❓ A tenth "supervisor" AgentDefinition/Profile that picks the next role would violate no checklist item and fail no named test: F-D.1/F-D.2 accept any definition, and F-E.2 forbids a loop *inside factory code*, not a supervisor agent inside a WIP session. M-13 survives by absence of implementation, not by a gate.
- Same shape for A8/M-40: the "no second model loop, tool gateway, worktree engine or merge policy" absence is "proven" by a presence test — "Facade resolved by `IAgent<>` and executed through `RunAgentAsync`…" (req:394).

**3.5 Metric proxies and deterministic stand-ins — three conversions.**
- (a) **D3 is a name-resolution check.** goal.md:27 requires a test that parses the doc, extracts 66 backticked names and asserts each resolves by reflection to `[Fact]`/`[Theory]`, "0 missing, 0 skipped". That proves the document and the assembly agree on 66 identifiers; it cannot distinguish a behavior test from `Assert.NotNull`. The defence — "Each gate test must fail if its target body is replaced with `throw new NotImplementedException()`; spot-checked per round" (goal.md:56) — is an unrecorded sample, not a gate. ❓ Against the doc's own "Metadata-only assertions are supporting evidence only" (req:111) and F-L.6's "absolute behavior-proof" (req:374), this is the largest epistemic weakening in the contract.
- (b) **Requirement coverage becomes document self-reference.** Test 63 asserts "every checklist item maps to at least one named test" (req:632) over a matrix (req:376–416) authored by the same document. Combined with (a), goal.md:15's "all 89 requirements are implemented" is verified by 66 resolvable names plus three demo commands. The doc's sign-off claims this is "not only a proxy" (req:732); that claim is itself unverified. ❓
- (c) **Review verification is halved (M-35).** "requirement coverage" and "architecture" as per-change checks appear only in F-J.8's candidate-evaluation gate list (req:353). F-F.2 runs build/test; F-F.3 only *records* "tool and validator versions with test/probe results" (req:307). No review-time probe, coverage validator or conformance check is required — a review can be accepted having executed compilation and tests only.
- (d) Modus scheduling hooks (M-30) are substituted, not proxied: no requirement, test or goal line names `IPluginScheduler`; only the baseline row says "Reuse as-is" (req:80).

**3.6 Other losses found while checking the inventory.**
- **Security:** the word "security" has zero occurrences in `Wip.Factory.Alpha.md`. Source §7.4 makes security a non-regressing hard gate (l. 466); §8 gives Review Analyst "security checks" (l. 491); §13 defines five trust boundaries (ll. 607–619). F-J.8's hard-gate list drops it (req:353). Trust-boundary halves survive (C-04, F-K.1, F-H.3, F-D.6, F-I.5), but there is no security gate and no security review check. Outside the mandated inventory; still material.
- **Untrusted work execution:** source §13.3 requires shell commands and generated code to run "in a restricted subprocess/container/VM with bounded CPU, memory, wall time, filesystem and egress" (l. 615). No requirement bounds implementation-session shell/generated-code execution; C-01 bounds the delivery target, F-K.1 bounds `lovelace-run`. M-15's claimed landing (F-G.10, F-F.5, F-K.1) does not cover it. ❓
- **Blackboard breadth (M-32):** F-H.1–F-H.4 require a version-pinned, provenance-bearing, role-scoped, budgeted resolve/publish; no requirement names the six logical views with their owners (§6, ll. 391–398) or requires resolve to span work, Git/delivery, telemetry and capability stores. ❓ A resolver over artifact records alone satisfies F-H; "unified workspace" is lost by omission, not by decision.
- **Metrics as evidence source (M-25):** F-L.4 defines metrics, F-L.1 exposes improvement lineage, but nothing feeds factory-wide metrics into reflection (F-J.1 uses "schedule and anomaly signals", req:346) or evaluation (F-J.10). Only F-K.3 consumes per-stage demand/WIP/arrival (req:361). ❓ Half-landed.
- **RustLace reachability:** D7 and F-K.1–F-K.5 land the adapter and comparison, but no requirement makes the capability reachable from an agent profile; source §11 says "a capability available to analytical agents" (l. 577) and `factory-analysis-no-orchestrator` calls it "a capability" (req:247) without requiring registration in the WIP catalog. ❓
- **Contract inconsistency:** D3's command targets `Wip.Factory.Gates.Tests` (goal.md:27) — an assembly with zero occurrences in the requirement doc, outside the fixed eleven projects (req:120, :258) and outside F-A.5's derived project-set assertion. ❓ The goal's central compliance gate depends on an unauthorised twelfth project.
- **goal.md:88–89 is false as written:** "Every mechanism the user or source named is `KEPT`. No mechanism is replaced or removed." M-08 is removed, M-30 and M-35 are replaced, and D-01…D-04 do not address any of them. The sentence should be deleted or qualified.

### 4. The single question

**Q1 — Is executable-capability evolution part of the alpha's improvement plane, or is "data-only profile evolution" the intended alpha scope?**

The source gives each agent the ability to "**invent or modify capabilities**" (§Exec:17), operationalised as a new tool/script/validator candidate built and tested in isolation and then admitted through the Modus/WIP path (§4.2 path 2, ll. 256–259; §7.3 bullet 3, l. 456; §14 F-M4.4, l. 669). The requirement set lands only profile/instruction revision (F-D.3, F-J.4) and records no exclusion (req:62–70, req:693–703); the goal asserts nothing was removed (goal.md:88–89).

**What the goal currently assumes:** that data-only evolution is the approved scope, because D-01's decision text says "candidate profile" and D6's payload is "a candidate profile version" (goal.md:30). That inference is not recorded, and D-01's alternatives ("reflection-and-hypothesis-only; defer entirely") show the decision was about loop *depth*, not about which evolution path is in scope.

**If the answer is "data-only for alpha":** record it as a user-signed decision (D-05), add it to Post-Alpha Exclusions, and delete the "no mechanism is replaced or removed" claim at goal.md:88.
**If the answer is "in scope":** add a requirement + named test for a candidate executable capability (isolated build/test, admission record, refusal of self-install) and add it to D6's observable.
