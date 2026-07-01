namespace Wip.Bones.Web.Tests;

internal static class BonesViewerLearningLoopStatusRequirementsChecklistItems
{
    public const string SidebarHtmlSkeleton =
        "Add `#learning-loop-status` section to `wwwroot/viewer/index.html` with semantic child elements and stable ids/`data-*` hooks for every field group in the Sidebar Status Baseline table [foundation for DOM binding]";

    public const string SsrSpaParity =
        "Mirror the same `#learning-loop-status` skeleton in `BonesViewerPageRenderer` SSR output inside `.viewer-sidebar` [depends on HTML structure] [mandatory - SSR/SPA parity]";

    public const string CssPanel =
        "Add `viewer.css` rules for `.learning-loop-status`, label/value pairs, error/promotion emphasis, and hidden-unavailable state without breaking existing sidebar scroll layout [depends on HTML structure]";

    public const string PollLearningLoopStatus =
        "Implement `pollLearningLoopStatus` in `viewer.js`: fetch `/api/bones/status`, parse JSON, call renderer; swallow 404/network errors and hide panel [depends on HTML structure] [mandatory - status poll]";

    public const string RenderLearningLoopStatus =
        "Implement `renderLearningLoopStatus(status, snapshot, turnIndex)` in `viewer.js`: map status JSON and active match snapshot to panel text and `data-*` attributes per Sidebar Status Baseline; format game budget with zero-max guard [depends on poll function] [mandatory - field binding]";

    public const string WireStatusPoll =
        "Wire status poll into existing live update loop (`pollLiveUpdates` or parallel interval) without blocking match snapshot reload or scrubber frame loads [depends on poll function]";

    public const string MatchTurnLine =
        "Update match turn line when snapshot `frameCount` or scrubber value changes so `Turn X of Y` stays coherent during live play and replay scrub [depends on render function] [mandatory - match turn progress]";

    public const string PendingErrorDisplay =
        "Show `learningPlayerStatus: \"pending\"` and omit effectiveness block until `learningPlayer` is present; surface `learningPlayerError` and `lastIterationError` in distinct error styling [depends on render function] [mandatory - defensive display]";

    public const string NoSecretLeakage =
        "Never render secret values: omit `apiKeySource` value display beyond existing diagnostic label if shown; assert no API key patterns in DOM tests [depends on render function]";

    public const string BehaviorProofTests =
        "Add `Wip.Bones.Web.Tests` coverage: stub `/api/bones/status` on test factory; serve viewer; assert DOM `data-*` and text match stub JSON; assert panel hidden when status returns 404; assert turn line updates when snapshot `frameCount` increases [depends on all viewer changes] [mandatory - behavior proof]";

    public const string HostLiveLoopProof =
        "Add `Wip.Bones.Host.Tests` coverage: open `/viewer/` while learning loop runs (stub providers); poll until `currentStage` advances; assert sidebar `data-current-stage` and iteration text track status API [depends on host + viewer wiring] [mandatory - host live-loop proof]";

    public const string ComplianceRegistry =
        "Register this requirements document in `BehaviorProofComplianceRegistry` with owning `Wip.Bones.Web.Tests` checklist bindings [depends on tests]";

    public const string BehaviorProofPolicy =
        "Enforce absolute behavior-proof verification for every planned integration test [mandatory - behavior-proof policy]";
}