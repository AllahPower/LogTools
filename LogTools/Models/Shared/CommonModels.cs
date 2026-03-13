using LogsParser.Abstractions;

namespace LogsParser;

public sealed record ParserCookie(string Name, string Value);

public sealed record ParserRequest(string RelativeUri, ICookieStorage? CookieStorage = null);
