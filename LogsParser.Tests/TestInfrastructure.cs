using System.Net;
using LogsParser.Abstractions;
using LogsParser.Models;
using LogsParser.Net;

namespace LogsParser.Tests;

/// <summary>A single request captured by <see cref="FakeHttpMessageHandler"/>.</summary>
internal sealed record RecordedRequest(HttpMethod Method, string Path, string Query, string Body, string Cookie);

/// <summary>
/// In-memory <see cref="HttpMessageHandler"/> that records every outgoing request and answers via a
/// caller-supplied responder. Lets the auth/data flow be exercised without touching the network.
/// </summary>
internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    public static readonly Uri BaseUri = new("https://arizonarp.logsparser.info/");

    private readonly Func<RecordedRequest, HttpResponseMessage> _responder;

    public FakeHttpMessageHandler(Func<RecordedRequest, HttpResponseMessage> responder)
    {
        _responder = responder;
    }

    public List<RecordedRequest> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        var cookie = request.Headers.TryGetValues("Cookie", out var values)
            ? string.Join("; ", values)
            : string.Empty;

        var recorded = new RecordedRequest(request.Method, request.RequestUri!.AbsolutePath, request.RequestUri!.Query, body, cookie);
        Requests.Add(recorded);

        var response = _responder(recorded);
        response.RequestMessage = request;
        return response;
    }

    public static HttpResponseMessage Ok(string content, params (string Name, string Value)[] headers)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(content)
        };

        foreach (var (name, value) in headers)
        {
            response.Headers.TryAddWithoutValidation(name, value);
        }

        return response;
    }

    public static HttpResponseMessage Redirect(string location)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Found);
        response.Headers.Location = new Uri(BaseUri, location);
        return response;
    }

    public static HttpResponseMessage Status(HttpStatusCode status, params (string Name, string Value)[] headers)
    {
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent(string.Empty)
        };

        foreach (var (name, value) in headers)
        {
            response.Headers.TryAddWithoutValidation(name, value);
        }

        return response;
    }
}

internal static class TestDataSource
{
    public static LogsParserHttpDataSource Create(
        FakeHttpMessageHandler handler,
        LogsParserCredentials? credentials = null,
        LogsParserHttpOptions? options = null,
        ICookieStorage? cookies = null)
    {
        var httpClient = new HttpClient(handler);
        return new LogsParserHttpDataSource(credentials, cookies, options, httpClient);
    }
}

/// <summary>
/// Stateful test double of the LogsParser site session: models the first factor (login cookie) and
/// the second factor (TOTP confirmation) so the whole authentication flow — including a mid-session
/// 2FA lapse that redirects to <c>/authenticator</c> — can be reproduced deterministically.
/// </summary>
internal sealed class FakeLogsParserServer
{
    public const string Csrf = "csrf-token-123";

    public bool LoggedIn { get; set; }

    public bool TwoFactorConfirmed { get; set; }

    public bool AcceptCredentials { get; set; } = true;

    public bool AcceptTotp { get; set; } = true;

    public string DataContent { get; set; } = "<html><body>DATA-OK</body></html>";

    public HttpResponseMessage Handle(RecordedRequest request)
    {
        return request switch
        {
            { Path: "/login", Method.Method: "GET" } => FakeHttpMessageHandler.Ok(Page()),
            { Path: "/login", Method.Method: "POST" } => HandleLoginPost(),
            { Path: "/authenticator", Method.Method: "GET" } => HandleAuthenticatorGet(),
            { Path: "/authenticator", Method.Method: "POST" } => HandleAuthenticatorPost(),
            _ => HandleData()
        };
    }

    private HttpResponseMessage HandleLoginPost()
    {
        if (!AcceptCredentials)
        {
            return FakeHttpMessageHandler.Redirect("/login");
        }

        LoggedIn = true;
        return FakeHttpMessageHandler.Redirect("/");
    }

    private HttpResponseMessage HandleAuthenticatorGet()
    {
        return LoggedIn
            ? FakeHttpMessageHandler.Ok(Page())
            : FakeHttpMessageHandler.Redirect("/login");
    }

    private HttpResponseMessage HandleAuthenticatorPost()
    {
        if (!AcceptTotp)
        {
            return FakeHttpMessageHandler.Redirect("/authenticator");
        }

        TwoFactorConfirmed = true;
        return FakeHttpMessageHandler.Redirect("/");
    }

    private HttpResponseMessage HandleData()
    {
        if (!LoggedIn)
        {
            return FakeHttpMessageHandler.Redirect("/login");
        }

        if (!TwoFactorConfirmed)
        {
            return FakeHttpMessageHandler.Redirect("/authenticator");
        }

        return FakeHttpMessageHandler.Ok(DataContent);
    }

    private static string Page() =>
        $"<html><head><meta name=\"csrf-token\" content=\"{Csrf}\"></head><body>form</body></html>";
}
