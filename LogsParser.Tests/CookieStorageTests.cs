using LogsParser.Abstractions;
using LogsParser.Net;
using Xunit;

namespace LogsParser.Tests;

public class CookieStorageTests
{
    [Fact]
    public void SetCookies_dedups_by_name_and_drops_empty_names()
    {
        var storage = new MemoryCookieStorage();

        storage.SetCookies(
        [
            new ParserCookie("a", "1"),
            new ParserCookie("a", "2"),
            new ParserCookie("", "x"),
            new ParserCookie("b", "3")
        ]);

        var cookies = storage.GetCookies();
        Assert.Equal(2, cookies.Count);
        Assert.Contains(cookies, c => c is { Name: "a", Value: "1" });
        Assert.Contains(cookies, c => c is { Name: "b", Value: "3" });
    }

    [Fact]
    public void UpdateFrom_parses_set_cookie_headers()
    {
        var storage = new MemoryCookieStorage();
        using var response = new HttpResponseMessage();
        response.Headers.TryAddWithoutValidation("Set-Cookie", "arizonarp_session=abc; path=/; httponly");
        response.Headers.TryAddWithoutValidation("Set-Cookie", "XSRF-TOKEN=xyz; path=/");

        storage.UpdateFrom(response.Headers);

        var cookies = storage.GetCookies();
        Assert.Contains(cookies, c => c is { Name: "arizonarp_session", Value: "abc" });
        Assert.Contains(cookies, c => c is { Name: "XSRF-TOKEN", Value: "xyz" });
    }

    [Fact]
    public void Upsert_updates_existing_value()
    {
        var storage = new MemoryCookieStorage();

        storage.Upsert(new ParserCookie("R3ACTLB", "old"));
        storage.Upsert(new ParserCookie("R3ACTLB", "new"));

        var cookies = storage.GetCookies();
        Assert.Single(cookies);
        Assert.Equal("new", cookies.First().Value);
    }

    [Fact]
    public void ApplyTo_serializes_cookie_header()
    {
        var storage = new MemoryCookieStorage();
        storage.SetCookies([new ParserCookie("a", "1"), new ParserCookie("b", "2")]);

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/");
        storage.ApplyTo(request.Headers);

        Assert.True(request.Headers.TryGetValues("Cookie", out var values));
        var header = string.Concat(values!);
        Assert.Contains("a=1", header);
        Assert.Contains("b=2", header);
        Assert.Contains("; ", header);
    }

    [Fact]
    public void ApplyTo_without_cookies_removes_header()
    {
        var storage = new MemoryCookieStorage();
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/");
        request.Headers.TryAddWithoutValidation("Cookie", "stale=1");

        storage.ApplyTo(request.Headers);

        Assert.False(request.Headers.Contains("Cookie"));
    }
}
