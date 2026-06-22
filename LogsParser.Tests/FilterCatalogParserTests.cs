using LogsParser.Parsing;
using Xunit;

namespace LogsParser.Tests;

public class FilterCatalogParserTests
{
    private const string CatalogHtml = """
        <html><head><meta name='csrf-token' content='t'></head>
        <body>
        <nav>
          <a id='navbarDropdown'>Marco_Beer</a>
          <ul class='navbar-nav ms-auto'>
            <span class='badge bg-primary'>Admin</span>
            <span class='badge bg-success'>VIP</span>
          </ul>
          <select name='server_number'>
            <option value='18' selected>[18] Phoenix</option>
            <option value='1'>[1] MARP</option>
          </select>
        </nav>
        <form>
          <select name='type[]'>
            <option value=''>—</option>
            <option value='connect'>Подключение</option>
            <option value='kill'>Убийство</option>
          </select>
          <div class='js_component_filter_item' data-filter-type='kill'>
            <label>Оружие</label>
            <input name='dynamic[0]' type='text'>
          </div>
        </form>
        </body></html>
        """;

    [Fact]
    public void Parses_filter_options()
    {
        var catalog = LogsFilterCatalogParser.Parse(CatalogHtml);

        Assert.Equal(2, catalog.Filters.Count);
        Assert.Contains(catalog.Filters, f => f is { Code: "connect", Name: "Подключение" });
        Assert.Contains(catalog.Filters, f => f is { Code: "kill", Name: "Убийство" });
    }

    [Fact]
    public void Parses_additional_parameters_for_filter()
    {
        var catalog = LogsFilterCatalogParser.Parse(CatalogHtml);

        var kill = Assert.Single(catalog.Filters, f => f.Code == "kill");
        var parameter = Assert.Single(kill.AdditionalParameters);
        Assert.Equal("dynamic[0]", parameter.QueryKey);
        Assert.Equal("Оружие", parameter.Label);
    }

    [Fact]
    public void Parses_account_from_layout()
    {
        var catalog = LogsFilterCatalogParser.Parse(CatalogHtml);

        Assert.NotNull(catalog.Account);
        Assert.Equal("Marco_Beer", catalog.Account!.Nickname);
        Assert.Contains(catalog.Account.AvailableServers, s => s is { Id: 18, IsSelected: true, Name: "Phoenix" });
        Assert.Contains(catalog.Account.Badges, b => b.Name == "Admin");
    }

    [Fact]
    public void Empty_html_throws()
    {
        Assert.ThrowsAny<ArgumentException>(() => LogsFilterCatalogParser.Parse("   "));
    }
}
