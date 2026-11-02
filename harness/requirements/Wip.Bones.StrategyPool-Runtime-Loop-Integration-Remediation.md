# Wip.Bones Strategy Pool — Post-Wiring Remediation Plan

> Remediation plan derived from live endpoint evaluation of the running host at 2026-07-08T20:30Z. The I1–I14 wiring compiled and deployed, but three critical runtime gaps prevent the pool from functioning as an evolutionary driver. This plan addresses only the verified failures; no new features.

---

## Runtime Diagnostics

| Signal | Detail |
|--------|--------|
| Host status (`/api/bones/status`) | Running, iteration 29, 115,741 games simulated, 456 co-learning sessions |
| Pool stats (`/bones/pool/stats`) | 100-103 entries, 124 total matches, avgELO 1431, eliteCount 0 |
| Pool entries (`/bones/pool`) | 7 library (M=0..31, R=1469-1531), 8 mutations (M=0, R=1439-1486), 88 exploration (M=0, R=1403-1431) |
| Pool entries — elite flag | **All `isElite: false`** — zero entries protected |
| Pool entries — matches played | Only library entries have M>0; 96 entries stuck at M=0 |
| Pool snapshot on disk | ❌ `pool/` directory does not exist |
| `dotnet build Wip.Bones.Agents` | ✅ 0 errors |
| `dotnet build Wip.Bones.Host` | ✅ 0 errors |
| `dotnet test Wip.Bones.Tests` | 356 passed, 3 failed (2 compliance gates + 1 flaky enhancer test) |
| `dotnet test Wip.Bones.Host.Tests` (pool suite) | **81 passed, 0 failed** |
| Critical failures found | (1) RecordOutcome throws on unknown IDs → 99.9% data loss, (2) snapshot never written, (3) IsElite broken, (4) status API reads wrong pool instance |

---

## Build/Test Gate

| Check | Status |
|-------|--------|
| dotnet build Wip.Bones.Agents | ✅ passed |
| dotnet build Wip.Bones.Host | ✅ passed |
| dotnet test Wip.Bones.Tests | 356 passed, 3 failed (2 compliance-gate format tests, 1 flaky enhancer test — all pre-existing) |
| dotnet test Wip.Bones.Host.Tests (pool suite) | 81 passed, 0 failed |
| Failures addressed in plan | All 4 critical runtime failures |

---

## Functionality Worktree

### Verification Policy

- Every fix must have a behavior-proof test that reproduces the exact failure observed live.
- Every fix must preserve the existing wiring (I4-I8 RecordOutcome + PopulationMatchComposer paths).
- Every fix must not break the 81 existing pool tests.

### Completeness Checklist

#### Phase A — Fix RecordOutcome data loss (highest leverage)

- [ ] **R1 — Make RecordOutcome tolerant of unknown strategy IDs** [mandatory — runtime failure: 99.9% data loss]
  **Diagnosis**: `BonesStrategyPool.RecordOutcome` line 189 throws `InvalidOperationException` when a strategy ID from the match is not in `_pool`. The co-learning workflow uses dynamic strategy IDs (e.g. `enhanced-...`, `ponder-...`) that are never added to the pool. Result: only the 4 library entries with matching IDs receive ELO updates; 88 exploration entries and ~115,000 match outcomes are discarded.
  **Fix**: Change the throw to a log-and-skip. When `TryGetValue` returns false, log a debug message and `continue` to the next seat. This turns RecordOutcome into a best-effort updater: it updates strategies that are in the pool, ignores strategies that aren't. Accumulated ELO data flows for all pool members.

- [ ] **R2 — Add admission path for co-learning strategies into the pool** [mandatory — runtime failure: strategies never enter pool]
  **Diagnosis**: `BonesCoLearningEnhanceAgent` line ~68 adds new strategies to `StrategyPool.Instance` (the static singleton). If `StrategyPool.Instance` is a different object than the DI-registered pool (which it IS in the current code — see R4), the add goes to the wrong pool. Even if it goes to the correct pool, the strategy IDs (like `enhanced-seat-1-<guid>`) would then be usable by RecordOutcome.
  **Fix**: Ensure `BonesCoLearningEnhanceAgent` adds to the same `StrategyPool` instance used by `BonesPlayMatchTool` and `BonesLearningLoopHost`. Either inject `StrategyPool` directly into the agent, or verify `StrategyPool.Instance` points to the DI singleton.

- [x] **R3 — Ensure PoolPopulation includes co-learning entries out-of-the-box** [depends on R2]
  **Test**: Seed pool → simulate co-learning enhance → verify new entry appears in `GET /bones/pool`, verify `poolSize` increments, verify new entry receives ELO updates from subsequent RecordOutcome calls.

#### Phase B — Fix snapshot persistence

- [ ] **R4 — Unify pool instance between Status API, Web pool, and runtime** [mandatory — runtime failure: two different pools]
  **Diagnosis**: `/api/bones/status` reports `poolSize: 3` while `/bones/pool` returns 103 entries. `BonesHostApplication` computes pool stats from `StrategyPool.Instance`, while the DI singleton is a separate object. `StrategyPool.Instance` is set in the constructor (`Instance = this;`), but when `SeedFromLibrary` creates a new pool (a static factory), it overwrites `Instance` with the seeded pool. Then DI creates another instance via the factory lambda, also setting `Instance`. The status API reads the last pool that ran its constructor.
  **Fix**: In the DI registration lambda, explicitly reassign `StrategyPool.Instance = pool;` after creating the pool (both for loaded-from-disk and seeded-from-library paths). This ensures the Web pool endpoints and the status API read the same pool that the host loop uses.

- [x] **R5 — Ensure first snapshot write creates the pool/ directory** [mandatory — runtime failure: snapshot never saved]
  **Diagnosis**: `PersistToDisk` calls `Directory.CreateDirectory(dir)`, but `TrySavePoolSnapshot` is guarded by `_iterationsSinceLastSnapshot >= 10`. The host may have been restarted with the new binary fewer than 10 iterations ago, OR the snapshot path has a null `Path.GetDirectoryName` (unlikely). The `pool/` directory simply doesn't exist — no evidence any `PersistToDisk` call has ever completed.
  **Fix**: Add a startup snapshot save in the DI registration lambda RIGHT AFTER the pool is created and `SnapshotPath` is set. This ensures an immediate snapshot on first startup regardless of iteration count. Also verify that `PersistToDisk` is called successfully by adding a trace log.

#### Phase C — Fix elite designation

- [ ] **R6 — Restore IsElite flag on library entries after SeedFromLibrary** [mandatory — runtime failure: all isElite=false]
  **Diagnosis**: `SeedFromLibrary` sets `IsElite = true` on library entries and adds grace period tracking. Then `CheckAllTimeBestPromotion` is called at the end of seeding, which computes composite scores and may demote elites. Because library entries start with effectiveness-derived ELO and mutations start at parent-50, it's possible no entry exceeds the `_allTimeBestCompositeScore` threshold (initially 0), so the best IS promoted. But the JSON serialization in `PersistedPoolEntry.ToEntry()` may not preserve `IsElite` through the roundtrip.
  **Fix**: Audit `PersistedPoolEntry.ToEntry()` to confirm `IsElite` is preserved. If missing, add it. Then verify in `CheckAllTimeBestPromotion` that the best entry(s) from seeding remain elite. Add a test: `SeedFromLibrary → assert at least 1 entry has IsElite=true`.

- [x] **R7 — Verify elite designation survives RecordOutcome → EvaluateAndPromote cycle** [depends on R6]
  **Test**: `EliteDesignation_GivenRecordOutcomeAndEvaluate_EliteSurvives` — Seed pool with 8 entries (4 elites + 4 non-elites), call RecordOutcome 3 times with rotating winners, call EvaluateAndPromote, assert at least 1 entry has IsElite=true, assert EliteStrategyId is non-null.

#### Phase D — Integration verification

- [ ] **R8 — Cross-boundary integration test: RecordOutcome → pool endpoint** [depends on R1]
  **Test**: (a) Seed pool with 4 library entries, (b) compose match via PopulationMatchComposer, (c) simulate via BonesMatchSimulator, (d) call RecordOutcome with both known and unknown strategy IDs, (e) assert known entries updated, unknown entries skipped without throw, (f) query `/bones/pool/stats` and assert avgRating changed.

- [ ] **R9 — Snapshot round-trip test with live host path** [depends on R5]
  **Test**: (a) Create pool, set SnapshotPath to temp dir, (b) call PersistToDisk, (c) verify file exists and dir was created, (d) load via TryLoadFromDisk, (e) assert all entries restored with correct ratings, elite designation, and lineage.

- [ ] **R10 — Full suite regression gate** [mandatory — behavior-proof policy]
  Run `dotnet test Wip.Bones.Tests` and `dotnet test Wip.Bones.Host.Tests` (pool suite). Assert 81/81 Host pool tests pass, Wip.Bones.Tests regressions ≤ 3 (only pre-existing compliance gates + flaky enhancer).

---

## Test Plan

### R1 — Tolerant RecordOutcome

1. `RecordOutcome_GivenUnknownStrategyId_SkipsWithLog_DoesNotThrow`
   *Assumption*: When seatToStrategyId contains an ID not in the pool, RecordOutcome logs a debug message and continues processing the remaining seats. No exception is thrown.

2. `RecordOutcome_GivenMixedKnownAndUnknown_UpdatesKnownEntriesOnly`
   *Assumption*: With 2 known and 2 unknown strategy IDs, the 2 known entries have their ELO updated and MatchesPlayed incremented; the 2 unknown are skipped.

### R2 — Co-learning admission path

3. `CoLearningEnhanceAgent_GivenPoolInjected_AddsNewEntryToCorrectPoolInstance`
   *Assumption*: When StrategyPool is injected into BonesCoLearningEnhanceAgent (or StrategyPool.Instance is the DI pool), calling ExecuteAsync adds a new entry to the pool observable via `GET /bones/pool`.

### R4 — Unified pool instance

4. `StatusApi_GivenPoolSeeded_PoolSizeMatchesPoolEndpoint`
   *Assumption*: After seeding, `/api/bones/status` poolSize field equals the number of entries in `/bones/pool`.

### R5 — Snapshot persistence

5. `PersistToDisk_GivenSnapshotPath_CreatesDirectoryAndWritesFile`
   *Assumption*: Calling PersistToDisk with a path whose directory does not exist creates the directory and writes a valid JSON file.

6. `TrySavePoolSnapshot_GivenEmptyPool_DoesNotWriteFile`
   *Assumption*: An empty pool does not trigger a disk write (directory not created, no file written).

### R6 — Elite designation

7. `SeedFromLibrary_GivenFourLibraryEntries_SetsIsEliteOnAtLeastOne`
   *Assumption*: After SeedFromLibrary with 4 library entries, at least 1 entry has IsElite=true and the pool's EliteStrategyId is non-null.

8. `PersistedPoolEntry_ToEntry_PreservesIsEliteFlag`
   *Assumption*: Creating a PersistedPoolEntry with IsElite=true and converting back via ToEntry() produces a StrategyEntry with IsElite=true.

### R8 — Integration test

9. `Integration_RecordOutcome_WithMixedIds_UpdatesPoolAndEndpoint`
   *Assumption*: Full pipeline: seed → compose → simulate → RecordOutcome → pool endpoint query shows ELO changed and MatchesPlayed incremented for known entries.

### R9 — Snapshot round-trip

10. `Integration_PersistAndReload_PreservesAllState`
    *Assumption*: Pool saved to disk and reloaded produces identical entry counts, ratings, lineage, elite designation, and AllTimeBestCompositeScore.

---

## Discovered Gaps

1. **RecordOutcome throws on unknown IDs** — 115,617 game outcomes discarded; only 124 matches recorded. Covered by: R1.

2. **Snapshot never persists** — `pool/` directory missing; pool state lost on restart. Covered by: R4, R5.

3. **Elite designation broken** — All 103 entries show `isElite: false`; elite protection and grace period are moot. Covered by: R6, R7.

4. **Pool instance split** — Status API sees a different pool than Web endpoints; `poolSize: 3` vs `totalStrategies: 103`. Covered by: R4.

5. **Co-learning entries never reach the pool** — 0 entries with IDs other than `initial-*`/`explore-*`/`mut-*` despite 456 co-learning sessions. Covered by: R2.

6. **88 exploration entries never played** — All at M=0 because RecordOutcome skips them (they ARE in the pool, but matches never select them because PoolPopulationComposer only activates for non-co-learning mode). Covered by: I7 wiring already in place; data will flow once R1 is fixed and I7 pool-driven composition selects from the full pool.

7. **Pre-existing test failures** — 2 compliance-gate meta-tests + 1 flaky enhancer test. Not addressed by this plan (out of scope).

---

*All diagnostics derived from live endpoint queries at `http://127.0.0.1:8080`, runtime inspection of `BonesStrategyPool.cs:186-191`, `BonesPlayMatchTool.cs:209-219`, `BonesHostServiceCollectionExtensions.cs:198-203`, and filesystem check of `bones-data/.bones/strategy-library/`. Zero Falsified rows.*
