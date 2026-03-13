namespace LogsParser.Abstractions;

public sealed class MemoryCookieStorage : ICookieStorage
{
    private readonly object _sync = new();
    private List<ParserCookie> _cookies = [];

    public IReadOnlyCollection<ParserCookie> GetCookies()
    {
        lock (_sync)
        {
            return _cookies.ToArray();
        }
    }

    public void SetCookies(IReadOnlyCollection<ParserCookie> cookies)
    {
        ArgumentNullException.ThrowIfNull(cookies);

        lock (_sync)
        {
            _cookies = cookies
                .Where(static cookie => !string.IsNullOrWhiteSpace(cookie.Name))
                .DistinctBy(static cookie => cookie.Name, StringComparer.Ordinal)
                .ToList();
        }
    }
}
