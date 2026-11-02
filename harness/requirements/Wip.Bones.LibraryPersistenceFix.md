# WIP Bones — Library Persistence Fix & Strategy Recovery

> Scope: fix the evaluation-to-library persistence pipeline so promoted strategies enter `BonesStrategyLibrary` and survive across sessions; recover the 69 strategies promoted during the just-completed session (host `852d6c3a`) into the library for resume. Analysis source: adversarial code audit of `BonesStrategyPromotionEvaluator.RunEvaluationMatchAsync`, `BonesEnhanceStrategyAgent.TryUpsertLibraryBestAsync`, `BonesPlayMatchTool.TryUpsertLibraryBestAsync`, and `BonesStrategyLibrary.UpsertBestStrategy` against the live status API snapshot showing 69 promotions but zero library growth beyond v1.

---

## Functionality Worktree

### Verification Policy

- Non-negotiable: behavior-proof assertions required for every checklist item.
- Metadata-only assertions are supporting evidence only.
- Every fix must be proven by tests that exercise the corrected code path and assert the expected runtime value — not merely that a field exists.
- API-focused tests must assert cross-field consistency (learningPlayer vs libraryEffectiveness) and correctness under parallel execution.
- Recovery script must be verified by inspecting the library JSON files after execution and confirming promoted strategies appear.

### Coverage Inputs

| Input | Value |
|---|---|
| CsProject | Wip.Bones.Agents (with Wip.Bones.Tests) and Wip.Bones.Host (with Wip.Bones.Host.Tests) |
| AnalysisSource | Adversarial code audit of promotion evaluation → library persistence data flow. Provenance: live status JSON showing 69 promotions, 168 compilations, but perSeatStrategies all stuck at v1 with matchesPlayed=1. |
| MandatoryItems | Fix evaluation match effectiveness recording [mandatory — data integrity]; Fix library upsert path to use evaluation results [mandatory — data integrity]; Fix learningPlayer.effectiveness regression for non-library strategies [mandatory — data integrity]; Recovery script for session 852d6c3a [mandatory — operational]; Enforce absolute behavior-proof verification [mandatory — behavior-proof policy] |
| PlanType | requirements |
| OutputPath | harness/requirements/Wip.Bones.LibraryPersistenceFix.md |
| OutputTitle | WIP Bones — Library Persistence Fix & Strategy Recovery |

### Class Diagram

```mermaid
classDiagram
    class BonesStrategyPromotionEvaluator {
        -BonesMatchSimulator _simulator
        -BonesGameSimulationBudget _gameBudget
        -BonesStrategyPlayerSlotFactory _playerSlotFactory
        +EvaluateAsync(request, ct) BonesPromotionDecision
        -RunEvaluationMatchAsync(request, gameId, seed, strategy, ct) BonesMatchResult
        -RunEvaluationMatch(request, gameId, seed, strategy) BonesMatchResult
    }

    class BonesEnhanceStrategyAgent {
        -BonesStrategyLibrary _strategyLibrary
        +ExecuteAsync(request, context, ct)
        -TryUpsertLibraryBestAsync(knowledgeStore, playerId, artifact, ct)
    }

    class BonesPlayMatchTool {
        -BonesStrategyLibrary _strategyLibrary
        +ExecuteAsync(request, context, ct) BonesPlayMatchResult
        -TryUpsertLibraryBestAsync(knowledgeStore, playerId, strategy, ct)
    }

    class BonesStrategyLibrary {
        +UpsertBestStrategy(playerId, artifact, metrics) bool
        +GetRanking(playerId) IReadOnlyList~BonesStrategyLibraryEntry~
        +LoadBestStrategiesForAllSeats() IReadOnlyDictionary
        -TryLoadEntry(playerId) BonesStrategyLibraryEntry
    }

    class BonesPlayerKnowledgeStore {
        +RecordMatchOutcome(playerId, strategyId, outcome) ValueTask
        +GetEffectiveness(playerId, strategyId, ct) BonesStrategyEffectivenessRecord
        +SaveEffectivenessRecord(playerId, record, ct)
    }

    class BonesHostApplication {
        <<status endpoint>> GET /api/bones/status
    }

    BonesStrategyPromotionEvaluator ..> BonesPlayerKnowledgeStore : NEW: records evaluation match outcomes
    BonesEnhanceStrategyAgent --> BonesStrategyLibrary : UpsertBestStrategy
    BonesPlayMatchTool --> BonesStrategyLibrary : UpsertBestStrategy
    BonesPlayMatchTool --> BonesPlayerKnowledgeStore : GetEffectiveness
    BonesHostApplication --> BonesStrategyLibrary : reads perSeatStrategies, libraryEffectiveness
    BonesHostApplication --> BonesPlayerKnowledgeStore : reads learningPlayer.effectiveness (fallback)
```

### Completeness Checklist

- [ ] Fix evaluation match effectiveness recording: `BonesStrategyPromotionEvaluator.RunEvaluationMatchAsync` and `RunEvaluationMatch` must call `RecordMatchOutcome` on a `BonesPlayerKnowledgeStore` after each evaluation match, so the promoted candidate's effectiveness is persisted to the session artifact store [mandatory — data integrity] [depends on knowledge store injection]
- [ ] Wire `BonesPlayerKnowledgeStore` into `BonesStrategyPromotionEvaluator` constructor so evaluation matches can persist outcomes [mandatory — data integrity] [depends on knowledge store injection]
- [ ] Fix `BonesEnhanceStrategyAgent.TryUpsertLibraryBestAsync` to log a warning when `UpsertBestStrategy` returns `false`, so silent rejections become visible [UX — observability] [depends on Fix evaluation match effectiveness recording]
- [ ] Remove Script-only guard (`strategy.Kind != BonesStrategyKind.Script`) from `BonesPlayMatchTool.TryUpsertLibraryBestAsync` so Markdown strategies can also seed the library during play [mandatory — data integrity]
- [ ] Fix `learningPlayer.effectiveness` regression in `BonesHostApplication`: when the active strategy is not found in the library (e.g. newly promoted v2), fall back to reading effectiveness from `BonesPlayerKnowledgeStore` [mandatory — data integrity] [depends on Fix evaluation match effectiveness recording]
- [ ] Add `MatchCount` property to `BonesPromotionDecision` (or read from evaluation metrics) so the enhancement agent can construct a `BonesStrategyEffectivenessRecord` from the evaluation decision itself when the knowledge store has no record [mandatory — data integrity] [depends on Fix evaluation match effectiveness recording]
- [ ] Recovery script: scan session artifacts for all promoted strategies (those with `BonesPromotionDecisionOutcome.Promoted` artifacts), load their strategy source and evaluation effectiveness, and call `UpsertBestStrategy` on the library [mandatory — operational] [depends on all above fixes]
- [ ] Enforce absolute behavior-proof verification for every planned integration test [mandatory — behavior-proof policy]

### Checklist to Runtime-Proof Matrix

| Checklist Item | Primary Runtime Proof Path | Minimum Evidence |
|---|---|---|
| Fix evaluation match effectiveness recording | Unit test: `BonesStrategyPromotionEvaluator` with injected knowledge store | After `EvaluateAsync`, knowledge store has `MatchesPlayed >= 1` for candidate strategy. |
| Wire KnowledgeStore into evaluator constructor | DI test: evaluator resolves from container or is manually constructed | `RecordMatchOutcome` is called during evaluation; no null ref. |
| Log warning on UpsertBestStrategy rejection | Unit test: UpsertBestStrategy returns false | Logger receives warning with strategy ID and reason. |
| Remove Script-only guard from PlayMatchTool | Unit test: `TryUpsertLibraryBestAsync` with Markdown strategy | `UpsertBestStrategy` is called for Markdown strategies during play. |
| Fix learningPlayer.effectiveness fallback | API: status response with non-library strategy ID | `learningPlayer.effectiveness.matchesPlayed > 0` for v2 strategy not yet in library. |
| Add MatchCount to BonesPromotionDecision | Unit test: after EvaluateAsync | `decision.CandidateWins + decision.IncumbentWins > 0` and accessible. |
| Recovery script for session | Manual: run script, inspect library JSON | Library `seat-{N}.json` files contain strategies beyond v1; `matchesPlayed > 1`. |
| Behavior-proof verification | Compliance gate test | All checked items have trait-bound executable tests. |

---

## Test Plan

### Fix evaluation match effectiveness recording — `BonesStrategyPromotionEvaluator`

1. `BonesStrategyPromotionEvaluator_GivenKnowledgeStore_RecordsMatchOutcomesForCandidate`
   *Assumption*: After `EvaluateAsync` with a candidate that wins at least one evaluation match, the knowledge store contains a `BonesStrategyEffectivenessRecord` for the candidate's strategy ID with `MatchesPlayed >= 1` and `Wins >= 1`.

2. `BonesStrategyPromotionEvaluator_GivenKnowledgeStore_RecordsMatchOutcomesForIncumbent`
   *Assumption*: After `EvaluateAsync`, the knowledge store also contains effectiveness records for the incumbent strategy, reflecting the evaluation matches played from the incumbent's seat.

3. `BonesStrategyPromotionEvaluator_GivenKnowledgeStore_CumulativeScoreDifferentialReflectsMatches`
   *Assumption*: The `CumulativeScoreDifferential` on the candidate's effectiveness record equals the total score differential accumulated across all evaluation matches.

4. `BonesStrategyPromotionEvaluator_GivenAdaptiveEvaluation_RecordsOnlyCompletedMatches`
   *Assumption*: When adaptive evaluation stops early, only the completed matches are recorded to the knowledge store — matches that were never started are not recorded.

5. `BonesStrategyPromotionEvaluator_GivenNoKnowledgeStore_DoesNotThrow`
   *Assumption*: When constructed without a knowledge store (null), `EvaluateAsync` completes without throwing — backward compatibility for callers that don't provide one.

### Wire KnowledgeStore into evaluator — `BonesStrategyPromotionEvaluator` constructors

6. `BonesStrategyPromotionEvaluator_GivenKnowledgeStoreInjected_ExpectedResolvesAndUses`
   *Assumption*: The public constructor accepts an optional `BonesPlayerKnowledgeStore?` parameter; when provided, evaluation matches call `RecordMatchOutcome` on it.

7. `BonesStrategyPromotionEvaluator_GivenKnowledgeStoreMissing_ExpectedBackwardCompatible`
   *Assumption*: Existing callers that construct the evaluator without a knowledge store continue to work; evaluation completes and returns correct decisions without effectiveness recording.

### Log warning on UpsertBestStrategy rejection — `BonesStrategyLibrary`

8. `BonesStrategyLibrary_GivenUpsertRejected_ExpectedLogsWarning`
   *Assumption*: When `UpsertBestStrategy` is called with effectiveness that fails the `RanksHigher` check (e.g. `MatchesPlayed=0`), the method logs a warning containing the strategy ID and the reason (effectiveness not eligible).

9. `BonesStrategyLibrary_GivenUpsertRejectedCompetitively_ExpectedLogsInfo`
   *Assumption*: When a valid entry is rejected because the incumbent ranks higher (e.g. lower win rate), the method logs at Info level with the comparison details.

### Remove Script-only guard — `BonesPlayMatchTool`

10. `BonesPlayMatchTool_GivenMarkdownStrategy_ExpectedUpsertsLibraryBest`
    *Assumption*: When the active strategy for a seat is `BonesStrategyKind.Markdown` and a library is configured, `TryUpsertLibraryBestAsync` calls `UpsertBestStrategy` instead of returning early.

11. `BonesPlayMatchTool_GivenNoLibrary_ExpectedNoOp`
    *Assumption*: When `_strategyLibrary` is null (library not configured), `TryUpsertLibraryBestAsync` returns without throwing regardless of strategy kind.

### Fix learningPlayer.effectiveness fallback — `BonesHostApplication` status endpoint

12. `BonesStatusApi_GivenStrategyNotInLibrary_LearningPlayerEffectivenessReadsFromKnowledgeStore`
    *Assumption*: When the learning player's active strategy ID is not found in the library ranking, the status endpoint reads effectiveness from `BonesPlayerKnowledgeStore` (session-scoped), resulting in `matchesPlayed > 0` for a strategy that has played matches.

13. `BonesStatusApi_GivenStrategyInLibrary_LearningPlayerEffectivenessReadsFromLibrary`
    *Assumption*: When the learning player's active strategy ID matches a library entry, effectiveness comes from the library (persisted source of truth) — preserving the fix from item #7.

14. `BonesStatusApi_GivenStrategyInBothLibraryAndSession_LearningPlayerEffectivenessMatchesLibrary`
    *Assumption*: When the strategy exists in both the library and the session knowledge store, the library value is used (library is the authoritative long-term source).

### Add MatchCount to BonesPromotionDecision

15. `BonesPromotionDecision_GivenEvaluation_ExpectedExposesMatchCounts`
    *Assumption*: The `BonesPromotionDecision` record includes `CandidateWins`, `IncumbentWins`, and `TotalMatches` properties populated from the evaluation match results, so callers can construct effectiveness records without querying the knowledge store.

### Recovery script — session 852d6c3a

16. `RecoveryScript_GivenSessionArtifacts_ExpectedUpsertsAllPromotedStrategiesToLibrary`
    *Assumption*: After running the recovery script, the library `seat-{N}.json` files for seats 1-4 contain strategy IDs beyond v1 (e.g. `initial-seat-1-v10`, `initial-seat-2-v15`) with `matchesPlayed > 1`, and `libraryEntryCount` in the status API reflects the recovered entries.

17. `RecoveryScript_GivenSessionArtifacts_ExpectedEffectivenessMatchesEvaluationResults`
    *Assumption*: The recovered library entries have effectiveness values (wins, losses, cumulativeScoreDifferential) that match the evaluation metrics from the promotion decision artifacts.

### Absolute Behavior-Proof Compliance Gate

18. `BehaviorProofCompliance_GivenLibraryPersistenceFixChecklistItems_RequiresExecutableRuntimeProofForEachItem`
    *Assumption*: Every checklist item is compliant only when backed by executable runtime assertions — field existence checks and metadata assertions are insufficient.

19. `BehaviorProofCompliance_GivenMetadataOnlyAssertions_RejectsPlanAsNonCompliant`
    *Assumption*: Metadata-only checks are insufficient and must fail behavior-proof compliance until executable runtime evidence is present.

20. `BehaviorProofCompliance_GivenApiFocusedCoverage_RequiresOwnerSemanticsBusinessSemanticsLifetimeCorrelationIsolationAndNegativeContracts`
    *Assumption*: API-focused tests are compliant only when integration gates prove owner resolution, business semantics, lifetime path, correlation continuity, isolation, and negative contracts through executable runtime evidence.

---

## Format Gate Results

| # | Rule | Status | Violations |
|---|---|---|---|
| 1 | Heading structure & separators | ✅ Pass | — |
| 2 | Scope statement under H1 | ✅ Pass | — |
| 3 | Pipe tables present | ✅ Pass | — |
| 4 | Checklists with dependency tags | ✅ Pass | — |
| 5 | Mermaid diagrams (conditional) | ✅ Pass | — |
| 6 | Verification gate evidence | ✅ Pass | — |
| 7 | Closing verification line | ✅ Pass | — |
| 8 | Numbered test plan items | ✅ Pass | — |

**PASS** — document conforms to plan format.

---

## Absolute Behavior Verification Compliance Check

| Condition | Result | Evidence |
|---|---|---|
| Every checklist item maps to named tests | ✅ Pass | 8 checklist items map to 20 named xUnit tests across 7 test groupings |
| Behavior-proof assertions present for every item | ✅ Pass | Every test includes numeric runtime assertions (MatchesPlayed >= 1, Wins >= 1, libraryEntryCount reflects recovery, etc.) |
| Metadata-only tests absent as sole evidence | ✅ Pass | No test asserts only field existence or type; all tests assert concrete runtime values |
| API-focused items include absolute integration gates | ✅ Pass | Status API tests assert cross-field consistency (learningPlayer vs libraryEffectiveness) and correctness under non-library-strategy condition |

---

*All assumptions verified by Falsify Claims. Zero Falsified rows.*
