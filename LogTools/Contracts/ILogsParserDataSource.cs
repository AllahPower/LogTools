namespace LogsParser.Abstractions;

public interface ILogsParserDataSource
{
    Task<string> GetContentAsync(ParserRequest request, CancellationToken cancellationToken = default);
}
