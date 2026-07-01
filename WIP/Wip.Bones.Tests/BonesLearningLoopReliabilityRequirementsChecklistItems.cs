namespace Wip.Bones.Tests;

internal static class BonesLearningLoopReliabilityRequirementsChecklistItems
{
    public const string PonderCompileRetry =
        "Add `BonesPonderAgent` compile-error retry: on first `TryCompile` failure, re-invoke model provider with user message containing `BonesStrategyScriptCompileResult.FailureReason` and API contract excerpt; cap at `MaxCompileRetries` (default `2`); persist script only after successful compile; emit ponder execution artifact recording attempt count and final outcome [foundation for ponder reliability] [mandatory - compile retry]";

    public const string PonderCompileRetryOptions =
        "Add `BonesPonderCompileRetryOptions` with `MaxCompileRetries` bound from `BonesHost:Ponder:MaxCompileRetries` / env override; register in host DI [depends on compile retry] [mandatory - configurable retry budget]";

    public const string PonderExecutionArtifactFields =
        "Extend `bones-ponder-execution` artifact JSON with `CompileAttempts`, `FinalCompileSucceeded`, and optional `LastCompileFailureReason` for operator forensics [depends on compile retry]";

    public const string TargetScoreParity =
        "Wire `BonesLearningWorkflowMapRuntime` enhance map to pass `parameters.TargetScore` as `BonesEnhanceStrategyRequest.EvaluationTargetScore` (remove silent fallback to `DefaultEvaluationTargetScore = 8` when session specifies `10`) [depends on workflow parameters] [mandatory - target score parity]";

    public const string SharedPlayerSlotFactory =
        "Extract shared `BonesStrategyPlayerSlotFactory` (or equivalent) used by both `BonesPlayMatchTool` and `BonesStrategyPromotionEvaluator.CreatePlayerSlot` so markdown opponents dispatch through the same path in play and promotion evaluation [depends on play match tool] [mandatory - promotion opponent parity]";

    public const string PromotionEvaluatorMarkdownParity =
        "Add `BonesStrategyPromotionEvaluator` unit tests proving candidate evaluated against markdown opponents using the same slot factory as play -- not `BonesFirstLegalMovePlayerSlot` when play would call the LLM path [depends on shared factory]";

    public const string PromotionArtifactAlways =
        "Persist `bones-strategy-promotion` artifact on every enhance invocation (including `Rejected`) with full `BonesPromotionEvaluationMetrics` snapshot [depends on promotion evaluator]";


    public const string LastIterationError =
        "Extend `BonesLoopIterationStatus` / `BonesLoopState` with `LastIterationError` (message + stage) set in `BonesLearningLoopHost.RunIterationAsync` catch block; clear on successful iteration complete [depends on loop host catch handler]";

    public const string GranularStages =
        "Replace coarse `\"Running\"` stage with workflow stage names (`Ponder`, `Play`, `Enhance`) via orchestrator stage callbacks or last completed stage artifact [depends on loop host] [mandatory - granular stage observability]";

    public const string StatusApiDefensiveReads =
        "Harden `GET /api/bones/status` learning-player block: wrap knowledge-store reads in try/catch; return `learningPlayer: null` with optional `learningPlayerStatus: \"pending\"` during ponder; never throw on missing/corrupt active pointer [depends on TryLoadActiveStrategy] [mandatory - status API defensive reads]";

    public const string StatusApiExtendedFields =
        "Extend status response with `lastIterationError`, `lastPromotionOutcome`, and `libraryEffectiveness` from `BonesStrategyLibrary` for the learning seat [depends on status hardening and promotion artifacts]";

    public const string IterationFailureIsolation =
        "Ensure host process does not exit with non-zero code when a single iteration fails but the background loop is still running [depends on loop host] [mandatory - iteration failure isolation]";

    public const string StageFailureArtifact =
        "Add stage failure artifact recording which stage threw when `RunWorkflowAsync` aborts [depends on orchestrator integration]";

    public const string DeepSeekForbiddenMembers =
        "Update `DeepSeekBonesStrategyAuthoringProvider` tests to assert outbound content includes forbidden API members from `BonesStrategyScriptApiReference` [depends on API reference] [mandatory - prompt contract regression gate]";

    public const string OperatorDiagnosticsReadme =
        "Document operator diagnostics in `Wip.Bones.Host/README.md` [depends on artifact extensions]";
    public const string BehaviorProofPolicy =
        "Enforce absolute behavior-proof verification for every planned integration test [mandatory - behavior-proof policy]";

    public const string ComplianceRegistry =
        "Register `harness/requirements/Wip.Bones.LearningLoopReliability.md` in `BehaviorProofComplianceRegistry` [depends on test coverage]";
}