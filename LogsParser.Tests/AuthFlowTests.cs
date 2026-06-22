using System.Net;
using LogsParser.Exceptions;
using LogsParser.Models;
using Xunit;

namespace LogsParser.Tests;

public class AuthFlowTests
{
    // RFC 6238 sample secret ("12345678901234567890" in Base32) — any valid Base32 works for the flow.
    private static readonly LogsParserCredentials Credentials =
        new("Nick_Name", "secret", "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ");

    [Fact]
    public async Task Data_request_with_lapsed_two_factor_reconfirms_and_returns_content()
    {
        // Regression: the session still holds the first factor, but its 2FA window lapsed, so the
        // server answers data requests with 302 → /authenticator (NOT /login). Before the fix this
        // surfaced as "failed with status code 302 (Found)".
        var server = new FakeLogsParserServer { LoggedIn = true, TwoFactorConfirmed = false };
        var handler = new FakeHttpMessageHandler(server.Handle);
        using var dataSource = TestDataSource.Create(handler, Credentials);

        var content = await dataSource.GetContentAsync(new ParserRequest("/admins?min_period=2026-05-01%2000:00:00"));

        Assert.Contains("DATA-OK", content);
        // The TOTP step ran...
        Assert.Contains(handler.Requests, r => r is { Path: "/authenticator", Method.Method: "POST" });
        // ...but the first factor (login) was NOT re-submitted.
        Assert.DoesNotContain(handler.Requests, r => r is { Path: "/login", Method.Method: "POST" });
    }

    [Fact]
    public async Task Full_login_flow_runs_login_then_two_factor()
    {
        var server = new FakeLogsParserServer();
        var handler = new FakeHttpMessageHandler(server.Handle);
        using var dataSource = TestDataSource.Create(handler, Credentials);

        var content = await dataSource.GetContentAsync(new ParserRequest("/admins"));

        Assert.Contains("DATA-OK", content);
        Assert.Contains(handler.Requests, r => r is { Path: "/login", Method.Method: "POST" });

        var totpPost = handler.Requests.First(r => r is { Path: "/authenticator", Method.Method: "POST" });
        Assert.Matches(@"(^|&)code=\d{6}(&|$)", totpPost.Body);
        Assert.Contains($"_token={FakeLogsParserServer.Csrf}", totpPost.Body);
    }

    [Fact]
    public async Task Invalid_credentials_throw_authentication_failed()
    {
        var server = new FakeLogsParserServer { AcceptCredentials = false };
        var handler = new FakeHttpMessageHandler(server.Handle);
        using var dataSource = TestDataSource.Create(handler, Credentials);

        await Assert.ThrowsAsync<AuthenticationFailedException>(
            () => dataSource.GetContentAsync(new ParserRequest("/admins")));
    }

    [Fact]
    public async Task Rejected_totp_throws_two_factor_exception()
    {
        var server = new FakeLogsParserServer { AcceptTotp = false };
        var handler = new FakeHttpMessageHandler(server.Handle);
        using var dataSource = TestDataSource.Create(handler, Credentials);

        await Assert.ThrowsAsync<TwoFactorAuthenticationException>(
            () => dataSource.GetContentAsync(new ParserRequest("/admins")));
    }

    [Fact]
    public async Task Lapsed_two_factor_without_credentials_throws_authentication_required()
    {
        var server = new FakeLogsParserServer { LoggedIn = true, TwoFactorConfirmed = false };
        var handler = new FakeHttpMessageHandler(server.Handle);
        using var dataSource = TestDataSource.Create(handler, credentials: null);

        await Assert.ThrowsAsync<AuthenticationRequiredException>(
            () => dataSource.GetContentAsync(new ParserRequest("/admins")));
    }

    [Fact]
    public async Task Login_redirect_without_credentials_throws_authentication_required()
    {
        var server = new FakeLogsParserServer();
        var handler = new FakeHttpMessageHandler(server.Handle);
        using var dataSource = TestDataSource.Create(handler, credentials: null);

        await Assert.ThrowsAsync<AuthenticationRequiredException>(
            () => dataSource.GetContentAsync(new ParserRequest("/admins")));
    }

    [Fact]
    public async Task Profile_redirect_throws_account_configuration()
    {
        var handler = new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Redirect("/profile"));
        using var dataSource = TestDataSource.Create(handler, Credentials);

        await Assert.ThrowsAsync<AccountConfigurationException>(
            () => dataSource.GetContentAsync(new ParserRequest("/admins")));
    }

    [Fact]
    public async Task Authenticated_session_returns_content_without_auth_roundtrips()
    {
        var server = new FakeLogsParserServer { LoggedIn = true, TwoFactorConfirmed = true };
        var handler = new FakeHttpMessageHandler(server.Handle);
        using var dataSource = TestDataSource.Create(handler, Credentials);

        var content = await dataSource.GetContentAsync(new ParserRequest("/admins"));

        Assert.Contains("DATA-OK", content);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Unhandled_error_status_throws_http_exception()
    {
        var handler = new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Status(HttpStatusCode.InternalServerError));
        var options = new LogsParserHttpOptions { MaxRetryAttempts = 1 };
        using var dataSource = TestDataSource.Create(handler, credentials: null, options: options);

        await Assert.ThrowsAsync<LogsParserHttpException>(
            () => dataSource.GetContentAsync(new ParserRequest("/admins")));
    }
}
