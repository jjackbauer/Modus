# Journal — Wip.Factory Operator Alpha v0.3

Append-only. Verified facts only; agent claims enter after the orchestrator re-checks them.

## OBS-001 — Baseline branch and spec state

- **Source**: session state; `harness/requirements/Wip.Factory.Operator.Alpha.v0.3.md`
- **Fact**: Implementation target is branch `feature/wip-factory-alpha` at `4a9793a`. The v0.3 spec is
  committed locally (not yet pushed). v0.1 is marked SUPERSEDED, v0.2 was never accepted.
- **Confidence**: High

## OBS-002 — Prior green counts are historical, not current

- **Source**: this session, earlier rounds (v0.3 N-01 explicitly discounts them)
- **Fact**: Earlier in this session the four factory suites reported 86 passing tests including the
  82/82 compliance gate, and the ten core WIP suites reported 460 passed / 3 failed. Under N-01 these
  must be **re-run and recorded fresh** before any delivery claim; they are not carried forward.
- **Confidence**: High

## DEC-001 — D-08 A and D-09 adopted as ratified

- **Source**: user request (v0.3 header and §2)
- **Decision**: Add exactly one adjacency edge `DeliveryValidation → NeedsRework`, legal **only before**
  the merge operation is applied. Policy approval permitted only for explicitly preapproved immutable
  templates with validated parameters; human fallback otherwise; template approval never authorises merge.
- **Owner**: user
- **Status**: approved

## OQ-001 — Who supplies the blind PA-01/02 requirements?

- **Question**: §1.1 requires an independent evaluator to select and disclose the two final requirements
  only after the implementation is frozen. In this session no external evaluator exists.
- **Impact**: PA-01/PA-02 cannot be honestly claimed without a withheld pair. Default plan: implementer
  authors two *development* fixtures for CI, and PA-01/02 remain **unclaimed** until the user supplies
  blind requirements after freeze.
- **Status**: open — needs user action at freeze time, not now.

## RISK-001 — Live model budget for blind runs

- **Risk**: PA-01/02 require the real configured model. Earlier this session a real DeepSeek round trip was
  measured (17 in / 3 out tokens), so the provider works, but a full app build is many steps and may
  exhaust budget or rate limits mid-run.
- **Mitigation**: bounded steps/timeouts; report `inconclusive` rather than substituting a fake model.
## OBS-003 — Independent intent audit FAILED the v1 goal contract

- **Source**: `audit-intent.md` (subagent `7aa8fb02`, read-only session; content persisted by me because
  the agent had no write tool), plus my own check of `deliverables.md`
- **Fact**: 18 mechanisms audited: **16 KEPT (3 full, 10 partial, 3 nominal), 0 REPLACED, 2 REMOVED**.
  REMOVED were **D-06 control root** and **D-04 three-role default** — neither appeared anywhere in the
  goal folder. Additionally the audit found ten partial/nominal rows (six tools named but not enumerated,
  "effectively-once" as a slogan with no operation identity, missing attribution chain in PA-03, PA-05
  missing "no false release / no lost failure record", PA-07 missing release artifact and process tree,
  PA-08 missing three refusal classes, absent CLI contract and manifest fields) and **one clause that
  contradicted the spec**: v1 stop criterion 1 allowed a goal to be *stopped* on an honestly-recorded
  failing PA gate, while v0.3:75 says a failed gate means the delivery is not accepted.
- **Implications**: **G0 FAILED**. Implementation could not start. This is the second time in this
  project that a completeness claim of mine ("all KEPT — no replacement, no exclusion") was false; the
  first was the 82/82 test count in the previous goal.
- **Confidence**: High
- **Related**: VAL-001, EVD-009, EVD-010, DEC-002..004

## VAL-001 — FALSIFIED: "all mechanisms KEPT" in goal.md v1

- **Claim**: v1 goal.md's anchors table asserted every user mechanism was KEPT.
- **Counterexample**: the table covered a 10-item subset; D-06 and D-04 were absent from the entire file
  (0 hits for "control"/"outside"/"three"/"role" in `goal.md`, and 0 hits in `deliverables.md` too —
  EVD-009).
- **Status**: Falsified. Superseded by goal.md v2's 18-row binding table.

## DEC-002 — PA-01 pass predicate ends at Released

- **Source**: audit §5; v0.3:58 vs v0.3:43/44/116
- **Decision**: PA-01 = merged, post-merge verified, reached `Released`. Liveness/`serve` proof belongs to
  PA-07. This resolves a contradiction the spec could not resolve itself: one gate cannot span two
  terminal states.
- **Owner**: agent (engineering resolution), consistent with v0.3's "proposed engineering resolutions"
  clause
- **Status**: approved

## DEC-003 — PA-06 and PA-08 are proven through harness injection seams

- **Source**: audit §5 secondary finding; v0.3:479 ("CLI and runtime")
- **Decision**: mutating an approval-bound field and requesting an out-of-profile tool are induced by
  test-only seams unreachable from the operator CLI.
- **Owner**: agent
- **Status**: approved

## DEC-004 — A failed PA gate is NOT acceptance

- **Source**: audit §3
- **Decision**: withdrawn the v1 clause that let "honest recording of a failing gate" stop the goal.
  Failure must be reported, and it never constitutes completion.
- **Owner**: agent, correcting my own contract
- **Status**: approved
## OBS-004 — M1 stop condition MET through the real CLI

- **Source**: operator run in a temp repo; commit `HEAD` after this round
- **Fact**: `factory requirement submit` → work item `WORK-bbb00f48` at `Proposed` v1 with requirement digest
  `sha256:08a4dc67…`. `factory run --until-idle` consumed the submission, the handler returned `Recorded`
  and the item became `Proposed` **v2** with a proposal digest — facts committed, state unchanged, exactly
  as v0.3 §3.2 requires. A **fresh process** read the proposal back. `factory contract approve --digest`
  froze it; `factory run` advanced `Proposed → RequirementsAligned` at **v4**; a second fresh process read
  `RequirementsAligned` from durable storage. Identical resubmission returned the same work item.
  Refusals verified: missing class + no expectations (`exit 2`), and control root inside the target
  (`control-root-inside-target`, `exit 2`).
- **Implications**: The intake → proposal → freeze → alignment spine is real and durable, and it runs
  through the production host rather than a demo. PA gates remain 0/8 because no implementation, contract
  execution, review, merge or serve exists yet.
- **Confidence**: High
- **Related**: EVD-011, EVD-012, EVD-013

## OBS-005 — Two defects found by running, not by reading

- **Source**: the first two CLI runs
- **Fact**: (1) the dispatch worker routes on `Data["workItemId"]`; my events omitted it, so every dispatch
  failed and the event was **quarantined** with "carries no 'workItemId'". (2) `ContractApproveAsync`
  committed the snapshot at its current version instead of `version + 1`, refused with "The snapshot
  version 2 must be exactly 3". Both were invisible to the build and to a code read; both surfaced on the
  first real run.
- **Implications**: Reinforces the harness rule that an unreproduced claim is not a delivery. The CLI path
  had never executed before this round.
- **Confidence**: High
- **Related**: OBS-004
## OBS-006 — The six-tool gateway exists and its refusals are tested

- **Source**: `Wip.Factory.Agents.Core/Tools/*`, `Wip.Factory.Wip/Isolation/RunCommandTool.cs`; Agents 67/67
- **Fact**: Six tools now exist: `list_files`, `read_file`, `search_text`, `apply_patch`, `write_file`,
  `run_command`. Nine PA-08 supporting tests pass: absolute path refused, traversal refused, patch conflict
  refused **atomically** (file verified byte-identical afterwards), matching patch applies, search respects
  its match cap and reports truncation, read_file truncates with total length, `nuget` subcommand refused,
  a `;`-bearing argument is passed literally (no marker file created, build fails instead), and a session
  without a worktree fails without writing.
- **Implications**: F-D's confinement and allowlist claims now have executable evidence. `apply_patch` is a
  real unified-diff applier with all-or-nothing semantics, not a whole-file rewrite.
- **Confidence**: High
- **Related**: EVD-015, EVD-016

## OBS-007 — M2 stop condition NOT reached this round

- **Source**: this round's scope
- **Fact**: The tools exist but nothing dispatches the Implementations role yet: there is no worktree
  provisioning, no profile-with-tools dispatch, no attributable-diff check and no `dotnet build`
  verification wired into the lifecycle. The work item still stops at `RequirementsAligned`.
- **Implications**: Honest statement of where the product is: intake and tooling are real; production does
  not yet generate source. M2 remains open.
- **Confidence**: High
- **Related**: OBS-004
## OBS-008 — M2 stop condition MET: the model built a working application

- **Source**: operator CLI run in `%TEMP%\m2b-43d7fb5f`; independent verification by me
- **Fact**: With the real model and six tools, the lifecycle advanced Proposed → RequirementsAligned →
  ArchitectureRefined → ReadyForImplementation → InImplementation → **InReview in 50 s**. The model created
  `BookCatalog.csproj` (Web SDK, net10.0) and a 62-line `Program.cs` implementing GET/POST/GET-by-id/DELETE
  with validation and a `BookStore`. I rebuilt it myself: `dotnet build` exit 0. I then started it on
  loopback and exercised the contract by hand: `GET /books` → 200 `[]`; `POST /books` → 201 with id; `GET
  /books/{id}` → 200 with title+author; `DELETE` → 204; `GET` after delete → 404. All four stated acceptance
  expectations hold against the running process.
- **Implications**: The thesis is demonstrated on a development fixture: requirements in, working software
  out, no per-application C#. PA-01/PA-02 remain **unclaimed** because this requirement was authored by me,
  not withheld (OQ-001).
- **Confidence**: High
- **Related**: EVD-018..021, OBS-009, OBS-010

## OBS-009 — Two more defects that only a real run exposed

- **Source**: the first two M2 runs
- **Fact**: (1) An empty target repository (git init, no commits) crashed the worktree provisioner:
  `git rev-parse --verify main` → `fatal: Needed a single revision`. Fixed by creating a baseline commit when
  HEAD is absent, and by converting provisioning exceptions into `worktree-failed:` instead of a raw throw.
  (2) The generated commit included `bin/` and `obj/` — 1,265 insertions, mostly binaries — because the fresh
  target repo had no `.gitignore`. Fixed by writing one at baseline so diffs stay source-only.
- **Implications**: Both were invisible to tests and to reading. Neither was in the spec's list of expected
  problems, which is why running first matters.
- **Confidence**: High
- **Related**: OBS-008

## OBS-010 — Scope honesty: this is not PA-01/PA-02

- **Source**: v0.3 §1.1; OQ-001
- **Fact**: The passing run used a requirement I wrote. v0.3 requires the two final requirements to be
  chosen by an independent evaluator **after** the implementation is frozen, and forbids accepting a
  hardcoded fixture as proof of general behaviour.
- **Implications**: M2 is complete; PA-01/PA-02 stay unclaimed. A second, behaviourally different
  development requirement should be run to reduce the risk of over-fitting before freeze.
- **Confidence**: High
- **Related**: OQ-001
## OBS-011 — The oracle exists, executes real HTTP, and can reject a wrong implementation

- **Source**: `Factory/AcceptanceContract.cs`, `AcceptanceContractRunner.cs`; Host 24/24
- **Fact**: A frozen contract is parsed, canonicalised and digest-pinned; the runner executes its scenarios in
  order over real loopback HTTP, substitutes captured values into later paths, compares JSON as a structural
  subset with ordered arrays, and records a per-scenario verdict with response digests. Six tests drive
  scripted TCP servers: a correct implementation passes all three scenarios; a wrong status fails AC-1 with
  `status-mismatch`; a wrong stored value fails AC-2 with `json-subset-mismatch`; capture substitution is
  proven by asserting the exact request sequence seen by the server; absolute URLs, missing `expect.status`
  and an unsupported class are refused at parse. A requirement may now reference `acceptanceContract:` and the
  proposal reports `executable: true` with the contract digest.
- **Implications**: PA-04 now has the mechanism it needs — an oracle that fails a plausible-but-wrong
  candidate whose statuses look fine. It is **not yet wired into the lifecycle**: the review stage does not
  run it, so the gates remain unclaimed.
- **Confidence**: High
- **Related**: EVD-022..025, OBS-012

## OBS-012 — Three defects found by testing the new code

- **Source**: this round's test runs
- **Fact**: (1) `AcceptanceContract.Digest` was a computed property, so `ToCanonicalJson()` serialised it and
  recursed into itself — a stack overflow that aborted the test run; fixed with `[JsonIgnore]`. (2) Storing the
  **canonical** form in the event made the proposal non-executable, because the canonical shape is not the
  contract schema and my parser could not re-read it; the operator bytes are now stored and the digest pins the
  canonical form. (3) Two of my own test expectations were wrong: a failed capture correctly cascades into the
  later scenarios, and ordered arrays report the first differing element, not the array.
- **Implications**: The cascade and the ordered-array rule are now asserted explicitly rather than left as
  undocumented behaviour.
- **Confidence**: High
- **Related**: OBS-011
## OBS-013 — The review gate is real: unverified candidates cannot advance

- **Source**: `FactoryStagePipeline.ReviewAsync`, `CandidateHost.cs`; Host 28/28; real run `m3b-f33d58e7`
- **Fact**: On `implementation.completed` the factory now runs a **separately executed build**, starts the
  candidate on a factory-allocated loopback port, verifies it owns that PID, executes the frozen contract over
  real HTTP, records four checks, and only then advances. Four tests prove the negatives: a wrong stored value
  stays `InReview`; a failed build never starts the candidate; an unverifiedprocess identity never advances.
  The real end-to-end run reached **`ReadyForDelivery` v9 in 62 s** with recorded evidence:
  `checks=validation=passed;requirement-coverage=passed;architectural-conformance=passed;security=passed`,
  `scenarios=AC-1=pass,AC-2=pass,AC-3=pass,AC-4=pass`, `reviewDigest=sha256:7f10b505…`,
  `contractDigest=sha256:1b27034b…`, `headSha=29dfabbc…`, `diffDigest=sha256:0915d8e9…`.
- **Implications**: The oracle is now a gate, not a library. An agent's assertion cannot substitute for it.
- **Confidence**: High
- **Related**: EVD-027..030, OBS-014

## OBS-014 — The real run caught the integration bug the stubs could not

- **Source**: first real review run, log record `event-quarantined reason=no-worktree-evidence`
- **Fact**: The review stage refused with `no-worktree-evidence` because `RealModelImplementationRunner` never
  passed its worktree path into `ImplementationOutcome`; the stub-based tests passed because they inject the
  path directly. Fixing the runner made the real run reach `ReadyForDelivery`.
- **Implications**: Precisely the failure mode the harness warns about — a mechanism proven with stubs is not
  proven. The integration path had to be run.
- **Confidence**: High
- **Related**: OBS-013
## OBS-015 — The merge lifecycle exists, with a drift guard and post-merge proof

- **Source**: `FactoryStagePipeline` (DeliveryValidation, ReleaseAsync), `MergeEngine.cs`, `FactoryRuntime.ApproveMergeAsync`; Host 33/33
- **Fact**: `factory approve --work-item --review-digest` records a **separate** operator authorisation bound to the
  recorded review digest; a mismatched digest is refused (`review-digest-mismatch`). Approval moves the item to
  `DeliveryValidation`, where the target branch's actual HEAD is compared with the approved base — a moved
  branch records `target-drift` and merges nothing. Otherwise the item becomes `ReleaseCandidate`, the real
  git merge runs with **reconciliation** (if the approved change is already an ancestor, it does not merge
  again), the merged revision is materialised in a detached worktree, rebuilt, started and re-verified against
  the frozen contract, and only then becomes `Released`. Five tests cover: approved → Released with exactly one
  merge; wrong digest refused; drift blocks the merge; post-merge contract failure is not Released; merge
  conflict is not Released.
- **Implications**: PA-03's "merged revision verified" and PA-06's digest binding now have mechanisms. They are
  proven with stubs and a stub merge engine; the real git path has **not** yet been exercised end to end.
- **Confidence**: High
- **Related**: EVD-031..033, OBS-016

## OBS-016 — Real merge path not yet run

- **Source**: scope of this round
- **Fact**: `GitMergeEngine` (merge, reconciliation, worktree materialisation) compiles and is wired into the
  CLI, but no end-to-end operator run has executed it. The merge tests use `StubMerge`.
- **Implications**: Per the lesson from OBS-014, this is not yet evidence. The next round must run the real
  path before any claim about merging.
- **Confidence**: High
- **Related**: OBS-015, OBS-014
## OBS-017 — The real path reaches Released, and the merged revision is the repository's HEAD

- **Source**: operator run `%TEMP%\m4b-517f1861` (no stubs anywhere)
- **Fact**: submit → contract approve → run (52 s: implementation + review → `ReadyForDelivery` v9) →
  `factory approve --review-digest …` → run (4 s: merge + post-merge verification) → **`Released` v13**.
  Target `main` now reads `e9bb30a merge 27e0bcf…`, `27e0bcf feat: implement …`, `24b3af9 chore: initialise`.
  The recorded release evidence is: `mergedSha=e9bb30abf933330b812c931a56637f26c6838f51` (identical to
  `git rev-parse HEAD`), `postMergeDetail=contract-passed-on-merged-revision`,
  `postMergeScenarios=AC-1=pass,AC-2=pass,AC-3=pass,AC-4=pass`, plus the review and contract digests.
- **Implications**: PA-03's "merged revision verified" is now demonstrated end to end with real git, and the
  merged revision is provably the one the repository holds. This is the first time the full operator path ran
  without a stub at any stage.
- **Confidence**: High
- **Related**: EVD-035, EVD-036, OBS-018, OBS-019

## OBS-018 — Three wiring defects, each fatal, each invisible to tests

- **Source**: the same run, twice
- **Fact**: (1) `factory approve` was not routed by `Program.Main`, so the verb fell through to "not
  implemented in this increment". (2) The CLI constructed `FactoryRuntime` without a merge engine, so the
  release stage failed with `merge-engine-unavailable` and the merge never ran. (3) The generated `.gitignore`
  contained a literal backtick-n (`bin/`nobj/`n`) because a PowerShell here-string swallowed the escape, so it
  ignored nothing and **35 `obj/` entries were committed** into the merged tree.
- **Implications**: The third is the most instructive: the ignore file existed, looked right in a directory
  listing, and did nothing. It is now a public constant with a test that asserts real lines and no backtick.
  All three were wiring, not logic — the class of defect that unit tests by construction cannot see.
- **Confidence**: High
- **Related**: OBS-017

## OBS-019 — D-06 is violated by the reused WIP worktree provider

- **Source**: `Wip.Workspaces.Git/WipWorkspaceProviderGit.cs:77`
- **Fact**: the provider hardcodes `<repository>/.wip/worktrees/<sessionId>`, so session worktrees live **inside**
  the target repository. v0.3 §9 requires them under the control root, and the target repository must never
  contain control state; after a run the target repo shows an untracked `.wip/` directory.
- **Implications**: This is a spec-versus-baseline conflict, not a bug I can fix in the factory alone: the
  factory must not create a second worktree engine (N-02), so the fix is an additive `worktreeRoot` parameter
  on the existing provider. Until then D-06 is only partially met — the control root itself is outside the
  repo, but worktrees are not. Recorded as RISK-002.
- **Confidence**: High
- **Related**: OBS-017, RISK-002
## RISK-002 — Worktrees live inside the target repository (D-06 partial)

- **Risk**: `WipWorkspaceProviderGit` hardcodes `<repo>/.wip/worktrees`. The target repo therefore accumulates
  factory control state, violating v0.3 §9 and leaving an untracked `.wip/` after every run.
- **Mitigation**: additive `worktreeRoot` option on the existing provider (no second engine), passed from the
  control root; then a test asserting no `.wip/` appears in the target repo.
- **Status**: open