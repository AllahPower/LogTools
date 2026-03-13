using LogsParser.Abstractions;
using LogsParser.Models;
using LogsParser.Parsing;

namespace LogsParser;

public sealed class LogsParserClient
{
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
        var html = await _dataSource.GetContentAsync(
            new ParserRequest(LogsRequestUriBuilder.BuildLogsUri(query), cookieStorage),
            cancellationToken).ConfigureAwait(false);

        return LogsHtmlParser.ParseLogs(html);
    }

    public async Task<LogsFilterCatalog> GetLogsFilterCatalogAsync(
        ICookieStorage? cookieStorage = null,
        CancellationToken cancellationToken = default)
    {
        var html = await _dataSource.GetContentAsync(
            new ParserRequest("/", cookieStorage),
            cancellationToken).ConfigureAwait(false);

        return LogsFilterCatalogParser.Parse(html);
    }

    public async Task<LogsAccount?> GetCurrentAccountAsync(
        ICookieStorage? cookieStorage = null,
        CancellationToken cancellationToken = default)
    {
        var html = await _dataSource.GetContentAsync(
            new ParserRequest("/", cookieStorage),
            cancellationToken).ConfigureAwait(false);

        return LogsAccountParser.Parse(html);
    }

    public async Task<AdminActivityReport> GetAdminActivityAsync(
        AdminActivityQuery query,
        ICookieStorage? cookieStorage = null,
        CancellationToken cancellationToken = default)
    {
        var html = await _dataSource.GetContentAsync(
            new ParserRequest(LogsRequestUriBuilder.BuildAdminActivityUri(query), cookieStorage),
            cancellationToken).ConfigureAwait(false);

        return LogsHtmlParser.ParseAdminActivity(html);
    }

    public async Task<TopOperationsReport> GetTopOperationsAsync(
        TopOperationsQuery query,
        ICookieStorage? cookieStorage = null,
        CancellationToken cancellationToken = default)
    {
        var html = await _dataSource.GetContentAsync(
            new ParserRequest(LogsRequestUriBuilder.BuildTopOperationsUri(query), cookieStorage),
            cancellationToken).ConfigureAwait(false);

        return LogsHtmlParser.ParseTopOperations(html);
    }
}
