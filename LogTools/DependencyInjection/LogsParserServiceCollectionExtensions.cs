using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using LogsParser.Abstractions;
using LogsParser.Diagnostics;
using LogsParser.Models;
using LogsParser.Net;

namespace LogsParser.DependencyInjection;

public static class LogsParserServiceCollectionExtensions
{
    public static IServiceCollection AddLogsParser(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<ICookieStorage, MemoryCookieStorage>();
        services.TryAddSingleton(new LogsParserHttpOptions());
        services.TryAddTransient<ILogsParserDataSource>(CreateDataSource);
        services.TryAddTransient<LogsParserClient>();

        return services;
    }

    public static IServiceCollection AddLogsParser(
        this IServiceCollection services,
        Action<LogsParserRegistrationOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new LogsParserRegistrationOptions();
        configure(options);

        services.TryAddSingleton(options.HttpOptions);

        if (options.Credentials is not null)
        {
            services.TryAddSingleton(options.Credentials);
        }

        if (options.CookieStorageFactory is not null)
        {
            services.Replace(ServiceDescriptor.Singleton(typeof(ICookieStorage), options.CookieStorageFactory));
        }
        else
        {
            services.TryAddSingleton<ICookieStorage, MemoryCookieStorage>();
        }

        if (options.DataSourceFactory is not null)
        {
            services.Replace(ServiceDescriptor.Transient(typeof(ILogsParserDataSource), options.DataSourceFactory));
        }
        else
        {
            services.TryAddTransient<ILogsParserDataSource>(CreateDataSource);
        }

        services.TryAddTransient<LogsParserClient>();
        return services;
    }

    private static ILogsParserDataSource CreateDataSource(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance;
        LogsParserLogging.UseLoggerFactory(loggerFactory);

        return new LogsParserHttpDataSource(
            serviceProvider.GetService<LogsParserCredentials>(),
            serviceProvider.GetRequiredService<ICookieStorage>(),
            serviceProvider.GetRequiredService<LogsParserHttpOptions>(),
            httpClient: null,
            loggerFactory: loggerFactory);
    }
}
