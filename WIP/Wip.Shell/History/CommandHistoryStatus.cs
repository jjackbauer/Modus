namespace Wip.Shell.History;

public readonly record struct CommandHistoryStatus(
    bool PersistenceEnabled,
    string StatusCode,
    string Description,
    string? HistoryFilePath);