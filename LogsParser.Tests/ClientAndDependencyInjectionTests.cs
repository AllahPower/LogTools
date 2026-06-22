using Microsoft.Extensions.DependencyInjection;
using LogsParser.Abstractions;
using LogsParser.DependencyInjection;
using LogsParser.Models;
using Xunit;

namespace LogsParser.Tests;

public class ClientAndDependencyInjectionTests
{
    private sealed class StubDataSource(string html) : ILogsParserDataSource
    {
        public Task<string> GetContentAsync(ParserRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(html);
    }

    [Fact]
    public async Task Client_parses_filter_catalog_from_data_source()
    {
        var html = "<select name='type[]'><option value='connect'>Подключение</option></select>";
        var client = new LogsParserClient(new StubDataSource(html));

        var catalog = await client.GetLogsFilterCatalogAsync();

        var filter = Assert.Single(catalog.Filters);
        Assert.Equal("connect", filter.Code);
        Assert.Equal("Подключение", filter.Name);
    }

    [Fact]
    public void Client_rejects_null_data_source()
    {
        Assert.Throws<ArgumentNullException>(() => new LogsParserClient(null!));
    }

    [Fact]
    public void AddLogsParser_resolves_client_with_overridden_data_source()
    {
        var services = new ServiceCollection();
        services.AddLogsParser(options =>
        {
            options.Credentials = new LogsParserCredentials("login", "password", "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ");
            options.DataSourceFactory = _ => new StubDataSource("<html></html>");
        });

        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<LogsParserClient>());
        Assert.IsType<StubDataSource>(provider.GetRequiredService<ILogsParserDataSource>());
        Assert.NotNull(provider.GetRequiredService<LogsParserCredentials>());
    }

    [Fact]
    public void AddLogsParser_registers_defaults_without_configuration()
    {
        var services = new ServiceCollection();
        services.AddLogsParser();

        using var provider = services.BuildServiceProvider();

        Assert.IsType<MemoryCookieStorage>(provider.GetRequiredService<ICookieStorage>());
        Assert.NotNull(provider.GetRequiredService<LogsParserClient>());
    }
}
