using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging;
using LogsParser.Abstractions;
using LogsParser.Diagnostics;

namespace LogsParser.Net;

internal static class CookieStorageExtensions
{
    private static ILogger Logger => LogsParserLogging.CreateLogger(nameof(CookieStorageExtensions));

    public static void ApplyTo(this ICookieStorage storage, HttpRequestHeaders headers)
    {
        var cookies = storage.GetCookies();
        if (cookies.Count == 0)
        {
            headers.Remove("Cookie");
            return;
        }

        headers.Remove("Cookie");
        headers.TryAddWithoutValidation("Cookie", Serialize(cookies));

        Logger.LogTrace("Cookies applied to request: {CookieCount} cookies [{CookieNames}]",
            cookies.Count, string.Join(", ", cookies.Select(static c => c.Name)));
    }

    public static void UpdateFrom(this ICookieStorage storage, HttpResponseHeaders headers)
    {
        if (!headers.TryGetValues("Set-Cookie", out var values))
        {
            return;
        }

        var current = storage.GetCookies()
            .ToDictionary(static cookie => cookie.Name, static cookie => cookie.Value, StringComparer.Ordinal);

        var updatedCount = 0;
        foreach (var rawHeader in values)
        {
            var cookie = ParseSetCookie(rawHeader);
            if (cookie is not null)
            {
                var isNew = !current.ContainsKey(cookie.Value.Key);
                current[cookie.Value.Key] = cookie.Value.Value;
                updatedCount++;

                Logger.LogTrace("Cookie {Action}: {CookieName}", isNew ? "added" : "updated", cookie.Value.Key);
            }
        }

        if (updatedCount > 0)
        {
            storage.SetCookies(current.Select(static pair => new ParserCookie(pair.Key, pair.Value)).ToArray());
            Logger.LogTrace("Cookie storage updated: {UpdatedCount} cookies changed, {TotalCount} total", updatedCount, current.Count);
        }
    }

    public static void Upsert(this ICookieStorage storage, ParserCookie cookie)
    {
        var cookies = storage.GetCookies()
            .ToDictionary(static existing => existing.Name, static existing => existing.Value, StringComparer.Ordinal);

        var isNew = !cookies.ContainsKey(cookie.Name);
        cookies[cookie.Name] = cookie.Value;
        storage.SetCookies(cookies.Select(static pair => new ParserCookie(pair.Key, pair.Value)).ToArray());

        Logger.LogTrace("Cookie upsert: {Action} {CookieName} ({TotalCount} total)", isNew ? "added" : "updated", cookie.Name, cookies.Count);
    }

    private static string Serialize(IReadOnlyCollection<ParserCookie> cookies)
    {
        var builder = new StringBuilder();
        var first = true;

        foreach (var cookie in cookies)
        {
            if (!first)
            {
                builder.Append("; ");
            }

            builder.Append(cookie.Name);
            builder.Append('=');
            builder.Append(cookie.Value);
            first = false;
        }

        return builder.ToString();
    }

    private static KeyValuePair<string, string>? ParseSetCookie(string setCookie)
    {
        if (string.IsNullOrWhiteSpace(setCookie))
        {
            return null;
        }

        var segments = setCookie.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return null;
        }

        var separatorIndex = segments[0].IndexOf('=');
        if (separatorIndex <= 0)
        {
            return null;
        }

        var name = segments[0][..separatorIndex];
        var value = segments[0][(separatorIndex + 1)..];
        return new KeyValuePair<string, string>(name, value);
    }
}
