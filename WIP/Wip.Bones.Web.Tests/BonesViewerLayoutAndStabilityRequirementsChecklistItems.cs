namespace Wip.Bones.Web.Tests;

internal static class BonesViewerLayoutAndStabilityRequirementsChecklistItems
{
    public const string MatchRevisionType =
        "Introduce `MatchRevision` typed value (monotonic per session/match key) on `BonesRegisteredMatch`; increment on every `Register` for the same session/match pair [foundation for coherent reads]";

    public const string ThreadSafeCatalog =
        "Replace or wrap `InMemoryBonesMatchCatalog` with a thread-safe implementation that atomically swaps immutable `BonesRegisteredMatch` instances (e.g. `ConcurrentDictionary` + lock, or immutable map swap) [depends on revision type] [mandatory - thread-safe catalog]";

    public const string ImmutablePublish =
        "Ensure `BonesCatalogPublicMatchFeed.PublishMatchState` and `BonesLearningViewerCoordinator.PublishLiveMatchState` always pass fully immutable `BonesMatchResult` graphs (no shared mutable list references after publish) [depends on thread-safe catalog] [mandatory - immutable publish]";

    public const string ExtendedViewModels =
        "Extend `BonesMatchViewModel` and `BonesMatchFrame` with `Revision`; populate from the captured `BonesRegisteredMatch` in `BonesMatchViewerService` [depends on revision type]";

    public const string JsonRevisionFields =
        "Expose `revision` on existing JSON routes (`GET .../matches/{matchId}`, `GET .../frames/{turnIndex}`) via camelCase serialization [depends on extended view models]";

    public const string CoherentHttpRead =
        "Refactor `BonesWebHost` match routes so each request captures one `BonesRegisteredMatch` reference and uses it for snapshot/timeline/frame derivation without re-reading mid-request [depends on thread-safe catalog] [mandatory - coherent HTTP read]";

    public const string SidebarShellLayout =
        "Restructure `wwwroot/viewer/index.html` and `BonesViewerPageRenderer` into `.viewer-shell` with `.viewer-sidebar` (seats + metadata + scrubber) and `.viewer-main` (board only) [depends on none] [mandatory - sidebar shell layout]";

    public const string FullCanvasBoard =
        "Update `viewer.css`: remove centered `max-width` constraint on the shell; sidebar fixed width (~20–24rem); `.viewer-main` flex-grow/min-height fills viewport; board chain scaling uses expanded main canvas bounds [depends on sidebar shell] [mandatory - full-canvas board]";

    public const string ViewerJsRelocation =
        "Update `viewer.js` DOM selectors and render paths for relocated sidebar nodes; preserve existing board/hand/scrubber behavior [depends on sidebar shell]";

    public const string SpaRevisionHandling =
        "Add SPA revision handling: on frame 404 or `frame.revision !== snapshot.revision`, reload snapshot and clamp/retry scrubber instead of leaving corrupt empty board/hands [depends on revision JSON and viewer.js] [mandatory - live-view stability]";

    public const string BehaviorProofTests =
        "Add `Wip.Bones.Web.Tests` coverage: concurrent catalog register/read stress, revision monotonicity, JSON revision fields, DOM sidebar/main geometry, board canvas larger than pre-change baseline, SSR/SPA structural parity [depends on all above] [mandatory - behavior proof]";

    public const string HostLiveViewProof =
        "Add `Wip.Bones.Host.Tests` coverage: open `/view` while loop publishes live state; scrub mid-match; assert frames remain coherent (no mixed-revision DOM, no unhandled 404 loop) [depends on host feed] [mandatory - host live-view proof]";

    public const string ComplianceRegistry =
        "Register this requirements document in `BehaviorProofComplianceRegistry` with owning `Wip.Bones.Web.Tests` checklist bindings [depends on tests]";

    public const string BehaviorProofPolicy =
        "Enforce absolute behavior-proof verification for every planned integration test [mandatory - behavior-proof policy]";
}