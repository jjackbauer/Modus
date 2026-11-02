# Bones Strategy Pool — Runtime Adversarial Audit

**Date**: 2026-07-08  
**Session**: `f316515e6fae4ac3bf02aa4638e5989b`  
**Pool snapshot**: `pool-snapshot.json` — **does not exist on disk**  
**Pool directory**: `bones-data/.bones/strategy-library/pool/` — **does not exist**

---

## Executive Summary

The `StrategyPool` and `TournamentSelector` are well-designed in isolation but are **architecturally disconnected** from the runtime learning loop. The pool operates as an in-memory, append-only diagnostic mirror — it receives new entries as a side-effect of co-learning enhancement but **never feeds back into match selection, never receives outcome updates, never prunes itself, and never persists**.

---

## 1. Live System Snapshot

| Metric | Value | Source |
|---|---|---|
| Iterations | 33 batches, **537 co-learning sessions** | `/api/bones/status` |
| Games simulated | **135,926** | `/api/bones/status` |
| Parallel sessions | 16 | `/api/bones/status` |
| Pool size | 111 strategies | `/bones/pool/stats` |
| Library entries | **4** (one per seat) | `/api/bones/status` |
| Elite strategies | **1** (`initial-seat-4-v6-r5`) | `/bones/pool` |
| Avg pool ELO | 1528.5 (range: 1457–1604) | `/bones/pool/stats` |
| Avg pool win rate | 57.4% | `/bones/pool/stats` |
| Cum. promoted vs rejected | 952 promoted / 1,135 rejected (45.6%) | `/api/bones/status` |
| Model provider | `deepseek-chat` | `/api/bones/status` |

---

## 2. Architecture Diagram: Two Separate Paths

```
┌─────────────────────────────────────────────────────────────────────┐
│                     ACTUAL RUNTIME LOOP (working)                    │
│                                                                      │
│  Observe ──→ Ponder ──→ Play ──→ Enhance                            │
│                        │           │                                 │
│                        │           ├──→ BonesStrategyLibrary.Upsert  │ ← source of truth
│                        │           └──→ StrategyPool.Add (side effect)
│                        │                                              │
│                        └── BonesPlayMatchTool                        │
│                             loads opponents from                     │
│                             BonesPlayerKnowledgeStore (artifacts)    │
│                             NOT the pool                             │
└─────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────┐
│                   STRATEGY POOL (append-only mirror)                 │
│                                                                      │
│  SeedFromLibrary() [startup only]                                    │
│       │                                                              │
│       └──→ 4 library entries + 96 replicas = 100 entries             │
│                                                                      │
│  .Add()  [co-learning side effect]                                   │
│       │                                                              │
│       └──→ ~11 additional entries from enhancement                   │
│                                                                      │
│  RecordOutcome()        ← NEVER CALLED (dead code)                   │
│  EvaluateAndPromote()   ← NEVER CALLED (dead code)                   │
│  Prune()                ← NEVER CALLED (dead code)                   │
│  PersistToDisk()        ← NEVER CALLED (no snapshot file on disk)    │
│  PopulationMatchComposer ← NEVER INSTANTIATED in production          │
│                                                                      │
│  Read paths: /bones/pool/* endpoints (viewer), /api/bones/status     │
└─────────────────────────────────────────────────────────────────────┘
```

---

## 3. Adversarial Analysis of Findings

Each claim below was challenged and cross-verified.

### 3.1 Claim: "RecordOutcome is dead code"

**Challenge**: Could it be called through an event handler, DI interceptor, middleware, or background timer?

**Verification**:
- Grepped all `.cs` files under `WIP/` for `RecordOutcome(`. Found 0 callers outside `BonesStrategyPool.cs` itself and test files.
- Grepped all `.cs` files under `WIP/Wip.Bones.Agents/Play/` for pool references. **Zero matches**.
- The learning loop (`BonesLearningLoopHost.cs`) does not inject `StrategyPool` and makes no calls to it.
- The pool is a concrete `sealed class`, not behind an interface — no DI decoration possible.

**Verdict**: Definitively dead. ✅

### 3.2 Claim: "EvaluateAndPromote is dead code"

**Challenge**: Could it be called by a workflow stage or scheduled job?

**Verification**:
- Grepped all `.cs` files under `WIP/` for `EvaluateAndPromote(`. Found 0 callers outside tests.
- The pool snapshot directory `bones-data/.bones/strategy-library/pool/` **does not exist on disk**. `PersistToDisk`, called only by `EvaluateAndPromote`, creates this directory — its absence is definitive proof.
- The code comment "H4: Auto-persist after EvaluateAndPromote" at `BonesHostServiceCollectionExtensions.cs:164` sets the `SnapshotPath` property but the triggering method is never called.

**Verdict**: Definitively dead. ✅

### 3.3 Claim: "PopulationMatchComposer never used"

**Challenge**: Could it be registered in a different DI extension or builder?

**Verification**:
- `BonesHostServiceCollectionExtensions.cs` registers all services. `PopulationMatchComposer` is absent.
- `BonesLearningLoopHost.cs` orchestrates match play via `RunWorkflowAsync` which dispatches to the workflow pipeline (Observe → Ponder → Play → Enhance), not to the population composer.
- The `BonesPlayMatchTool` handles match execution; it loads opponent strategies from `BonesPlayerKnowledgeStore` (artifact files), not from the pool.

**Verdict**: Never instantiated in production. ✅

### 3.4 Claim: "~11 appended from co-learning"

**Challenge**: This is an inference from 111 − 100 = 11. The pool JSON was truncated at 15KB. Could the actual count differ?

**Verification**:
- Default `PopulationPoolOptions.PoolSize = 100`. The seed code uses `targetPoolSize: opts.Population.PoolSize`.
- Pool stats show exactly 111 entries. All visible entries in the truncated JSON show `iterationCount: 0` and identical per-seat stats, consistent with seeded replicas.
- The 11 extra entries beyond 100 are almost certainly from `BonesCoLearningEnhanceAgent.cs:68` which calls `pool.Add(new StrategyEntry(...))` with fresh stats (0-0-0, 1500 ELO).

**Verdict**: Inference is sound. Minor uncertainty on exact entry identities due to truncation. ⚠️

### 3.5 Claim: "Pool has zero influence on match selection"

**Challenge**: Is there any indirect path where the pool could affect gameplay?

**Full trace of the Play path**:
1. `BonesLearningLoopHost.RunSingleSessionAsync` → `_orchestrator.RunWorkflowAsync`
2. Workflow dispatches to `BonesPlayMatchTool` (registered as a tool)
3. `BonesPlayMatchTool` receives a `BonesPlayMatchRequest` mapped from `BonesPonderResult`
4. The opponent strategies come from `BonesPlayerKnowledgeStore.LoadActiveStrategy()` — reads artifact files per session
5. Match runs via `BonesMatchSimulator`
6. **No contact with StrategyPool at any point**

**Verdict**: No influence. The pool and the gameplay loop are entirely separate. ✅

### 3.6 Claim: "ELO ratings are frozen"

**Challenge**: Could ELO be updated through some other mechanism?

**Verification**:
- ELO updates happen only in `RecordOutcome()` (line 153–176 of `BonesStrategyPool.cs`)
- `RecordOutcome` is the sole mutator of `entry.Rating` post-seeding
- Since `RecordOutcome` is dead, ratings are frozen at seed-time values (ELO based on Bayesian win rate from the library effectiveness record, plus ±5% perturbation for replicas)

**Verdict**: Ratings are frozen. ✅

---

## 4. Root Cause Analysis

### 4.1 Two Competing Architecture Patterns

The codebase has **two different designs** for strategy management that never converged:

| Aspect | Knowledge Store Path (active) | Population Pool Path (dormant) |
|---|---|---|
| Strategy source | Artifact files per session | In-memory dictionary |
| Selection | LLM-generated + evaluated vs incumbent | Tournament composite score |
| Outcome tracking | `BonesStrategyEffectivenessRecord` per artifact | `RecordOutcome` on pool entries |
| Promotion | `BonesStrategyPromotionEvaluator` (head-to-head) | `EvaluateAndPromote` (population culling) |
| Persistence | `BonesStrategyLibrary` (`.bones/strategy-library/`) | Pool snapshot (`.bones/strategy-library/pool/`) |
| Status | **Active, driving all 135,926 games** | **Dead, mirroring co-learning output** |

### 4.2 How This Happened

1. The `StrategyPool` + `TournamentSelector` + `PopulationMatchComposer` were designed as a **population-based tournament system** — a classic evolutionary algorithm pattern where the pool *is* the population, matches are composed from the pool, outcomes feed back into the pool, and periodic evaluation/pruning keeps the population healthy.

2. The actual learning loop evolved along a different path: a **pipeline workflow** (Observe → Ponder → Play → Enhance) using session-scoped artifact storage and the strategy library for cross-session persistence.

3. The two paths were **bridged only at the append point**: `BonesCoLearningEnhanceAgent` writes new strategies to the pool as a side-effect. No feedback loop was ever wired in the other direction.

### 4.3 Why This Matters

- **135,926 games of data are lost to the pool**: Every match outcome that could update ELO ratings, win/loss records, and composite scores is discarded.
- **Pool cannot self-regulate**: Without `Prune()`, the pool grows unboundedly (100 → 111 and climbing).
- **Tournament selection is inert**: The well-implemented composite scoring (0.4 × win rate + 0.3 × sigmoid(score diff) + 0.3 × normalized ELO) is never used to select match participants.
- **Elite protection is window dressing**: Elite status, set at seed time, never changes. The one elite strategy (`initial-seat-4-v6-r5`) won't be challenged by new candidates through pool mechanisms.

---

## 5. What The Pool *Does* Do Correctly

Despite the disconnect, the pool provides working **observability**:

| Function | Status |
|---|---|
| `/bones/pool` — JSON snapshot of all entries | ✅ Working |
| `/bones/pool/stats` — aggregate statistics | ✅ Working |
| `/bones/pool/{id}` — individual strategy detail | ✅ Working |
| `/bones/pool/{id}/lineage` — parent-child tracking | ✅ Working |
| `/bones/pool/view` — HTML page renderer | ✅ Working |
| `/api/bones/status` — pool size, min/max ELO, elite | ✅ Working |
| `StrategyPool.Instance` singleton | ✅ Working, thread-safe via `Dictionary` |
| `SeedFromLibrary` | ✅ Seeds correctly at startup |
| `Add` with duplicate rejection | ✅ Working |

---

## 6. Recommendations

### 6.1 Decide: Connect or Remove

**Option A — Wire the pool into the runtime loop** (if population-based tournament selection is the intended design):
- Call `RecordOutcome()` after every match in `BonesPlayMatchTool`
- Call `EvaluateAndPromote()` periodically (e.g., every `EvaluationCycleGameCount` games)
- Use `PopulationMatchComposer.ComposeMatch()` to select 4 opponents from the pool for each learning match
- Remove the artifact-based opponent loading in favor of pool-driven selection

**Option B — Remove the pool** (if the workflow pipeline is the intended design):
- Delete `StrategyPool`, `TournamentSelector`, `PopulationMatchComposer`, `BonesStrategyGraveyard`, `PoolLineageRecord`, `PoolPersistenceTypes`, `StrategyPoolJsonService`, `StrategyPoolPageRenderer`, and related tests
- The co-learning append in `BonesCoLearningEnhanceAgent` becomes dead code removal
- Status endpoint reads from `BonesStrategyLibrary` instead

### 6.2 Minimum Viable Fix (if keeping the pool)

1. **Wire `RecordOutcome`** into `BonesPlayMatchTool` after match completion — pass match result + seat→strategy mapping to the pool.
2. **Wire periodic `EvaluateAndPromote`** — add a counter in `BonesLearningLoopHost` and trigger after `EvaluationCycleGameCount` games (default: pool size × 8 = 800 games).
3. **Wire `Prune`** — call after `EvaluateAndPromote` if pool size exceeds `MaxPopulation` (default: 200).

### 6.3 Immediate Observability Fix

Even without connecting the pool to gameplay, add a periodic **snapshot persistence** call (e.g., every 100 co-learning iterations) so the pool state survives restarts. Currently, if the process restarts, all 111 in-memory entries including lineage records are lost — only the 4 library entries survive via `SeedFromLibrary`.

---

## 7. Evidence Inventory

| Evidence | Status |
|---|---|
| `bones-data/.bones/strategy-library/pool/` directory | ❌ Does not exist |
| `pool-snapshot.json` on disk | ❌ Does not exist |
| `RecordOutcome` callers outside tests | ❌ None found |
| `EvaluateAndPromote` callers outside tests | ❌ None found |
| `PopulationMatchComposer` in production DI | ❌ Not registered |
| Pool references in `BonesPlayMatchTool` | ❌ None |
| Pool references in `BonesLearningLoopHost` | ❌ None (only status endpoint reads) |
| Pool endpoint `/bones/pool` responding | ✅ Returns 111 entries |
| Pool endpoint `/bones/pool/stats` responding | ✅ Returns aggregate stats |
| Co-learning append to pool | ✅ `BonesCoLearningEnhanceAgent.cs:68` |
| Seed from library | ✅ `BonesHostServiceCollectionExtensions.cs:172` |

---

## 8. Test Coverage Gap

| Component | Test file | Production usage |
|---|---|---|
| `StrategyPool` | `BonesStrategyPoolTests.cs` (43 tests) | View/append only |
| `TournamentSelector` | Inside pool tests | Dead code |
| `PopulationMatchComposer` | `PopulationMatchComposerTests.cs` (3 tests) | Not instantiated |
| `BonesStrategyGraveyard` | Inside pool tests | Dead code |
| Pool persistence | `BonesPoolPersistenceTests.cs` (17 tests) | Persistence never triggers |

The tests validate correct behavior but **do not detect the architectural disconnect** — they test the pool in isolation, not whether it's wired into the runtime.

---

*This audit was performed against a live running host at `http://127.0.0.1:8080/` without interrupting the session. All claims were verified through code inspection (grep, read), live endpoint queries, and filesystem checks.*
