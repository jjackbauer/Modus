# Round 4 — Canonical harness memory (R-03) and strategy lineage (R-04)

## Outcome

- **R-03** — `HarnessMemoryStore` persists a canonical, content-hashed harness memory at
  `dataDirectory/harness/memory.json`, schema `tholdem.harness-memory.v1`. Entries are keyed by
  approach, kinded `pattern` / `failed-approach` / `dead-end`, capped at 64 with deterministic
  least-recently-seen eviction, and a tampered file is refused on both load and write. Missing file is
  an empty snapshot.
- **R-04** — `StrategyLineageReader` walks the active champion's `parentId` chain from real persisted
  artifacts, verifies the ledger hash chain, joins each artifact to the ledger row that evaluated it,
  and returns entries root-first with a 64-artifact depth cap and stable refusals for a missing
  parent, a cycle, an artifact hash mismatch and a broken ledger.
- **Integration** — `TrainingLoop.Run` records the refinement proposal's `RationaleCode` after every
  completed evaluation iteration **in refinement mode only**, write-only: rejected/verification-failed
  → `RecordRejection`, promoted → `RecordPattern`. The memory store is never given to the policy, and
  the pinned challenger choices of the already-accepted round-3 refinement arm are unchanged.
- **Status** — 16 new tests pass (10 memory/integration, 6 lineage); the unfiltered assembly passes
  **341/341** (325 pre-existing + 16 new); the Agents project builds with 0 errors and only the two
  pre-existing `Wip.Runtime` warnings; legacy mode reproduces all 7 recorded baseline hashes in two
  fresh directories and writes no memory file.

## Files

| File | Change |
|---|---|
| `WIP/Wip.Tholdem.Agents/Learning/Refinement/HarnessMemoryStore.cs` | new: kinds, entry, snapshot, `RefinementDataException`, `HarnessMemoryException`, store (`:87`) |
| `WIP/Wip.Tholdem.Agents/Learning/Refinement/StrategyLineageReader.cs` | new: lineage entry (`:11`), exception (`:23`), reader (`:49`) |
| `WIP/Wip.Tholdem.Agents/Learning/TrainingLoop.cs` | minimal integration: store creation `:98`, rationale capture `:159-166`, write-only record `:267-281` |
| `WIP/Wip.Tholdem.Tests/Learning/HarnessMemoryTests.cs` | new, 10 tests |
| `WIP/Wip.Tholdem.Tests/Learning/StrategyLineageReaderTests.cs` | new, 6 tests |
| `WIP/Wip.Tholdem.Tests/Learning/HarnessRefinementTests.cs` | one stale scratch assertion scoped at `:72-74` (authorized; see deviations) |
| `docs/goal-tholdem-harness/round-4/implementation.md` | this document |

No engine, history, simulator, evaluator, gate, verifier, frozen-manifest, solution, project file,
Host, Web or runtime file was modified. No existing test other than the authorized `:72` predicate was
edited, and no assertion was deleted or skipped.

## Test-first evidence

### 1. Red before the production types existed (compile failure)

The two new test files were written first; the production types did not exist. Command:

```powershell
dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release --nologo -v q
```

Observed:

```text
...HarnessMemoryTests.cs(491,20): error CS0246: The type or namespace name 'HarnessMemorySnapshot'
  could not be found (are you missing a using directive or an assembly reference?)
...HarnessMemoryTests.cs(506,20): error CS0246: The type or namespace name 'HarnessMemorySnapshot'
  could not be found (are you missing a using directive or an assembly reference?)
    0 Warning(s)
    2 Error(s)
```

This compile phase is recorded for completeness only; it is **not** accepted as test evidence.

### 2. Red with only the API surface present (runtime failures, not compile failures)

The new types were added with `NotImplementedException` bodies (all removed before the green run), and
the loop had no memory write yet. Command:

```powershell
.tmp/tholdem-verify/RunXunit/bin/Release/net10.0/RunXunit.exe `
  WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll HarnessMemoryTests
```

Observed (log `.tmp/goal-refinement/round4-red-memory.log`):

```text
FAIL HarnessMemoryTests.Memory_GivenRejectedChallenger_ExpectedFailedApproachEntry: NotImplementedException: red: harness memory is not implemented yet.
FAIL HarnessMemoryTests.Memory_GivenThirdRejectionOfSameKey_ExpectedDeadEnd: NotImplementedException: red: harness memory is not implemented yet.
FAIL HarnessMemoryTests.Memory_GivenEntriesAboveCap_ExpectedDeterministicEvictionAndStableHash: NotImplementedException: red: harness memory is not implemented yet.
FAIL HarnessMemoryTests.Memory_GivenTamperedFile_ExpectedRefusal: NotImplementedException: red: harness memory is not implemented yet.
FAIL HarnessMemoryTests.Memory_GivenSameInputsTwoDirectories_ExpectedIdenticalBytes: NotImplementedException: red: harness memory is not implemented yet.
FAIL HarnessMemoryTests.Run_GivenPromotedRefinementMode_ExpectedPatternEntries: FileNotFoundException: Could not find file '...\harness\memory.json'.
FAIL HarnessMemoryTests.Run_GivenRefinementMode_ExpectedMemoryWrittenAndPolicyUnchanged: NotImplementedException: red: harness memory is not implemented yet.
TOTAL 9 PASSED 1 FAILED 8 SKIPPED 0
```

The single passing test was `Run_GivenLegacyMode_ExpectedNoMemoryFile`, which asserts the untouched
legacy path. Lineage command:

```powershell
.tmp/tholdem-verify/RunXunit/bin/Release/net10.0/RunXunit.exe `
  WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll StrategyLineageReaderTests
```

Observed (log `.tmp/goal-refinement/round4-red-lineage.log`):

```text
FAIL StrategyLineageReaderTests.Lineage_GivenCycle_ExpectedCycleRefusal: ThrowsException: Assert.Throws() Failure: Exception type was not an exact match
Expected: typeof(Wip.Tholdem.Agents.Learning.Refinement.StrategyLineageException)
Actual:   typeof(System.NotImplementedException)
FAIL StrategyLineageReaderTests.Lineage_GivenMissingParent_ExpectedStableRefusal: ThrowsException: Assert.Throws() Failure: Exception type was not an exact match
FAIL StrategyLineageReaderTests.Lineage_GivenTamperedArtifact_ExpectedStableRefusal: ThrowsException: Assert.Throws() Failure: Exception type was not an exact match
FAIL StrategyLineageReaderTests.Lineage_GivenChampionChain_ExpectedEntriesWithOutcomes: NotImplementedException: red: strategy lineage is not implemented yet.
FAIL StrategyLineageReaderTests.Lineage_GivenTamperedLedger_ExpectedStableRefusal: NotImplementedException: red: strategy lineage is not implemented yet.
FAIL StrategyLineageReaderTests.Lineage_GivenChainAboveDepthCap_ExpectedDepthRefusal: NotImplementedException: red: strategy lineage is not implemented yet.
TOTAL 6 PASSED 0 FAILED 6 SKIPPED 0
```

### 3. Pre-fix full suite (observed stale-assertion failure)

The implementation writes `harness/memory.json` in refinement mode, which the pre-round-4 scratch
assertion at `HarnessRefinementTests.cs:72` treated as a failure. Full suite before the authorized fix:

```powershell
.tmp/tholdem-verify/RunXunit/bin/Release/net10.0/RunXunit.exe `
  WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
```

Observed (log `.tmp/goal-refinement/round4-prefix-full.log`):

```text
FAIL HarnessRefinementTests.Capture_GivenTrainingRun_ExpectedAllHandsAndScratchRemoved: EmptyException: Assert.Empty() Failure: Collection was not empty
Collection: ["C:\\Users\\ricar\\dev\\Modus\\.tmp\\tholdem-verify"···]
TOTAL 340 PASSED 339 FAILED 1 SKIPPED 0
```

This is exactly one failure, and it is the stale predicate described under deviations. After the
authorized one-line scope correction the same suite is green (below).

## Green evidence

### Builds

```powershell
dotnet build WIP/Wip.Tholdem.Agents/Wip.Tholdem.Agents.csproj -c Release --no-incremental --nologo -v m
```

Observed: `Build succeeded.` — `2 Warning(s)`, `0 Error(s)`. Both warnings are pre-existing and in
untouched files: `WIP/Wip.Runtime/Runtime/AgentExecutor.cs(143,53)` (CS8425) and
`WIP/Wip.Runtime/Runtime/DeepSeekChatModel.cs(125,17)` (CA2024).

```powershell
dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release --nologo -v q
```

Observed: `Build succeeded.` — `14 Warning(s)`, `0 Error(s)`, the same count as the pre-change
baseline build recorded at the start of this round. No warning originates in a file added or changed
by this round.

### Focused tests

```powershell
RunXunit.exe WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll HarnessMemoryTests
RunXunit.exe WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll StrategyLineageReaderTests
```

Observed (`.tmp/goal-refinement/round4-green-memory.log`, `round4-green-lineage.log`):

```text
TOTAL 10 PASSED 10 FAILED 0 SKIPPED 0
TOTAL 6 PASSED 6 FAILED 0 SKIPPED 0
```

### Full unfiltered suite

```powershell
.tmp/tholdem-verify/RunXunit/bin/Release/net10.0/RunXunit.exe `
  WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
```

Observed (log `.tmp/goal-refinement/round4-green-full.log`):

```text
TOTAL 341 PASSED 341 FAILED 0 SKIPPED 0
```

341 = 325 pre-existing + 16 new tests. `dotnet test` remains unusable on this machine
(`Win32Exception (5)` in the VSTest testhost), so the repository's supplementary runner was used, as
in rounds 1–3; CI remains the authoritative engine.

### Legacy equivalence (D1 re-check)

Real Host CLI runs with the recorded baseline seed into two fresh directories:

```powershell
dotnet run --project WIP/Wip.Tholdem.Host -c Release -- train --seed 1099511627776 --iterations 2 --data-dir .tmp/goal-refinement/round4-legacy-a
dotnet run --project WIP/Wip.Tholdem.Host -c Release --no-build -- train --seed 1099511627776 --iterations 2 --data-dir .tmp/goal-refinement/round4-legacy-b
```

Both runs printed the recorded round-0 iterations
(`v2-0ceaa8fd` rejected, `v2-e1ba56f7` rejected, champion `v1-f100e793`). SHA-256 against
`docs/goal-tholdem-harness/round-0/baseline-hashes.txt:5-11`:

```text
baseline entries=7
round4-legacy-a files=7
round4-legacy-b files=7
TOTAL_MISMATCH=0
harnessA=False harnessB=False
memoryA=False
```

That is: 7/7 recorded hashes in each directory, no `harness` directory and no memory file with
refinement off. The in-suite `Run_GivenLegacyMode_ExpectedNoMemoryFile`
(`HarnessMemoryTests.cs:479`) also compares every produced JSON byte across two fresh legacy
directories and asserts exactly 5 files and no memory file.

## Members implemented

| Member | Location | Behaviour |
|---|---|---|
| `HarnessMemoryKinds` | `HarnessMemoryStore.cs:9` | fixed `pattern` / `failed-approach` / `dead-end` vocabulary |
| `HarnessMemoryEntry` | `:22` | `Kind`, `Key`, `Count`, `FirstIteration`, `LastIteration` |
| `HarnessMemorySnapshot` | `:29` | immutable verified view; `ContentHash`; `ToCanonicalJson()` |
| `RefinementDataException` | `:42` | base refusal with stable machine-readable `Code` |
| `HarnessMemoryException` | `:60` | memory-specific refusal |
| `HarnessMemoryStore` | `:87` | file at `dataDirectory/harness/memory.json` (`:111-119`) |
| `HarnessMemoryStore.Load` | `:125` | missing ⇒ empty; schema/shape/hash verified; stable `Code` on refusal |
| `HarnessMemoryStore.RecordRejection` | `:223` | upsert, `Count++`, `LastIteration`, dead end at `Count >= 3` |
| `HarnessMemoryStore.RecordPattern` | `:227` | upsert as `pattern`; dead end remains absorbing |
| `BuildEntriesJson` / `ComputeContentHash` / `BuildCanonicalJson` | `:231`, `:263`, `:267` | fixed key order, invariant integers, SHA-256 over the entries array |
| `Record` / `WriteAtomic` | `:281`, `:330` | loads and re-verifies first, evicts over cap, sorts by key, temp-file + move |
| `StrategyLineageEntry` | `StrategyLineageReader.cs:11` | `Id`, `Version`, `ParentId`, `CreatedAtIteration`, nullable `Outcome`/`Reason`/`MeanBbPer100`/`LowerBoundBbPer100`/`Seeds` |
| `StrategyLineageException` | `:23` | dedicated stable-code refusal |
| `StrategyLineageReader.Read` | `:66` | ledger verify, shape walk, content verify, root-first entries |
| depth and refusal codes | `:52-64` | `MaximumDepth = 64`; `lineage-missing-parent`, `lineage-cycle`, `lineage-artifact-hash-mismatch`, `lineage-artifact-unreadable`, `lineage-ledger-integrity`, `lineage-depth-exceeded` |
| `ReadLedgerRows` / `BuildOutcomeIndex` | `:178`, `:166` | verifies the hash chain, tolerates absent ledger, last-wins on duplicate keys |
| `ReadRawParentId` | `:260` | structural `id`/`parentId` read used only for the acyclic-shape proof |
| loop integration | `TrainingLoop.cs:98`, `:159-166`, `:267-281` | refinement-only store, rationale capture, write-only record |

No stub or `NotImplementedException` remains:

```powershell
Select-String -Path WIP/Wip.Tholdem.Agents/Learning/Refinement/*.cs -Pattern 'NotImplementedException|red:'
```

Observed: no matches.

## R-03 canonical format and semantics

Exact file shape (one line, UTF-8 without BOM, no whitespace):

```json
{"schema":"tholdem.harness-memory.v1","contentHash":"<sha256>","entries":[{"kind":"failed-approach","key":"missed-value","count":1,"firstIteration":4,"lastIteration":4}]}
```

- `contentHash` is the lowercase SHA-256 of the canonical `entries` array only, in fixed key order
  `kind,key,count,firstIteration,lastIteration`, with invariant integer formatting. No clock, GUID,
  seed, card, culture or free text is persisted.
- `entries` is always ordered by ordinal key. A file is accepted only when its parsed entries
  canonicalise to the stored hash; an edited entry, a duplicate key, an unknown kind or a bad shape is
  refused (`memory-content-hash-mismatch` / `memory-malformed`). `Load` never writes, and
  `RecordRejection`/`RecordPattern` load first, so a tampered file blocks both reading and mutation.
- Upsert: `Count++`, `LastIteration = iteration`, `FirstIteration` set only on insert. At `Count >= 3`
  the kind is `dead-end`; a later pattern record keeps the dead end (absorbing) but still increments
  the count and last iteration.
- Cap: after an upsert, while there are more than 64 entries the entry with the lowest
  `LastIteration`, then the lowest ordinal key, is evicted. The new entry participates in that
  ordering, so the rule is deterministic for any input.
- Writes are atomic through a fixed `memory.json.tmp` sibling (deterministic name, no GUID) followed
  by `File.Move(..., overwrite: true)`.

## R-04 semantics

- The ledger is verified first (`LedgerChain.Verify`); invalid JSON, a broken link or an entry-hash
  mismatch refuses the read with `lineage-ledger-integrity`. An absent ledger is treated as no rows.
- The walk starts at `championId`, reads the artifact's `id` and `parentId`, and stops at a null
  parent. A repeated id is `lineage-cycle`; a missing artifact is `lineage-missing-parent`; the 65th
  artifact is `lineage-depth-exceeded`. All three refusals happen before any partial list exists.
- Every artifact in the chain is then re-read through `TrainingLoop.ReadPersistedStrategy`, which
  refuses an id that no longer matches its content hash (`lineage-artifact-hash-mismatch`).
- Outcomes come from the ledger row keyed by `(challengerId = entry.Id, championId = entry.ParentId)`,
  i.e. the evaluation this artifact received against its own parent. The root has no such row, so its
  outcome fields are null. A ledger row without metrics (an inert proposal) yields null metrics.
- Entries are returned root-first; versions ascend for every chain the loop can produce.

## No-memory-in-policy proof

The refinement policy cannot see memory or lineage:

1. `RefinementContext` has exactly `Incumbent`, `Analysis`, `Signatures`, `Iteration`
   (`IRefinementPolicy.cs:12-16`); no memory or lineage member exists to pass.
2. `TrainingLoop` constructs the store at `TrainingLoop.cs:98` but touches it only at `:267-281`,
   after the evaluation, verifier and persistence phases. The policy call at `:163-166` passes only the
   champion genome, the analyzed training report, the extracted signatures and the iteration.
3. `Run_GivenRefinementMode_ExpectedMemoryWrittenAndPolicyUnchanged`
   (`HarnessMemoryTests.cs:291`) proves the measured arm is unchanged four ways:
   - it pins the challenger ids recorded from the round-3 loop shape before this round's integration:
     `tholdem-strategy-v2-d24dcf6d`, `tholdem-strategy-v2-c34333fe`,
     `tholdem-strategy-v2-86f42856` (champion `tholdem-strategy-v1-f100e793`, all rejected);
   - a second fresh directory is pre-seeded with a **dead-end** memory entry for
     `aggression-by-street-low`; the run still chooses the identical champion/challenger pair for every
     iteration and increments the seeded entry (`3 → 5`) without changing a proposal;
   - a bare `new SignatureDrivenRefinementPolicy()` replay of each iteration's exact context reproduces
     the challenger genome (`Assert.Equal(challenger.Genome, proposal.Genome)`) and the recorded
     rationale codes `aggression-by-street-low`, `aggression-by-street-low`, `fold-to-raise`;
   - the memory file is asserted byte-for-byte against the pinned product of those rationales
     (`contentHash f7f6174a…`, two `failed-approach` entries).
4. `Run_GivenPromotedRefinementMode_ExpectedPatternEntries` (`HarnessMemoryTests.cs:403`) pins the
   promoted arm (`v2-d24dcf6d`, `v3-56423da7`, `v4-eaa7bf28`) and the exact `pattern` memory bytes
   (`contentHash c27aeb2e…`).
5. `Run_GivenInertProposal_ExpectedNoMemoryEntry` (`HarnessMemoryTests.cs:449`) shows a guard-refused
   proposal writes no memory entry and no evaluation file.

Pre-round-4 probe output used for the pins (run before the loop integration existed):

```text
standard iteration=1 champion=tholdem-strategy-v1-f100e793 challenger=tholdem-strategy-v2-d24dcf6d outcome=rejected
standard iteration=2 champion=tholdem-strategy-v1-f100e793 challenger=tholdem-strategy-v2-c34333fe outcome=rejected
standard iteration=3 champion=tholdem-strategy-v1-f100e793 challenger=tholdem-strategy-v2-86f42856 outcome=rejected
promoted iteration=1 champion=tholdem-strategy-v1-f100e793 challenger=tholdem-strategy-v2-d24dcf6d outcome=promoted
promoted iteration=2 champion=tholdem-strategy-v2-d24dcf6d challenger=tholdem-strategy-v3-56423da7 outcome=promoted
promoted iteration=3 champion=tholdem-strategy-v3-56423da7 challenger=tholdem-strategy-v4-eaa7bf28 outcome=promoted
standard replay iteration=1 rationale=aggression-by-street-low genomeMatches=True
standard replay iteration=2 rationale=aggression-by-street-low genomeMatches=True
standard replay iteration=3 rationale=fold-to-raise genomeMatches=True
```

## Deviations from REQUIRED DESIGN and interpretations

1. **`pattern` kind added (explicitly permitted).** `RecordPattern` is implemented; a record is
   `pattern` until the shared count reaches 3, at which point the entry is `dead-end`, and a dead end is
   never demoted by a later pattern. The promoted integration path therefore writes real `pattern`
   entries instead of a documented fallback.
2. **Lineage content verification follows the structural walk.** REQUIRED DESIGN says the reader
   "reads artifacts with `TrainingLoop.ReadPersistedStrategy`, follows `ParentId`". Literal
   hash-verified reads first make a *constructible* hash-valid cycle impossible without a fixed point:
   `StrategyArtifact.Hash` covers `parentId` and the id embeds its own hash suffix, so a 2-cycle needs
   `A_suffix = H_A(B_id)` and `B_suffix = H_B(A_id)` — a ~2^32 SHA-256 search, not deterministic test
   input. The reader therefore proves the shape first from raw `id`/`parentId` (missing parent, cycle,
   depth), then verifies the content hash of every returned artifact with
   `TrainingLoop.ReadPersistedStrategy`. No tampered artifact can contribute an entry; only the
   refusal *code* for a malformed graph is decided earlier. `Lineage_GivenCycle_ExpectedCycleRefusal`
   (`StrategyLineageReaderTests.cs:132`) proves a 2-cycle and a self-cycle are refused as
   `lineage-cycle`, not as hash mismatches.
3. **`RefinementDataException` derives from `Exception`, not `InvalidDataException`.** The first stub
   build observed `error CS0509: 'RefinementDataException': cannot derive from sealed type
   'InvalidDataException'`; `InvalidDataException` is sealed on this framework.
4. **Verification-failed iterations count as rejections.** A completed evaluation that the verifier
   refuses is not a promotion, so it records `failed-approach`; an inert guard refusal never reaches
   the memory write at all (asserted).
5. **Eviction candidate set includes the newly added entry.** This keeps the rule "lowest
   `LastIteration`, then key ordinal" total and deterministic even if a caller records an old
   iteration after newer ones. The test records increasing iterations, so the newly added `key-64`
   survives and the lowest-tied `key-00` is evicted.
6. **Canonical order is ordinal key order.** `Load` sorts parsed entries before hashing; the writer
   always stores sorted order, so a hand-edited array whose hash was recomputed over a different order
   is still refused by the content hash.
7. **Memory key/iteration domain.** Keys must be non-empty and at most 128 characters; iterations must
   be non-negative. These are the only inputs that can create an entry.
8. **Ledger join key.** Outcomes are joined by `(challengerId, parentId)`, not by challenger id alone,
   so the entry describes the evaluation against its own parent; duplicate ledger keys are last-wins
   (matching the loop's upsert semantics). An absent/empty ledger yields null outcome fields rather
   than a refusal, which keeps a fresh root-only directory readable.
9. **Depth cap interpretation.** 64 artifacts are allowed; the 65th refuses. Both are asserted in
   `Lineage_GivenChainAboveDepthCap_ExpectedDepthRefusal` (`StrategyLineageReaderTests.cs:233`).
10. **Authorized existing-test scope correction.** `HarnessRefinementTests.cs:72-74` changed the
    scratch predicate from `*.json` to `hand-*.json` and added one comment: `harness/memory.json` is a
    new persisted product artifact, not trajectory scratch. The test's intent — no captured hand file
    survives the scratch cleanup — is preserved, and the memory file's own existence, hash, tamper,
    eviction and legacy absence are proven by the new tests. This was authorized by the orchestrator
    after the pre-fix failure was reported (observed above: `TOTAL 340 PASSED 339 FAILED 1`); it is a
    scope correction of one overbroad predicate, not a weakened pass. No other assertion in that or
    any other existing test was changed.

## What could not be made to pass

Nothing. All 16 new tests and all 341 tests in the assembly pass. The only environment limitation is
unchanged from earlier rounds: `dotnet test` aborts with `Win32Exception (5)`, so the supplementary
`RunXunit` runner is the local evidence and CI remains authoritative. No publish, merge, deploy or
CI-green claim is made.

## Commands run

```powershell
dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release --nologo -v q
dotnet build WIP/Wip.Tholdem.Agents/Wip.Tholdem.Agents.csproj -c Release --no-incremental --nologo -v m
.tmp/tholdem-verify/RunXunit/bin/Release/net10.0/RunXunit.exe WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll HarnessMemoryTests
.tmp/tholdem-verify/RunXunit/bin/Release/net10.0/RunXunit.exe WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll StrategyLineageReaderTests
.tmp/tholdem-verify/RunXunit/bin/Release/net10.0/RunXunit.exe WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
dotnet run --project WIP/Wip.Tholdem.Host -c Release -- train --seed 1099511627776 --iterations 2 --data-dir .tmp/goal-refinement/round4-legacy-a
dotnet run --project WIP/Wip.Tholdem.Host -c Release --no-build -- train --seed 1099511627776 --iterations 2 --data-dir .tmp/goal-refinement/round4-legacy-b
Select-String -Path WIP/Wip.Tholdem.Agents/Learning/Refinement/*.cs -Pattern 'NotImplementedException|red:'
```

Logs: `.tmp/goal-refinement/round4-red-memory.log`, `round4-red-lineage.log`,
`round4-prefix-full.log`, `round4-green-memory.log`, `round4-green-lineage.log`,
`round4-green-full.log`.
