namespace Wip.Bones.Host.Tests;

internal static class BonesLearningLoopReliabilityRequirementsChecklistItems
{
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

    public const string OperatorDiagnosticsReadme =
        "Document operator diagnostics in `Wip.Bones.Host/README.md` [depends on artifact extensions]";

    public const string BehaviorProofPolicy =
        "Enforce absolute behavior-proof verification for every planned integration test [mandatory - behavior-proof policy]";

    public const string ComplianceRegistry =
        "Register `harness/requirements/Wip.Bones.LearningLoopReliability.md` in `BehaviorProofComplianceRegistry` [depends on test coverage]";
}