namespace LogsParser.Models;

public sealed record LogsParserCredentials(string Login, string Password, string TotpSecret);

public sealed record LogsParserHttpOptions
{
    public Uri BaseUri { get; init; } = new("https://arizonarp.logsparser.info/");

    public string UserAgent { get; init; } =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36";

    public string Accept { get; init; } =
        "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7";

    public int MaxRetryAttempts { get; init; } = 5;
}
