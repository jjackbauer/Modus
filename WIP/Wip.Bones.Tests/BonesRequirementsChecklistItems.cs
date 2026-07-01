namespace Wip.Bones.Tests;

internal static class BonesRequirementsChecklistItems
{
    public const string DomainTypes =
        "Introduce `Wip.Bones` domain assembly with typed identifiers (`BonesPlayerId`, `BonesTileId`, `BonesMoveId`, `BonesGameId`, `BonesStrategyId`), immutable value objects for tiles, hands, board ends, and pip counts, and `BonesTableConfig` fixing player count at four seats [foundation for game simulation]";

    public const string GameEngine =
        "Implement `BonesGameEngine` for four-player Block Dominoes (double-six): shuffle/deal seven tiles to each of four seats, opening lead resolution across all hands, clockwise turn rotation, legal-move enumeration, pass handling, round completion, and pip-based scoring [depends on domain types] [mandatory - game loop simulation]";

    public const string LegalMovesApi =
        "Expose `GetLegalMoves` and `ApplyMove` with deterministic rejection of illegal plays (wrong pip, not in hand, out-of-turn) and append-only `BonesEvent` audit log entries [depends on game engine]";

    public const string MatchSimulator =
        "Add `BonesMatchSimulator` to run full multi-round matches across four registered player slots with configurable target score and seed for reproducibility [depends on game engine]";

    public const string KnowledgeStore =
        "Introduce `Wip.Bones.Agents` with `BonesPlayerKnowledgeStore` persisting per-`BonesPlayerId` strategy markdown, observation transcripts, and match history under session-scoped artifact paths with no cross-player reads [depends on Wip.Artifacts.Local] [mandatory - agent isolation]";

    public const string ObserveGamesTool =
        "Implement `BonesObserveGamesTool` that runs seeded observation matches (stub or random legal players), persists redacted transcripts per observing agent, and returns summary counts [depends on match simulator and knowledge store]";

    public const string PonderAgent =
        "Implement `BonesPonderAgent` that loads only the requesting agent's observation artifacts, prompts via `IModelProvider` (or deterministic stub provider for tests) to author initial strategy rules, and persists `bones-strategy` artifact [depends on knowledge store and observe tool output] [mandatory - strategy authoring]";

    public const string PlayMatchTool =
        "Implement `BonesPlayMatchTool` that loads all four agents' strategy artifacts, prompts the active seat to choose among `GetLegalMoves` options each turn, applies moves through `BonesGameEngine`, and persists full four-player match transcript [depends on ponder agent strategies and game engine] [mandatory - agent play loop]";

    public const string EnhanceStrategyAgent =
        "Implement `BonesEnhanceStrategyAgent` that reads the agent's own match history and current strategy, prompts for tactic refinements, versions strategy with monotonic `BonesStrategyId`, and persists enhanced strategy artifact without mutating other agents' stores [depends on play match tool and knowledge store] [mandatory - strategy enhancement]";

    public const string WorkflowRegistration =
        "Register `workflow.bones.learning` typed linear workflow: Observe → Ponder → Play → Enhance with compiled map adapters between stage contracts via `WipBuilder` and `TypedLinearWorkflowBuilder` [depends on all stage capabilities]";

    public const string BuilderExtension =
        "Wire builder extension `AddBonesLearningWorkflow` registering agents, tools, and workflow id `workflow.bones.learning` for shell host and test harness consumption [depends on workflow registration]";

    public const string IsolationNegativeGate =
        "Add negative isolation gate: `BonesPlayerKnowledgeStore` must throw or return empty when an agent requests another player's strategy or private observation path [depends on knowledge store] [mandatory - isolation enforcement]";

    public const string StrategyKindEnums =
        "Introduce `BonesStrategyKind` (`Script`, `Markdown`) and `BonesPromotionStatus` (`Active`, `Candidate`, `Rejected`, `Superseded`) typed enums in `Wip.Bones.Identifiers` or `Wip.Bones.Agents.Knowledge` [foundation for strategy artifact model]";

    public const string StrategyArtifact =
        "Replace or extend `BonesStrategyDocument` with `BonesStrategyArtifact` carrying `StrategyId`, `PlayerId`, `Kind`, `Source` (C# body or markdown), and `PromotionStatus` [depends on strategy kind enums] [mandatory - typed strategy artifact]";

    public const string ScriptApiReference =
        "Publish `BonesStrategyScriptApiReference` documenting the deterministic script contract: implement `IBonesPlayerSlot`, receive `BonesRoundState` and `IReadOnlyList<BonesMove>` from `GetLegalMoves`, return exactly one move from the legal set or throw; list allowed namespaces/types (`Wip.Bones.Domain`, `Wip.Bones.Identifiers`, `Wip.Bones.Engine`) [depends on artifact model] [mandatory - script API contract]";

    public const string ScriptHost =
        "Implement `BonesStrategyScriptHost` compiling C# source via Roslyn (`Microsoft.CodeAnalysis.CSharp`) into an `IBonesPlayerSlot` instance with restricted references, compilation timeout, and per-turn execution timeout; reject scripts referencing disallowed assemblies or performing IO/network [depends on API reference] [mandatory - sandboxed script executor]";

    public const string CompiledStrategyCache =
        "Implement `BonesCompiledStrategy` cache keyed by `BonesStrategyId` and source hash so repeated turns within a match do not recompile [depends on script host]";

    public const string ScriptStrategyPlayerSlot =
        "Implement `BonesScriptStrategyPlayerSlot` wrapping `BonesStrategyScriptHost` and validating script output is contained in `legalMoves`; on illegal selection fall back to first legal move and emit execution warning artifact (mirroring current LLM retry semantics) [depends on script host] [mandatory - legal-move enforcement]";

    public const string ScriptResponseParser =
        "Add `BonesStrategyScriptResponseParser` extracting fenced `csharp` blocks from DeepSeek ponder/enhance responses and validating presence of `IBonesPlayerSlot` implementation before persistence [depends on artifact model]";

    public const string PonderScriptAuthoringPrompt =
        "Update `BonesPonderPromptBuilder` system and user prompts to instruct DeepSeek to output a single compilable C# class implementing `IBonesPlayerSlot`, including the API reference excerpt, observation summaries, and example `ChooseMove` skeleton — not markdown rules [depends on API reference] [mandatory - script authoring prompt]";

    public const string EnhanceScriptRefinementPrompt =
        "Update `BonesEnhancePromptBuilder` to request C# script refinement from prior script source and match outcome summaries; preserve incumbent logic where outcomes were wins, revise where losses or negative score differential [depends on ponder prompt contract] [mandatory - script enhancement prompt]";

    public const string DeepSeekScriptAuthoringHttpMapping =
        "Update `DeepSeekBonesStrategyAuthoringProvider` and `DeepSeekBonesStrategyEnhancementProvider` HTTP mapping tests to assert outbound user content includes script API contract headers and `IBonesPlayerSlot` requirement [depends on prompt builder changes]";

    public const string PonderScriptPersistence =
        "Change `BonesPonderAgent` to parse script from model response, compile via `BonesStrategyScriptHost` as validation gate, and persist `Kind=Script` artifact with `PromotionStatus=Active` for initial v1 strategies [depends on script host and parser] [mandatory - ponder produces scripts]";

    public const string StrategyEffectivenessRecord =
        "Introduce `BonesStrategyEffectivenessRecord` persisted per `BonesStrategyId` and `BonesPlayerId` tracking `MatchesPlayed`, `Wins`, `Losses`, and `CumulativeScoreDifferential` [depends on artifact model] [mandatory - effectiveness metrics]";

    public const string PlayMatchScriptDispatch =
        "Extend `BonesPlayMatchTool` to load **active** strategies, bind each seat to `BonesScriptStrategyPlayerSlot` when `Kind=Script`, and skip `IModelProvider<BonesPlayTurnRequest, …>` dispatch for script seats; retain markdown+LLM path only for legacy `Kind=Markdown` artifacts [depends on script player slot] [mandatory - script play dispatch]";

    public const string RecordMatchOutcome =
        "After each play match, call `BonesPlayerKnowledgeStore.RecordMatchOutcome` for every seat using the active `BonesStrategyId` played, updating effectiveness records from `BonesPlayMatchExecutionLog` winner and final scores [depends on effectiveness record] [mandatory - outcome recording]";

    public const string KnowledgeStoreActivePointer =
        "Add `BonesPlayerKnowledgeStore` APIs: `LoadActiveStrategy`, `SaveCandidateStrategy`, `PromoteStrategy`, `GetEffectiveness`, and `ListStrategyVersions`; persist active pointer as session-scoped JSON artifact `bones-active-strategy-seat-{N}` [depends on effectiveness record] [mandatory - active strategy pointer]";

    public const string LoadStrategyActiveAlias =
        "Stop using `OrderByDescending(ProducedAtUtc)` alone as the play-time strategy selector; `LoadStrategy` becomes alias for `LoadActiveStrategy` or throws when no active pointer exists [depends on active pointer APIs]";

    public const string StrategyPromotionOptions =
        "Implement `BonesStrategyPromotionOptions` with configurable `EvaluationMatchCount`, `MinimumWinRateImprovement`, and `MinimumScoreDifferentialImprovement` [depends on effectiveness metrics]";

    public const string StrategyPromotionEvaluator =
        "Implement `BonesStrategyPromotionEvaluator` running `EvaluationMatchCount` seeded head-to-head evaluation matches with candidate script vs incumbent script for the learning player (opponents use their active scripts); compute win rate and average score differential delta [depends on promotion options and script play path] [mandatory - promotion evaluator]";

    public const string EnhancePromotionGate =
        "Update `BonesEnhanceStrategyAgent` to save enhanced script as `PromotionStatus=Candidate`, invoke `BonesStrategyPromotionEvaluator`, call `PromoteStrategy` only when evaluation passes, otherwise mark `Rejected` and leave active pointer unchanged [depends on promotion evaluator and knowledge store APIs] [mandatory - effectiveness-gated promotion]";

    public const string T2_4_OpponentAwareEnhancement =
        "Add opponent-aware enhancement context — feed opponent strategy summaries into the enhancement prompt so the LLM can counter-adapt [Tier 2 — opponent modeling]";

    public const string PromotionDecisionArtifact =
        "Persist promotion decision artifact (`bones-strategy-promotion-{guid}`) recording incumbent id, candidate id, evaluation metrics, and `Promoted` or `Rejected` outcome for operator visibility [depends on enhance agent changes]";

    public const string GameSimulationBudget =
        "Implement `BonesGameSimulationBudget` tracking cumulative simulated games across observe, play, and promotion-evaluation stages; expose `GamesSimulated`, `MaxGamesPerRun`, and `TryReserve(gameCount)` returning false when the reservation would exceed the cap [depends on MaxGamesPerRun option] [mandatory - game budget tracker]";

    public const string GameSimulationBudgetInstrumentation =
        "Instrument `BonesObserveGamesTool`, `BonesPlayMatchTool`, and `BonesStrategyPromotionEvaluator` to report actual games simulated back to `BonesGameSimulationBudget` via `CapabilityContext` or injected budget service [depends on game budget tracker]";

    public const string AdaptiveEvaluationBudget =
        "Implement adaptive evaluation budget using sequential testing — stop early when candidate is clearly better or worse, reserve full 20 matches only for borderline cases [Tier 2 — efficiency]";

    public const string StrategyLibrary =
        "Implement repository-scoped `BonesStrategyLibrary` persisting under `{DataDirectory}/.bones/strategy-library/` one `BonesStrategyLibraryEntry` per seat with strategy artifact source, `BonesStrategyId`, effectiveness metrics, and `LastUpdatedUtc` — survives session and process restarts [depends on effectiveness record and artifact model] [mandatory - persistent strategy library]";

    public const string StrategyLibraryRanking =
        "Define library ranking: primary sort by win rate (`Wins / MatchesPlayed`) with minimum `MatchesPlayed >= 1`; tie-break by `CumulativeScoreDifferential` descending; then by `LastUpdatedUtc` [depends on strategy library]";

    public const string StrategyLibraryUpsertBestStrategy =
        "Implement `BonesStrategyLibrary.UpsertBestStrategy` after successful promotion or when an active strategy's effectiveness exceeds the stored entry for that seat [depends on library ranking and promotion gate]";

    public const string StrategyLibraryLoadBestForAllSeats =
        "Add `BonesStrategyLibrary.LoadBestStrategiesForAllSeats()` returning `IReadOnlyDictionary<BonesPlayerId, BonesStrategyArtifact>` — fails gracefully with a `BonesLibrarySeatIntegrity` report when entries are missing [depends on strategy library]";

    public const string StrategyBootstrapper =
        "Replace `BonesLearningLoopHost.SeedInitialStrategiesAsync` hardcoded markdown v1 seeds with `BonesStrategyBootstrapper` that, when `ResumeFromBest=true`, copies each seat's library best into the new session as `PromotionStatus=Active` and sets active pointers; when no library entry exists for a seat, retain current default-seed behavior for opponents and run ponder for the learning player [depends on strategy library and active pointer APIs] [mandatory - restart from best bootstrap]";

    public const string PonderBootstrapSkip =
        "Skip `BonesPonderAgent` dispatch for seats that received a library bootstrap in the current session (strategy already active); workflow map or host pre-stage gate records `BootstrapSource=Library` on execution artifact [depends on bootstrapper]";

    public const string CoLearningDistinctStrategiesProof =
        "Add test proving that after 3 co-learning iterations with stub providers, all 4 seats have distinct `Kind=Script` artifacts with game-aware logic (not `legalMoves[0]`) and non-zero effectiveness records [mandatory - co-learning proof]";

    public const string ComplianceRegistry =
        "Register `harness/requirements/Wip.Bones.AdversarialCoLearning.md` in `BehaviorProofComplianceRegistry` with owning assemblies `Wip.Bones.Tests` and `Wip.Bones.Host.Tests` [depends on test coverage]";

    public const string StrategyVersioningCollisionAvoidance =
        "`BonesStrategyVersioning.NextVersion` must avoid name collisions when the fallback path produces `initial-seat-N-v2` — check whether `initial-seat-N-v1` already exists in the active knowledge store and increment past the highest existing version. [depends on strategy naming convention]";

    public const string T3_1_SkillDecomposition =
        "Implement skill decomposition — replace monolithic `IBonesPlayerSlot.ChooseMove()` with composable heuristics (TileEvaluation, Blocking, Endgame, OpponentModel) [Tier 3 — architectural]";

    public const string T3_2_MetaPromptOptimization =
        "Implement online meta-prompt optimization — allow the enhance LLM to propose improvements to the enhancement system prompt itself [Tier 3 — meta-learning]";

    public const string T3_3_LongContextMemory =
        "Implement long-context memory architecture — dedicated memory store accumulating strategy lineages, match statistics, discovered patterns, and failed approaches across sessions [Tier 3 — continual learning]";

    public const string T3_4_SubAgentDelegation =
        "Implement sub-agent delegation — allow strategies to delegate uncertain decisions to deep-think sub-agents with more context or different model [Tier 3 — delegation]";

    public const string T3_5_CurriculumLearning =
        "Implement curriculum learning — start with simpler game variants for initial strategy bootstrapping before full 4-player game [Tier 3 — curriculum]";

    public const string M1_SoftRetention =
        "Persist rejected candidate strategies with full evaluation context for future reconsideration (soft retention, not permanent discard) [depends on T1.1]";

    public const string BehaviorProofPolicy =
        "Enforce absolute behavior-proof verification for every planned integration test [mandatory - behavior-proof policy]";
}