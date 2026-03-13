namespace LogsParser.Abstractions;

public interface ICookieStorage
{
    IReadOnlyCollection<ParserCookie> GetCookies();

    void SetCookies(IReadOnlyCollection<ParserCookie> cookies);
}
