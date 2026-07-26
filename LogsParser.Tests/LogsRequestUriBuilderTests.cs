using System.Net;
using LogsParser.Models;
using Xunit;

namespace LogsParser.Tests;

public class LogsRequestUriBuilderTests
{
    [Fact]
    public void Builds_basic_query()
    {
        var uri = LogsRequestUriBuilder.BuildLogsUri(new LogsQuery(ServerId: 18));

        Assert.StartsWith("?", uri);
        Assert.Contains("server_number=18", uri);
        Assert.Contains("sort=desc", uri);
        Assert.Contains("limit=1000", uri);
        Assert.Contains("page=1", uri);
    }

    [Theory]
    [InlineData("desc")]
    [InlineData("asc")]
    [InlineData("ASC")]
    public void Accepts_the_two_orderings_the_toolbar_offers(string sort)
    {
        var uri = LogsRequestUriBuilder.BuildLogsUri(new LogsQuery(18, Sort: sort));

        Assert.Contains($"sort={sort.ToLowerInvariant()}", uri);
    }

    [Theory]
    [InlineData("descending")]
    [InlineData("newest")]
    [InlineData("")]
    public void Rejects_unsupported_sort(string sort)
    {
        Assert.Throws<ArgumentException>(() => LogsRequestUriBuilder.BuildLogsUri(new LogsQuery(18, Sort: sort)));
    }

    [Fact]
    public void Encodes_filters_as_type_array()
    {
        var uri = LogsRequestUriBuilder.BuildLogsUri(new LogsQuery(18, Filters: ["connect", "kill"]));

        Assert.Contains("type%5B%5D=connect", uri);
        Assert.Contains("type%5B%5D=kill", uri);
    }

    [Fact]
    public void Appends_period_player_target_and_ip()
    {
        var uri = LogsRequestUriBuilder.BuildLogsUri(new LogsQuery(
            18,
            PeriodFrom: new DateTime(2026, 5, 1, 3, 0, 0),
            PeriodTo: new DateTime(2026, 6, 1, 3, 0, 0),
            Player: "Ivan",
            Target: "Petr",
            IpAddress: IPAddress.Parse("127.0.0.1")));

        Assert.Contains("min_period=2026-05-01%2003%3A00%3A00", uri);
        Assert.Contains("max_period=2026-06-01%2003%3A00%3A00", uri);
        Assert.Contains("player=Ivan", uri);
        Assert.Contains("target=Petr", uri);
        Assert.Contains("ip=127.0.0.1", uri);
    }

    [Theory]
    [InlineData(50, 100)]
    [InlineData(250, 100)]
    [InlineData(600, 500)]
    [InlineData(100, 100)]
    [InlineData(500, 500)]
    [InlineData(1000, 1000)]
    public void Normalizes_limit_to_supported_values(int input, int expected)
    {
        var uri = LogsRequestUriBuilder.BuildLogsUri(new LogsQuery(18, Limit: input));

        Assert.Contains($"limit={expected}", uri);
    }

    [Fact]
    public void Rejects_non_positive_server()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LogsRequestUriBuilder.BuildLogsUri(new LogsQuery(0)));
    }

    [Fact]
    public void Rejects_non_positive_page()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LogsRequestUriBuilder.BuildLogsUri(new LogsQuery(18, Page: 0)));
    }

    [Fact]
    public void Accepts_dynamic_additional_parameter()
    {
        var uri = LogsRequestUriBuilder.BuildLogsUri(new LogsQuery(
            18,
            AdditionalParameters: new Dictionary<string, string> { ["dynamic[0]"] = "42" }));

        Assert.Contains("dynamic%5B0%5D=42", uri);
    }

    [Fact]
    public void Rejects_reserved_additional_parameter()
    {
        Assert.Throws<ArgumentException>(() => LogsRequestUriBuilder.BuildLogsUri(new LogsQuery(
            18,
            AdditionalParameters: new Dictionary<string, string> { ["player"] = "x" })));
    }

    [Fact]
    public void Rejects_unknown_additional_parameter()
    {
        Assert.Throws<ArgumentException>(() => LogsRequestUriBuilder.BuildLogsUri(new LogsQuery(
            18,
            AdditionalParameters: new Dictionary<string, string> { ["foo"] = "x" })));
    }

    [Fact]
    public void Builds_admin_activity_uri()
    {
        var uri = LogsRequestUriBuilder.BuildAdminActivityUri(new AdminActivityQuery(
            new DateTime(2026, 5, 1, 3, 0, 0),
            new DateTime(2026, 6, 1, 3, 0, 0)));

        Assert.StartsWith("/admins?", uri);
        Assert.Contains("min_period=2026-05-01%2003%3A00%3A00", uri);
        Assert.Contains("max_period=2026-06-01%2003%3A00%3A00", uri);
    }

    [Fact]
    public void Builds_top_operations_uri()
    {
        var uri = LogsRequestUriBuilder.BuildTopOperationsUri(new TopOperationsQuery("transfer", new DateTime(2026, 6, 1)));

        Assert.StartsWith("top?", uri);
        Assert.Contains("type=transfer", uri);
        Assert.Contains("date=2026-06-01%2000%3A00%3A00", uri);
    }
}
