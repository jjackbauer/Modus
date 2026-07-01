namespace Wip.Bones.Host.Tests;

internal static class BonesHostRequirementsChecklistItems
{
    public const string HostProjectFoundation =
        "Introduce `Wip.Bones.Host` executable project (`Microsoft.NET.Sdk.Web`) as sole Bones composition root — references `Wip.Bones`, `Wip.Bones.Agents`, `Wip.Bones.Web`, `Wip.Runtime`, `Wip.Builder`, `Wip.Artifacts.Local`; **must not** reference `Wip.ShellHost` or `Wip.Shell` [foundation for dedicated hosting] [mandatory - no WIP shell dependency]";

    public const string HostOptions =
        "Implement `BonesHostOptions` with listen URL, data directory, default workflow parameters (`gameCount`, `seed`, `targetScore`, `learningPlayerId`), and `IterationDelayMs` between consecutive learning iterations [depends on host project]";

    public const string HostStartupDi =
        "Wire host startup DI: game engine, match simulator, artifact store, all four learning-stage capabilities, `workflow.bones.learning`, and in-process `IBonesPublicMatchFeed` -> `BonesCatalogPublicMatchFeed` sharing singleton `IBonesMatchCatalog` with viewer [depends on host options] [mandatory - single-process viewer feed]";

    public const string ContinuousLoopHost =
        "Implement `BonesLearningLoopHost` as `IHostedService`/`BackgroundService` that **starts automatically on host boot**, runs Observe -> Ponder -> Play -> Enhance via `WipRuntimeOrchestrator.RunWorkflowAsync`, increments iteration counter, and schedules the next iteration without external commands [depends on host DI] [mandatory - continuous autonomous loop]";

    public const string DerivedSeed =
        "Ensure each iteration uses deterministic seed derivation (`HashCode.Combine(baseSeed, iterationNumber)`) so successive games are reproducible yet distinct [depends on loop host]";

    public const string WebServer =
        "Map `BonesWebHost` viewer routes and static SPA (`/bones/...`, `/viewer/...`, `/health`) on the same Kestrel instance [depends on web host wiring] [mandatory - web server]";

    public const string StatusControlApi =
        "Expose read/control HTTP API: `GET /api/bones/status` (loop state, iteration count, current stage, viewer URL), `POST /api/bones/stop` (request graceful stop after current iteration), optional `POST /api/bones/start` only when loop was stopped — **not** required to begin first iteration [depends on loop host] [mandatory - observability and stop control]";

    public const string GracefulShutdown =
        "Wire graceful shutdown: Ctrl+C/SIGTERM/`IHostApplicationLifetime` cancellation stops loop host; in-flight iteration completes or cancels within bounded timeout without corrupting artifact store [depends on loop host] [mandatory - stop when asked]";

    public const string LiveMatchFeed =
        "Publish live match frames to viewer catalog during each iteration so `GET /bones/sessions/{sessionId}/matches/{matchId}` returns increasing `frameCount` while loop is running [depends on in-process feed] [mandatory - live visualization]";

    public const string StubModelProviders =
        "Register default stub `IModelProvider` implementations (strategy, play-turn, enhancement) so loop runs without external API keys [depends on host DI]";

    public const string IntegrationTests =
        "Add `Wip.Bones.Host.Tests` proving host auto-starts loop, completes at least two consecutive iterations without POST between them, and serves viewer snapshots with engine-equivalent state [depends on loop host] [mandatory - integration proof]";

    public const string StopSemanticsTests =
        "Add tests proving `POST /api/bones/stop` and host shutdown stop further iterations after the current one finishes [depends on loop host] [mandatory - stop semantics]";

    public const string ProcessSmokeTest =
        "Add process smoke test: launch host, wait for two iterations via status polling, call stop, verify exit code 0 [depends on host tests]";

    public const string OperatorDocumentation =
        "Document canonical operator flow: `dotnet run --project WIP/Wip.Bones.Host` starts loop + viewer; open `/viewer/`; stop with Ctrl+C or `POST /api/bones/stop`; mark shell/`WIP_BONES_E2E` path legacy test-only [depends on smoke test]";

    public const string ShellIsolation =
        "Add negative gate: host assembly must not reference or load `Wip.ShellHost`/`Wip.Shell` types [depends on host project] [mandatory - isolation from WIP shell]";

    public const string ComplianceRegistry =
        "Register `harness/requirements/Wip.Bones.Host.md` in `BehaviorProofComplianceRegistry` with owning assembly `Wip.Bones.Host.Tests` [depends on integration tests]";

    public const string BehaviorProofPolicy =
        "Enforce absolute behavior-proof verification for every planned integration test [mandatory - behavior-proof policy]";

    public const string ViewerLayoutLiveView =
        "Add `Wip.Bones.Host.Tests` coverage: open `/view` while loop publishes live state; scrub mid-match; assert frames remain coherent (no mixed-revision DOM, no unhandled 404 loop) [depends on host feed] [mandatory - host live-view proof]";

    public const string ViewerLayoutRevision =
        "Expose `revision` on existing JSON routes (`GET .../matches/{matchId}`, `GET .../frames/{turnIndex}`) via camelCase serialization [depends on extended view models]";

    public const string StatusApiLearningPlayer =
        "Expose `GET /api/bones/status` fields for learning player active strategy id, candidate id (if any), effectiveness summary (wins/losses/score differential), `gamesSimulated`, `maxGamesPerRun`, and `resumeFromBest` [depends on knowledge store APIs and game budget]";

    public const string MaxGamesPerRunOption =
        "Add `BonesHostOptions.MaxGamesPerRun` (default `0` = unlimited) bound from `BonesHost:MaxGamesPerRun`, `BonesHost__MaxGamesPerRun`, and `BONES_MAX_GAMES_PER_RUN` with precedence: dedicated env var → double-underscore env → config section [depends on host options pattern] [mandatory - per-run game cap configuration]";

    public const string GameSimulationBudget =
        "Implement `BonesGameSimulationBudget` tracking cumulative simulated games across observe, play, and promotion-evaluation stages; expose `GamesSimulated`, `MaxGamesPerRun`, and `TryReserve(gameCount)` returning false when the reservation would exceed the cap [depends on MaxGamesPerRun option] [mandatory - game budget tracker]";

    public const string LearningLoopGameCap =
        "Update `BonesLearningLoopHost` to consult `BonesGameSimulationBudget` before starting each iteration; when `TryReserve` fails for the iteration's planned game budget, set loop state to stopped-with-cap-reached and exit gracefully after the prior iteration completes [depends on game budget tracker] [mandatory - graceful cap stop]";

    public const string GameSimulationBudgetInstrumentation =
        "Instrument `BonesObserveGamesTool`, `BonesPlayMatchTool`, and `BonesStrategyPromotionEvaluator` to report actual games simulated back to `BonesGameSimulationBudget` via `CapabilityContext` or injected budget service [depends on game budget tracker]";

    public const string ResumeFromBestOption =
        "Add `BonesHostOptions.ResumeFromBest` (default `true`) bound from `BonesHost:ResumeFromBest`, `BonesHost__ResumeFromBest`, and `BONES_RESUME_FROM_BEST` [depends on host options pattern] [mandatory - resume configuration]";

    public const string StrategyLibrary =
        "Implement repository-scoped `BonesStrategyLibrary` persisting under `{DataDirectory}/.bones/strategy-library/` one `BonesStrategyLibraryEntry` per seat with strategy artifact source, `BonesStrategyId`, effectiveness metrics, and `LastUpdatedUtc` — survives session and process restarts [depends on effectiveness record and artifact model] [mandatory - persistent strategy library]";

    public const string StrategyLibraryRanking =
        "Define library ranking: primary sort by win rate (`Wins / MatchesPlayed`) with minimum `MatchesPlayed >= 1`; tie-break by `CumulativeScoreDifferential` descending; then by `LastUpdatedUtc` [depends on strategy library]";

    public const string StrategyLibraryUpsertBestStrategy =
        "Implement `BonesStrategyLibrary.UpsertBestStrategy` after successful promotion or when an active strategy's effectiveness exceeds the stored entry for that seat [depends on library ranking and promotion gate]";

    public const string StrategyBootstrapper =
        "Replace `BonesLearningLoopHost.SeedInitialStrategiesAsync` hardcoded markdown v1 seeds with `BonesStrategyBootstrapper` that, when `ResumeFromBest=true`, copies each seat's library best into the new session as `PromotionStatus=Active` and sets active pointers; when no library entry exists for a seat, retain current default-seed behavior for opponents and run ponder for the learning player [depends on strategy library and active pointer APIs] [mandatory - restart from best bootstrap]";

    public const string PonderBootstrapSkip =
        "Skip `BonesPonderAgent` dispatch for seats that received a library bootstrap in the current session (strategy already active); workflow map or host pre-stage gate records `BootstrapSource=Library` on execution artifact [depends on bootstrapper]";

    public const string OperatorSetupDocumentation =
        "Document operator setup: `BONES_MAX_GAMES_PER_RUN=50` for bounded runs; `BONES_RESUME_FROM_BEST=true` (default) to continue from prior bests; `BONES_RESUME_FROM_BEST=false` for cold-start experiments [depends on host wiring]";

    public const string CoLearningConcurrencyProof =
        "Add host integration test proving that with `ParallelSessionCount=4` and `AllSeatsLearning=true`, four concurrent co-learning iterations complete in less wall-clock time than four serial iterations [mandatory - concurrency proof]";

    public const string CoLearningRestartSurvivalProof =
        "Add host integration test proving that after a restart with `ResumeFromBest=true` and `AllSeatsLearning=true`, all four seats load their library strategies and the first match transcript differs from a cold-start transcript [mandatory - restart survival proof]";

    public const string PromotionArtifactSeatReading =
        "Fix `TryReadAllSeatPromotionOutcomesAsync` to read the `\"LearningPlayerSeat\"` JSON property (in addition to `\"Seat\"`/`\"seat\"`) so the `seat > 0` guard passes and `CoLearningIterationsCompleted` increments [depends on promotion artifact schema]";

    public const string PromotionArtifactSeatReadingTests =
        "Add tests proving `CoLearningIterationsCompleted` increments after a co-learning iteration completes with promotion artifacts that use `LearningPlayerSeat` property; also test reading from raw JSON with all four property variants [depends on PromotionArtifactSeatReading]";

    public const string StrategiesPromotedRejectedCounts =
        "Replace hardcoded `strategiesPromoted = 0` / `strategiesRejected = 0` in `GET /api/bones/status` with live counts computed from `BonesCoLearningMetrics.LastPromotionOutcomes` (count where `Outcome` equals `\"Promoted\"` / `\"Rejected\"`, case-insensitive) [depends on co-learning metrics]";
}