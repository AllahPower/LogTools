using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LogsParser.Diagnostics;

public static class LogsParserLogging
{
    private static volatile ILoggerFactory _loggerFactory = NullLoggerFactory.Instance;
    private static readonly ConcurrentDictionary<string, ILogger> _loggerCache = new(StringComparer.Ordinal);

    public static ILogger CreateLogger<T>()
    {
        return CreateLogger(typeof(T).Name);
    }

    public static ILogger CreateLogger(string categoryName)
    {
        return _loggerCache.GetOrAdd(categoryName, static name => _loggerFactory.CreateLogger(name));
    }

    public static void UseLoggerFactory(ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _loggerFactory = loggerFactory;
        _loggerCache.Clear();
    }

    public static void Reset()
    {
        _loggerFactory = NullLoggerFactory.Instance;
        _loggerCache.Clear();
    }
}
