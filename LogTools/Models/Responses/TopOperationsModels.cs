using System.Net;

namespace LogsParser.Models;

public sealed record TopOperationsReport(
    DateOnly Date,
    IReadOnlyList<TopOperationsEntry> Entries,
    TopOperationsMetaInfo MetaInfo);

public sealed record TopOperationsEntry(
    string Nickname,
    int Id,
    IPAddress Ip,
    IPAddress RegistrationIp,
    int TotalTransactions,
    ulong Sum);

public sealed record TopOperationsMetaInfo(int PlayerCount, int TotalTransactions, ulong TotalSum);
