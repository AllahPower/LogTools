using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LogsParser.Diagnostics;

public static class LogsParserLogging
{
    private static volatile ILoggerFactory _loggerFactory = NullLoggerFactory.Instance;
    private static volatile ConcurrentDictionary<string, ILogger> _loggerCache = new(StringComparer.Ordinal);
    private static readonly object _sync = new();

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

        lock (_sync)
        {
            _loggerFactory = loggerFactory;
            _loggerCache = new ConcurrentDictionary<string, ILogger>(StringComparer.Ordinal);
        }
    }

    public static void Reset()
    {
        lock (_sync)
        {
            _loggerFactory = NullLoggerFactory.Instance;
            _loggerCache = new ConcurrentDictionary<string, ILogger>(StringComparer.Ordinal);
        }
    }
}
