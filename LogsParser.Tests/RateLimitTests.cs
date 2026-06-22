using System.Net;
using LogsParser.Exceptions;
using LogsParser.Models;
using Xunit;

namespace LogsParser.Tests;

public class RateLimitTests
{
    [Fact]
    public async Task Rate_limit_throws_when_not_waiting()
    {
        var handler = new FakeHttpMessageHandler(_ =>
            FakeHttpMessageHandler.Status(HttpStatusCode.TooManyRequests, ("Retry-After", "7")));
        var options = new LogsParserHttpOptions { WaitForRateLimitReset = false };
        using var dataSource = TestDataSource.Create(handler, credentials: null, options: options);

        var exception = await Assert.ThrowsAsync<RateLimitExceededException>(
            () => dataSource.GetContentAsync(new ParserRequest("/admins")));

        Assert.Equal(7, exception.RetryAfterSeconds);
    }

    [Fact]
    public async Task Rate_limit_headers_are_exposed_on_success()
    {
        var handler = new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Ok(
            "ok",
            ("X-Ratelimit-Limit", "20"),
            ("X-Ratelimit-Remaining", "5"),
            ("X-Ratelimit-Reset", "1893456000")));
        using var dataSource = TestDataSource.Create(handler, credentials: null);

        await dataSource.GetContentAsync(new ParserRequest("/admins"));

        Assert.Equal(20, dataSource.RateLimitMax);
        Assert.Equal(5, dataSource.RateLimitRemaining);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1893456000), dataSource.RateLimitReset);
    }
}
