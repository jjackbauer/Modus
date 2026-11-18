# Intent-fidelity audit — Wip.Factory Operator Alpha v0.3

**Question.** Does the proposed contract `docs/goal-wip-factory-operator-alpha/goal.md` actually commit
to every mechanism `harness/requirements/Wip.Factory.Operator.Alpha.v0.3.md` declares mandatory?

**Method.** Line-level comparison of those two documents. WIP source was read only to confirm one
baseline fact (current state-machine adjacency). No file modified, no build/test run, no git command.
`harness/requirements/Wip.Factory.Operator.Alpha.md:3` marks v0.1 **SUPERSEDED**, so v0.1 is cited
nowhere as authority; v0.2 is cited once only to show a contradiction v0.3 already fixed.

**Verdicts.** **KEPT** = goal.md commits to it. **REPLACED BY x** = goal.md substitutes something else.
**REMOVED** = goal.md contains no commitment. Fidelity: **full** (same binding strength as the spec),
**partial** (one or more stated proof conditions missing), **nominal** (named only, not falsifiable).

## 1. Mechanism-by-mechanism verdicts

| # | Mechanism (mandatory in v0.3) | Spec | goal.md | Verdict | Fidelity |
|---|---|---|---|---|---|
| 1 | PA-01 unfamiliar requirement → verified release | 58 | 24 | KEPT | partial — drops "`serve` runs and proves the intended release" |
| 2 | PA-02 second differing requirement, same binary | 59 | 25 | KEPT | full |
| 3 | PA-03 diff = merged revision, attributable | 60 | 26 | KEPT | partial — lineage `work-item→session→profile→attempt→base/head/diff` (60, 386) absent |
| 4 | PA-04 known-wrong candidate rejected by oracle | 61 | 27 | KEPT | partial — "refuses progress" becomes "no release" |
| 5 | PA-05 kill/resume, no duplicate effects | 62 | 28 | KEPT | partial — "false release" and "lost failure record" dropped |
| 6 | PA-06 every approval-bound fact mutated → refuse | 63, 324 | 29 | KEPT | full |
| 7 | PA-07 served release identity + contract | 64 | 30 | KEPT | partial — process tree + release artifact replaced by "PID/port/SHA" |
| 8 | PA-08 unpinned/path/unsupported refusal | 65 | 31 | KEPT | partial — reparse points, bad `dotnet` subcommands, contract edits dropped |
| 9 | Six-tool interface | 355–363 | 52 | KEPT | nominal — only "KEPT (§6.1 six tools)"; no tool names, no gate |
| 10 | Frozen acceptance contract (digest-pinned, agent-unwritable) | 36, 176, 270–272 | 16, 27, 54 | KEPT | partial — "immutable/independent" kept; digest pinning, control-root storage, write prohibition absent |
| 11 | Merge approval separate from contract approval | 37, 196, 214 | 16, 55 | KEPT | full |
| 12 | `factory serve` supervision distinct from `run` | 42, 218, 393–394 | 18, 56, 30 | KEPT | partial — "blocking/owns process" gone; pinned release artifact not gated |
| 13 | Effectively-once outcomes where reconcilable | 297–305 | 57 | KEPT | nominal — slogan only; no operation IDs, reservation, `Recorded` outcome, locks |
| 14 | Fail-closed execution isolation | 373 | 58, 31, 41 | KEPT | partial — mechanism named; `--trusted-environment` semantics (374) absent |
| 15 | Durable control root outside repo + worktrees (D-06) | 90, 331, 424–437, 496 | none — absent from 36–43 and 45–58; 0 hits for "control"/"outside" in the file | **REMOVED** | — |
| 16 | Three model-driven roles by default, nine available (D-04) | 50, 88 | 34 (only "nine roles" as a non-substitute) | **REMOVED** | — |
| 17 | D-08 A: one new edge `DeliveryValidation → NeedsRework`, pre-merge only | 92, 113, 623 | 8–9, 42 | KEPT | partial — "pre-merge only" survives only inside the quoted request (9); post-merge prohibition (217, 230) absent |
| 18 | D-09 template-policy contract approval | 93, 280–289, 538–543 | 9–10 (quoted request only) | KEPT | nominal — no gate, no anchor row, no negative tests |

Totals: **KEPT 16** (3 full, 10 partial, 3 nominal), **REPLACED 0**, **REMOVED 2**.

## 2. Silently dropped, softened, or made unfalsifiable

1. **Control root (D-06) is gone.** goal.md's hard constraints (36–43) and anchors (49–58) never mention
   it, yet the spec makes it an absolute invariant with a fail-closed test
   (`Host_GivenControlRootInsideTarget_RefusesToStart`, 496) and a symlink-proof rule (437). A delivery
   that writes factory state inside the operator's repository passes every goal criterion.
2. **Three-role default (D-04) is gone.** goal.md:34 mentions "nine roles" only to deny it as proof; the
   spec's "three model-driven roles by default" (50, 88) has no counterpart. A nine-role path, or a
   single-role path, is untested by goal.md.
3. **D-09 is quoted, never operationalized.** It appears only in the verbatim request (9–10). The spec
   requires provenance recording and eight negative tests (280–289, 538–543), and explicitly permits the
   evaluator's human approval to carry PA-01/02 (72) — so template policy can be absent while all eight
   gates pass.
4. **PA-01 softened.** goal.md:24 stops at "verified release" + "manifest emitted"; spec:58 additionally
   requires "`serve` runs and proves the intended release". Serve is now tested only by PA-07.
5. **PA-03 loses attribution.** Spec:60 and 386(e) require the durable `workItemId→sessionId→
   profileVersion→stageAttemptId→baseSha→headSha→diffDigest` chain; goal.md:26 keeps only "nonempty
   diff" and "merged tree contains it". "Attributable" is asserted, not defined.
6. **PA-05 loses two honesty properties.** Spec:62 requires "no … false release, or lost failure
   record"; goal.md:28 keeps only "no duplicate merge; no corrupt state". "False release" is the exact
   property spec:345 makes mandatory.
7. **PA-07 loses the artifact.** Spec:64 names "process tree, release artifact and allocated loopback
   port"; goal.md:30 observes "PID/port/SHA". Spec:393's "serve launches exactly that release, not a
   mutable working directory" is therefore ungated.
8. **PA-08 loses three refusal classes.** Spec:65 includes reparse-point escapes, unsupported `dotnet`
   subcommands, unauthorized contract edits; goal.md:31 lists only unpinned tools, path escapes,
   unsupported isolation. Tests 32/57/58 (514, 542–543) have no goal surface.
9. **Six tools are nominal.** The only mention is goal.md:52. The names, the `dotnet` subcommand
   allowlist, atomic patch and worktree confinement (355–363) appear nowhere; PA-08 tests the ceiling,
   not the required set. Three tools would satisfy the goal.
10. **"Effectively-once" is a slogan.** goal.md:57 anchors the phrase but drops operation identity
    (309–313: `stageAttemptId`/`effectOrdinal`, three writes → three IDs), reservation semantics (302),
    the `Recorded`/`ProgressWithoutTransition` dispatch outcome (180), the reconciliation table (337–345)
    and cross-process locks (326–333). goal.md:41 says only "no claim of … multi-process scheduling".
11. **The evidence manifest is narrowed.** Spec:77–79 requires per-gate pass/fail/inconclusive plus host
    SHA, model/provider, digests, operation IDs, timings. goal.md mentions a manifest once, inside PA-01's
    observable ("manifest emitted", 24) — no fields, no per-gate binding.
12. **The CLI contract is absent.** Spec:406–415 documents the command surface and 420 the exit
    categories; goal.md names only `factory serve` (18, 56). `requirement submit`, `contract
    approve/reject`, `approve`, `status`, `show`, `template *` are uncommitted, so spec:54's "documented
    CLI" has no documented CLI to point at.
13. **Root defect: the spec body is incorporated by reference but never made a stop criterion.**
    goal.md:5–7 calls v0.3 "the normative body", yet the definition of done (20–31) and stop criteria
    (62–64) reference only the eight PA gates. Every omission above is individually non-falsifying, and
    the anchors table's heading "all KEPT — no replacement, no exclusion" (45) is a completeness claim
    over a ten-item subset.

## 3. Where goal.md promises what the spec forbids

**One clause.** goal.md:62 makes "a gate is honestly recorded as failing" a legitimate stop condition;
spec:75 states "A failed PA gate makes the overall delivery **not accepted**" and spec:645 repeats the
absolute definition. As written, the goal can be *stopped* (i.e. satisfied) with a failed mandatory gate.

No other violation found: goal.md's N-02 (38) keeps the single tool gateway/merge gate, N-09 (41) keeps
the no-sandbox / no-exactly-once / no-multi-process claims, and PA-03/PA-07 stay inside spec:60/64. I
checked the three categories the brief named and they are clean.

## 4. The single most important question

**Is goal.md's eight-gate table the complete acceptance contract, or is the v0.3 normative body binding
clause by clause?** If the former, a delivery may omit D-06 control-root separation, D-04's three-role
default, D-09 template policy, the six-tool set, operation identity and cross-process locks and still
satisfy every stop criterion. If the latter, those clauses must be promoted into gates or hard
constraints before implementation starts. (The failed-gate conflict in §3 is a separate, smaller
question the user should also settle.)

## 5. Internal contradiction affecting PA provability

**No contradiction makes a gate impossible to execute, but PA-01 is unverifiable as written.** Spec:58
requires merge + post-merge verification *and* "`serve` runs and proves the intended release", while
spec:43 defines `Released` as explicitly "**not** evidence that a persistent app process is running" and
spec:44/116 reserve liveness for `OperationallyValidated`. PA-01's named proof ends at
"…ToVerifiedRelease" (58). One gate therefore spans two distinct terminal states (115 vs 116) with no
single pass predicate: if serve fails after a successful merge, PA-01 is neither pass nor fail.
goal.md:24 resolves this silently in the **weak** direction by dropping serve.

Secondary, lower severity: spec:54 requires all gates to run "through the documented CLI", but spec:406–415
documents no verb that can mutate an approval-bound fact (PA-06) or induce an out-of-profile tool call /
missing resource control (PA-08); spec:479 relaxes the bar to "CLI and runtime". PA-06/PA-08 are provable
only through an injection seam the spec never defines.

Cleared false positives: the state table (103–120) matches the routing table (188–203) for every trigger;
the sixteen-state count is exact; "four-check review" (195) matches §5.5 (349); and PA-06's "every bound
category" (63, 324) correctly supersedes v0.2's fixed seven fields (v0.2:47).

## 6. What I could not check (❓)

- ❓ **Whether other goal-folder files carry the dropped mechanisms.** `state.md`, `deliverables.md`,
  `journal.md`, `evidence.md` exist beside goal.md but were outside the assigned scope, so I did not open
  them. If `deliverables.md` enumerates D-06 / D-04, both REMOVED verdicts should become KEPT (nominal) —
  this is the one finding that could change the totals, and it should be checked before acting.
- ❓ **Whether the WIP baseline already satisfies any row.** I verified only
  `WIP/Wip.Factory.Core/WorkStates.cs:56–57`, where `DeliveryValidation` currently targets
  `[ReleaseCandidate, Failed]` — confirming D-08 A is genuinely unimplemented work, not documentation.
  Broader baseline conformance would need builds/reviews, which are out of bounds.
