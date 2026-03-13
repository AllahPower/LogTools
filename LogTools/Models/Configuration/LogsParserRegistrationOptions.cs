using LogsParser.Abstractions;

namespace LogsParser.Models;

public sealed class LogsParserRegistrationOptions
{
    public LogsParserCredentials? Credentials { get; set; }

    public LogsParserHttpOptions HttpOptions { get; set; } = new();

    public Func<IServiceProvider, ICookieStorage>? CookieStorageFactory { get; set; }

    public Func<IServiceProvider, ILogsParserDataSource>? DataSourceFactory { get; set; }
}
