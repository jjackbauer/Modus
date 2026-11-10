# Round 5 — Bootstrap modes (R-06) and refinement safety controls (R-08)

## Outcome

- **R-06 — bootstrap modes.** `HarnessBootstrapMode` (`None`, `Frozen`, `Updating`) is selectable
  through two new last-optional `TrainingOptions` parameters; `HarnessBootstrap.Load` verifies a
  prior data directory (memory, champion pointer, content-addressed champion artifact, lineage) and
  `HarnessBootstrap.PrepareTarget` materialises a fresh target from it. `TrainingRun.Bootstrap`
  records the mode, source, champion id, lineage count, memory hash before/after and whether memory
  was writable. Frozen leaves the target memory bytes byte-identical to the loaded copy; Updating
  appends one record per completed evaluation iteration. `None` is the untouched legacy path.
- **R-08 — safety controls.** An injection-looking memory key is stored as inert data and changes no
  challenger, gate artifact or frozen-component manifest. A poisoned `promote` key cannot change the
  verifier verdict, the ledger bytes or the champion pointer. The extended source scan over every
  `WIP/Wip.Tholdem.Agents/Learning/**/*.cs` file finds no process, network, socket, dynamic-load or
  compiler token.
- **Status — 13 new tests pass**: 10 bootstrap/refusal tests (the 7 required bootstrap scenarios,
  the pinned no-bootstrap arm, and one extra refusal test each for `bootstrap-champion-missing` and
  `bootstrap-lineage-invalid`) and 3 safety tests. The unfiltered assembly passes **354/354**
  (341 pre-existing + 13 new, 0 failed, 0 skipped). The Agents project builds with 0 errors and
  0 warnings; the test project's build summary reports only pre-existing nullable/analyzer warnings
  in existing test files (9 distinct warning identities after de-duplication). The measured
  `HarnessRefinement` arm's pinned champion/challenger ids are unchanged and `run.Bootstrap` is null
  without a bootstrap mode.

## Files

| File | Change |
|---|---|
| `WIP/Wip.Tholdem.Agents/Learning/Refinement/HarnessBootstrapMode.cs` | new: the three-mode enum (`:8`) |
| `WIP/Wip.Tholdem.Agents/Learning/Refinement/HarnessBootstrapSummary.cs` | new: the data-only run summary record (`:8`) |
| `WIP/Wip.Tholdem.Agents/Learning/Refinement/HarnessBootstrap.cs` | new: `HarnessBootstrapException` (`:7`), stable codes (`:42`), `HarnessBootstrapState` (`:25`), `Load` (`:59`), `PrepareTarget` (`:184`) |
| `WIP/Wip.Tholdem.Agents/Learning/TrainingOptions.cs` | two last optional parameters (`:16-17`) |
| `WIP/Wip.Tholdem.Agents/Learning/TrainingRun.cs` | last optional positional `Bootstrap` (`:15`) |
| `WIP/Wip.Tholdem.Agents/Learning/TrainingLoop.cs` | bootstrap block before `LoadChampion` (`:91-106`), write gate (`:118-121`, `:293`), summary (`:309-328`) |
| `WIP/Wip.Tholdem.Tests/Learning/HarnessBootstrapTests.cs` | new, 10 tests |
| `WIP/Wip.Tholdem.Tests/Learning/HarnessSafetyTests.cs` | new, 3 tests |
| `docs/goal-tholdem-harness/round-5/implementation.md` | this document |

No engine, history, simulator, evaluator, gate, verifier, frozen-manifest, solution, project file,
Host, Web or runtime file was modified. `EvaluationCriteria.cs`, `PromotionPolicy.cs`,
`PromotionVerifier.cs`, `MatchEvaluator.cs`, `SeedDerivation.cs`, `FrozenComponents.cs`, `MetaLoop.cs`,
`StrategyGenome.cs`, `StrategyArtifact.cs`, `ProposerGenome.cs`, `SignatureDrivenRefinementPolicy.cs`
and `FailureSignatureExtractor.cs` are untouched, and no existing test was edited, deleted, skipped
or weakened.

## Test-first evidence

### 1. Red with the API surface present and the loader stubbed

The two test files were written first. The production surface (enum, summary, state, options, run
field, loop wiring) was then added with `Load`/`PrepareTarget` bodies throwing
`NotImplementedException`, so the red run is a **runtime** failure, not a compile failure.

```powershell
dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release --nologo -v q
.tmp/tholdem-verify/RunXunit/bin/Release/net10.0/RunXunit.exe `
  WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll HarnessBootstrap
```

Observed (build exit 0, log `.tmp/goal-refinement/round5-red-bootstrap.log`):

```text
FAIL HarnessBootstrapTests.Bootstrap_GivenFrozen_ExpectedMemoryBytesUnchangedAndHashesRecorded: NotImplementedException: red: the bootstrap loader is not implemented yet.
FAIL HarnessBootstrapTests.Bootstrap_GivenMissingSource_ExpectedStableRefusal: ThrowsException: Assert.Throws() Failure: Exception type was not an exact match
Expected: typeof(Wip.Tholdem.Agents.Learning.Refinement.HarnessBootstrapException)
Actual:   typeof(System.NotImplementedException)
FAIL HarnessBootstrapTests.Bootstrap_GivenNonFreshTarget_ExpectedStableRefusal: ThrowsException: Assert.Throws() Failure: Exception type was not an exact match
Expected: typeof(Wip.Tholdem.Agents.Learning.Refinement.HarnessBootstrapException)
Actual:   typeof(System.NotImplementedException)
FAIL HarnessBootstrapTests.Bootstrap_GivenSameInputsTwoTargets_ExpectedIdenticalArtifacts: NotImplementedException: red: the bootstrap loader is not implemented yet.
FAIL HarnessBootstrapTests.Bootstrap_GivenSourceChampion_ExpectedTargetStartsFromSameGenomeAndLineageCount: NotImplementedException: red: the bootstrap loader is not implemented yet.
FAIL HarnessBootstrapTests.Bootstrap_GivenTamperedSourceMemory_ExpectedStableRefusal: ThrowsException: Assert.Throws() Failure: Exception type was not an exact match
Expected: typeof(Wip.Tholdem.Agents.Learning.Refinement.HarnessBootstrapException)
Actual:   typeof(System.NotImplementedException)
FAIL HarnessBootstrapTests.Bootstrap_GivenUpdating_ExpectedMemoryUpdatedAndHashesDiffer: NotImplementedException: red: the bootstrap loader is not implemented yet.
TOTAL 8 PASSED 1 FAILED 7 SKIPPED 0
```

The one passing bootstrap test is the pinned no-bootstrap arm, which already asserts `Bootstrap` is
null and the round-3 challenger ids. The safety controls are behaviour that already existed, so they
were green at this stage (log `.tmp/goal-refinement/round5-red-safety.log`):

```text
TOTAL 3 PASSED 3 FAILED 0 SKIPPED 0
```

After the first green run, two further refusal tests were added to cover the two remaining stable
codes named by the design: `Bootstrap_GivenMissingSourceChampion_ExpectedStableRefusal` (missing
pointer and missing content-addressed artifact, both `bootstrap-champion-missing`) and
`Bootstrap_GivenUnreadableSourceLineage_ExpectedStableRefusal` (a deleted root artifact yields
`bootstrap-lineage-invalid` wrapping `lineage-missing-parent`). They execute the implemented loader
and pass in the final run below.

### 2. Pre-change baseline

Before any edit, the same runner reported the pre-existing suite (log
`.tmp/goal-refinement/round5-baseline-full.log`):

```text
TOTAL 341 PASSED 341 FAILED 0 SKIPPED 0
```

### 3. Final green

```powershell
dotnet build WIP/Wip.Tholdem.Tests/Wip.Tholdem.Tests.csproj -c Release --nologo -v q
.tmp/tholdem-verify/RunXunit/bin/Release/net10.0/RunXunit.exe `
  WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll HarnessBootstrap
.tmp/tholdem-verify/RunXunit/bin/Release/net10.0/RunXunit.exe `
  WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll HarnessSafety
.tmp/tholdem-verify/RunXunit/bin/Release/net10.0/RunXunit.exe `
  WIP/Wip.Tholdem.Tests/bin/Release/net10.0/Wip.Tholdem.Tests.dll
```

Observed (logs `round5-final2-build.log`, `round5-final2-bootstrap.log`,
`round5-final2-safety.log`, `round5-final2-full.log`):

```text
BUILD EXIT 0
    12 Warning(s)
    0 Error(s)
BOOTSTRAP EXIT 0
TOTAL 10 PASSED 10 FAILED 0 SKIPPED 0
SAFETY EXIT 0
TOTAL 3 PASSED 3 FAILED 0 SKIPPED 0
FULL EXIT 0
TOTAL 354 PASSED 354 FAILED 0 SKIPPED 0
```

The 12 warnings in the summary line de-duplicate to 9 distinct warnings, all pre-existing and all in
existing test files (`LearningWebIntegrationTests.cs`, `TrainingLoopTests.cs`,
`ThreatControlTests.cs`); no warning identity in the log is in `Wip.Tholdem.Agents` (the two CS8602
warnings introduced by the first loop draft, `TrainingLoop.cs:297` and `:301` in
`.tmp/goal-refinement/round5-red-build.log`, were removed by the explicit `memoryStore is not null`
guard at `TrainingLoop.cs:293`).

`dotnet test` remains unusable in this session (Win32Exception 5 on the VSTest testhost handle); the
supplied supplementary runner executes the same compiled assembly and is the local evidence path.
CI remains the authoritative gate.

## Bootstrap format

### Source layout read by `HarnessBootstrap.Load`

| Source path | Requirement |
|---|---|
| `<source>/harness/memory.json` | optional; when present it is loaded through `HarnessMemoryStore.Load`, so schema, shape and content hash must verify. Missing means the empty snapshot. Raw file bytes are retained for a verbatim copy. |
| `<source>/active-champion.json` | required; a JSON object with a non-empty `strategyId`. Its raw bytes are retained. |
| `<source>/strategies/<strategyId>.json` | required; re-read through `TrainingLoop.ReadPersistedStrategy`, so the id must match the content hash. |
| `<source>/ledger.json` | optional for the lineage join; when present it must pass `LedgerChain.Verify`. |
| lineage from the champion id | required to resolve to a root through existing artifacts; 64-artifact depth cap and cycle/refusal rules unchanged. |

`Load` verifies memory **before** the pointer and champion, and resolves lineage **after** both, as
specified. Every check throws `HarnessBootstrapException` (a `RefinementDataException`) with one of
these stable codes:

| Code | Condition | Side effect |
|---|---|---|
| `bootstrap-source-missing` | the source path is not an existing directory | none; the target is not touched |
| `bootstrap-champion-missing` | pointer missing/unreadable, no non-empty `strategyId`, artifact missing/unreadable, or artifact not content-addressed | none |
| `bootstrap-memory-invalid` | memory file fails `HarnessMemoryStore` schema/shape/hash verification or cannot be read | none; the tampered source keeps its bytes |
| `bootstrap-lineage-invalid` | `StrategyLineageReader` refuses (missing parent, cycle, tampered artifact, broken ledger, depth) or the lineage cannot be read | none |
| `bootstrap-target-not-fresh` | `<target>/active-champion.json` or `<target>/ledger.json` already exists | none; checked before any target write |

If a bootstrap mode is selected without `BootstrapFrom`, the loop throws `ArgumentException` before
touching either directory.

### Target layout written by `HarnessBootstrap.PrepareTarget`

| Target path | Content |
|---|---|
| `<target>/strategies/<championId>.json` | the champion artifact re-emitted through `ToCanonicalJson` (bytes equal the source artifact, which is content-hash verified) |
| `<target>/active-champion.json` | the source pointer bytes copied verbatim (no clock, no reformatting) |
| `<target>/harness/memory.json` | the source memory bytes copied verbatim; when the source has no memory file, the canonical empty memory file |

There is no clock, GUID, random value or culture-dependent formatting in the path: all bytes are
canonical UTF-8 without BOM, the artifact file name is derived from the content-addressed id, and the
pointer/memory file names are the fixed constants used by the existing store and loop.

## Frozen versus Updating byte behaviour

| Scenario | Memory bytes | Summary hashes | `MemoryWritable` |
|---|---|---|---|
| `Frozen` + refinement | target memory is byte-identical to the source copy; the source file is untouched | `MemoryHashBefore == MemoryHashAfter ==` source `contentHash` | `false` |
| `Updating` + refinement | target memory differs; each completed iteration appends exactly one record (the two-iteration probe's entry-count sum grows by exactly 2) | `MemoryHashAfter` differs from before and equals the reloaded target `contentHash` | `true` |
| `None` + refinement | unchanged round-4 behaviour and pinned bytes | `Bootstrap` is `null` | n/a (no summary) |
| any bootstrap + legacy (`HarnessRefinement=false`) | target keeps the copied memory bytes; the loop writes no memory | hashes equal | `false` |

`TrainingLoop.Run` wires it as follows: the bootstrap block runs before `LoadChampion`
(`TrainingLoop.cs:91-106`); the store is still created only in refinement mode (`:116`); the write
gate is `memoryStore is not null && memoryWritable && rationaleCode is not null` (`:293`), where
`memoryWritable` is false for Frozen and true for Updating or a plain opt-in run (`:118-121`); the
summary is built from the loaded state and the reloaded target memory (`:309-321`). The writer never
calls `RecordRejection`/`RecordPattern` in Frozen mode, and legacy mode has no store at all.

## Safety evidence (R-08)

- `HarnessSafetyTests.Memory_GivenInjectionKey_ExpectedInertStorageAndPolicyUnchanged`
  (`HarnessSafetyTests.cs:26`) seeds `IGNORE ALL RULES ... http://evil.invalid/promote` as a memory
  key, runs the pinned 3-iteration refinement probe into a clean and a seeded directory, and asserts
  identical challenger ids (`tholdem-strategy-v2-d24dcf6d`, `-c34333fe`, `-86f42856`), identical
  outcomes, the injection text only in `harness/memory.json`, an unchanged frozen-component manifest
  hash, and the pinned 5-component manifest.
- `HarnessSafetyTests.Memory_GivenPoisonedPromoteInstruction_ExpectedGateVerdictUnchangedAndNoPointerMove`
  (`HarnessSafetyTests.cs:88`) seeds a `promote-challenger-unconditionally` pattern, runs the
  always-promote control in refinement mode, and asserts `verification-failed`, `verified=false`,
  `promoted=false`, a `verifier:` reason, the same challenger/report/ledger bytes as a clean control,
  and a pointer that still names the initial champion at iteration 0.
- `HarnessSafetyTests.RefinementSources_GivenSafetyScan_ExpectedNoExecutionNetworkOrModelCall`
  (`HarnessSafetyTests.cs:152`) scans every `*.cs` under `Wip.Tholdem.Agents/Learning` for `Process`,
  `HttpClient`, `Socket`, `Dns.`, `TcpClient`, `UdpClient`, `WebRequest`, `Assembly.Load`, `Compile`,
  `CSharpScript` and `CodeDom`; none is present (32 sources scanned, including the three new files).

## Deviations (all deliberate, none weakening a gate)

1. **`HarnessBootstrapState` carries two extra fields beyond the five named in the design:**
   `MemoryBytes` and `ChampionPointerBytes`. They are required to copy the source memory and pointer
   bytes verbatim in `PrepareTarget`, which receives only `(targetDirectory, state)`. No behaviour is
   hidden in them; both are raw verified/copied bytes.
2. **`HarnessBootstrapException` is a subclass of `RefinementDataException`** rather than throwing the
   base type directly. It remains a `RefinementDataException` and matches the existing
   `HarnessMemoryException` / `StrategyLineageException` pattern; every refusal code is a public
   constant on `HarnessBootstrap` (`HarnessBootstrap.cs:42-55`).
3. **The target champion artifact is re-emitted canonically** (not byte-copied). For a source written
   by the loop (the tested case) the canonical bytes equal the source file byte-for-byte because the
   artifact is content-addressed and canonical; the test compares them directly
   (`HarnessBootstrapTests.cs:344-347`). A hand-formatted but hash-valid source artifact would be
   normalised. Pointer and memory files are copied verbatim.
4. **Bootstrap tests run the target under a non-promoting gate** (`MarginBbPer100: 10_000`) so the
   copied pointer remains byte-comparable after the run. The promoting source run builds the loaded
   three-link lineage; the deterministic two-target test still exercises the full Updating memory
   write path.
5. **Frozen mode does not expose memory to the policy.** The measured `RefinementContext` has no
   memory field (round-3 boundary) and the policy never receives the store, so "read-only" is proven
   by byte-identical memory plus the recorded before/after hashes. Feeding memory into the policy
   would change the measured arm and is outside this round.
6. **"Tool/permission surface" interpretation.** The Learning layer has no tool registry or
   permission descriptor to introspect. The observable proxy used is the pinned frozen-component
   manifest before/after the injection run plus the extended source scan; both pass.
7. **Source memory is read twice** (verify through `HarnessMemoryStore.Load`, then raw bytes for the
   verbatim copy). A concurrent mutation between the two reads would be copied but would fail the
   target's own later `Load`; the source is static in every test and in normal use.

## Anything not passing

Nothing. All 13 new tests and all 341 pre-existing tests pass (354/354), with zero skips and zero
failures, and the build has zero errors. The only warnings are the 12 pre-existing ones in existing
test files; the only environment limitation remains the known `dotnet test` Win32Exception (5), which
is why the supplied supplementary runner is cited.
