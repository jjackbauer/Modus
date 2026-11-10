namespace Wip.Tholdem.Web;

/// <summary>
/// Immutable configuration for the read-only Tholdem spectator viewer. The data directory is the
/// frozen artifact root: sessions, strategies, the promotion ledger and the active champion.
/// </summary>
public sealed record TholdemViewerOptions(string DataDirectory, int DefaultDelayMs = 250, int MaxLiveHands = 500);