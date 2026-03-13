namespace LogsParser.Models;

public sealed record LogsAccount(
    string Nickname,
    IReadOnlyList<LogsAccountBadge> Badges,
    IReadOnlyList<LogsAccountServer> AvailableServers);

public sealed record LogsAccountBadge(string Name);

public sealed record LogsAccountServer(
    int Id,
    string Name,
    string DisplayName,
    bool IsSelected);
