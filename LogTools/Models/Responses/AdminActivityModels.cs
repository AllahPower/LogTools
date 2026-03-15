namespace LogsParser.Models;

public sealed record AdminActivityReport(
    (DateTime From, DateTime To) Period,
    IReadOnlyList<AdminActivityEntry> Entries,
    AdminActivityMetaInfo MetaInfo);

public sealed record AdminActivityEntry(
    string Nickname,
    int Id,
    int AdminLevel,
    int TotalReports,
    int TotalMutes,
    int TotalJails,
    int TotalWarns,
    int TotalBans,
    int TotalThanks,
    TimeSpan TotalOnline,
    IReadOnlyList<AdminActivityDay> Details);

public sealed record AdminActivityDay(
    DateTime Date,
    TimeSpan Online,
    int Reports,
    int Mutes,
    int Jails,
    int Warns,
    int Bans);

public sealed record AdminActivityMetaInfo(int AdminCount, int PeriodDays, int TotalReports, int TotalBans);
