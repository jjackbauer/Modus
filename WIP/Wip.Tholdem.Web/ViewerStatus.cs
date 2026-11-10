namespace Wip.Tholdem.Web;

/// <summary>
/// The GET /api/status response: an offline spectator viewer over one data directory.
/// <see cref="LiveMatches"/> counts only matches that are running or paused at the time of the
/// request; completed and stopped matches are retained for their final snapshot but are not live.
/// </summary>
public sealed record ViewerStatus(
    string Mode,
    string DataDirectory,
    int Sessions,
    int Strategies,
    int LiveMatches,
    bool Offline);
