using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LogsParser.Diagnostics;

public static class LogsParserLogging
{
    private static ILoggerFactory _loggerFactory = NullLoggerFactory.Instance;

    public static ILogger CreateLogger<T>()
    {
        return _loggerFactory.CreateLogger<T>();
    }

    public static ILogger CreateLogger(string categoryName)
    {
        return _loggerFactory.CreateLogger(categoryName);
    }

    public static void UseLoggerFactory(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
    }

    public static void Reset()
    {
        _loggerFactory = NullLoggerFactory.Instance;
    }
}
