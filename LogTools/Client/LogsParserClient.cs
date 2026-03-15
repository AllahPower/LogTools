using Microsoft.Extensions.Logging;
using LogsParser.Abstractions;
using LogsParser.Diagnostics;
using LogsParser.Models;
using LogsParser.Parsing;

namespace LogsParser;

public sealed class LogsParserClient
{
    private static ILogger Logger => LogsParserLogging.CreateLogger<LogsParserClient>();

    private readonly ILogsParserDataSource _dataSource;

    public LogsParserClient(ILogsParserDataSource dataSource)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
    }

    public async Task<LogsPage> GetLogsAsync(
        LogsQuery query,
        ICookieStorage? cookieStorage = null,
        CancellationToken cancellationToken = default)
    {
        var uri = LogsRequestUriBuilder.BuildLogsUri(query);
        Logger.LogDebug(
            "GetLogsAsync: ServerId={ServerId}, Filters=[{Filters}], Period={PeriodFrom}..{PeriodTo}, Page={Page}, Limit={Limit}",
            query.ServerId,
            query.Filters is not null ? string.Join(", ", query.Filters) : "",
            query.PeriodFrom?.ToString("yyyy-MM-dd HH:mm:ss"),
            query.PeriodTo?.ToString("yyyy-MM-dd HH:mm:ss"),
            query.Page,
            query.Limit);

        var html = await _dataSource.GetContentAsync(
            new ParserRequest(uri, cookieStorage),
            cancellationToken).ConfigureAwait(false);

        Logger.LogTrace("GetLogsAsync: received {ContentLength} characters of HTML", html.Length);

        var result = LogsHtmlParser.ParseLogs(html);

        Logger.LogInformation(
            "GetLogsAsync: parsed {EntryCount} entries, MetaInfo={Start}-{End}/{Total}",
            result.Entries.Count,
            result.MetaInfo?.Start,
            result.MetaInfo?.End,
            result.MetaInfo?.Total);

        return result;
    }

    public async Task<LogsFilterCatalog> GetLogsFilterCatalogAsync(
        ICookieStorage? cookieStorage = null,
        CancellationToken cancellationToken = default)
    {
        Logger.LogDebug("GetLogsFilterCatalogAsync: requesting filter catalog");

        var html = await _dataSource.GetContentAsync(
            new ParserRequest("/", cookieStorage),
            cancellationToken).ConfigureAwait(false);

        var result = LogsFilterCatalogParser.Parse(html);

        Logger.LogInformation(
            "GetLogsFilterCatalogAsync: parsed {FilterCount} filters",
            result.Filters.Count);

        return result;
    }

    public async Task<LogsAccount?> GetCurrentAccountAsync(
        ICookieStorage? cookieStorage = null,
        CancellationToken cancellationToken = default)
    {
        Logger.LogDebug("GetCurrentAccountAsync: requesting account info");

        var html = await _dataSource.GetContentAsync(
            new ParserRequest("/", cookieStorage),
            cancellationToken).ConfigureAwait(false);

        var result = LogsAccountParser.Parse(html);

        if (result is not null)
        {
            Logger.LogInformation(
                "GetCurrentAccountAsync: account={Nickname}, servers={ServerCount}, badges={BadgeCount}",
                result.Nickname,
                result.AvailableServers.Count,
                result.Badges.Count);
        }
        else
        {
            Logger.LogWarning("GetCurrentAccountAsync: account info not found in response");
        }

        return result;
    }

    public async Task<AdminActivityReport> GetAdminActivityAsync(
        AdminActivityQuery query,
        ICookieStorage? cookieStorage = null,
        CancellationToken cancellationToken = default)
    {
        Logger.LogDebug(
            "GetAdminActivityAsync: Period={PeriodFrom}..{PeriodTo}",
            query.PeriodFrom.ToString("yyyy-MM-dd HH:mm:ss"),
            query.PeriodTo.ToString("yyyy-MM-dd HH:mm:ss"));

        var html = await _dataSource.GetContentAsync(
            new ParserRequest(LogsRequestUriBuilder.BuildAdminActivityUri(query), cookieStorage),
            cancellationToken).ConfigureAwait(false);

        var result = LogsHtmlParser.ParseAdminActivity(html);

        Logger.LogInformation(
            "GetAdminActivityAsync: parsed {EntryCount} admins, {TotalReports} reports, {TotalBans} bans over {Days} days",
            result.Entries.Count,
            result.MetaInfo.TotalReports,
            result.MetaInfo.TotalBans,
            result.MetaInfo.PeriodDays);

        return result;
    }

    public async Task<TopOperationsReport> GetTopOperationsAsync(
        TopOperationsQuery query,
        ICookieStorage? cookieStorage = null,
        CancellationToken cancellationToken = default)
    {
        Logger.LogDebug(
            "GetTopOperationsAsync: Filter={Filter}, Date={Date}",
            query.Filter,
            query.Date?.ToString("yyyy-MM-dd"));

        var html = await _dataSource.GetContentAsync(
            new ParserRequest(LogsRequestUriBuilder.BuildTopOperationsUri(query), cookieStorage),
            cancellationToken).ConfigureAwait(false);

        var result = LogsHtmlParser.ParseTopOperations(html);

        Logger.LogInformation(
            "GetTopOperationsAsync: parsed {EntryCount} entries, {TotalTransactions} transactions, totalSum={TotalSum}",
            result.Entries.Count,
            result.MetaInfo.TotalTransactions,
            result.MetaInfo.TotalSum);

        return result;
    }
}
