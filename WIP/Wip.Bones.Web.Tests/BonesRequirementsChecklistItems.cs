namespace Wip.Bones.Web.Tests;

internal static class BonesRequirementsChecklistItems
{
    public const string ViewerApi =
        "Introduce `Wip.Bones.Web` ASP.NET Core host exposing read-only match viewer API (`GET /bones/sessions/{sessionId}/matches/{matchId}`, `GET /bones/sessions/{sessionId}/matches/{matchId}/frames/{turnIndex}`) returning typed view models derived from `BonesRoundState`, event log, or persisted transcript artifacts [depends on game engine and match simulator] [mandatory - game visualization]";

    public const string BrowserViewerUi =
        "Implement browser viewer UI (static SPA served by `Wip.Bones.Web`) rendering domino board layout, four player hands, pip totals, active seat, pass/play markers, round score, and match replay scrubber synchronized to event-log turn indices [depends on viewer API] [mandatory - game visualization]";

    public const string ViewerWorkflowWiring =
        "Wire workflow/shell integration so `workflow.bones.learning` run publishes viewer base URL (stdout marker or session artifact) and live play/observe stages push frame updates consumable by the viewer without cross-player strategy leakage [depends on viewer UI and play/observe tools]";
}