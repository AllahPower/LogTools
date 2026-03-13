using System.Net;

namespace LogsParser.Models;

public sealed record LogsQuery(
    int ServerId,
    IReadOnlyCollection<string>? Filters = null,
    DateTime? PeriodFrom = null,
    DateTime? PeriodTo = null,
    string? Player = null,
    string? Target = null,
    IPAddress? IpAddress = null,
    IReadOnlyDictionary<string, string>? AdditionalParameters = null,
    int Page = 1,
    int Limit = 1000,
    string Sort = "desc");

public sealed record AdminActivityQuery(DateTime PeriodFrom, DateTime PeriodTo);

public sealed record TopOperationsQuery(string? Filter = null, DateTime? Date = null);
