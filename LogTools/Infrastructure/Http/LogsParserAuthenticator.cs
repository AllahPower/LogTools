using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using LogsParser.Abstractions;
using LogsParser.Diagnostics;
using LogsParser.Exceptions;
using LogsParser.Models;

namespace LogsParser.Net;

internal sealed partial class LogsParserAuthenticator
{
    private static ILogger Logger => LogsParserLogging.CreateLogger<LogsParserAuthenticator>();

    private readonly HttpClient _httpClient;
    private readonly ICookieStorage _cookieStorage;
    private readonly LogsParserCredentials _credentials;

    public LogsParserAuthenticator(HttpClient httpClient, ICookieStorage cookieStorage, LogsParserCredentials credentials)
    {
        _httpClient = httpClient;
        _cookieStorage = cookieStorage;
        _credentials = credentials;
    }

    public async Task AuthenticateAsync(CancellationToken cancellationToken)
    {
        Logger.LogInformation("Starting authentication flow for login {Login}", _credentials.Login);

        Logger.LogDebug("Step 1/4: GET /login — loading login page");
        var loginPageResponse = await SendGetAsync("login", cancellationToken).ConfigureAwait(false);
        if (!loginPageResponse.IsSuccessStatusCode)
        {
            Logger.LogError("Failed to load login page: StatusCode={StatusCode}", (int)loginPageResponse.StatusCode);
            throw new AuthenticationFailedException("Failed to open login page.");
        }

        var loginPageContent = await loginPageResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var loginCsrf = ExtractCsrfToken(loginPageContent);
        Logger.LogTrace("CSRF token extracted from login page ({TokenLength} chars)", loginCsrf.Length);

        Logger.LogDebug("Step 2/4: POST /login — submitting credentials for {Login}", _credentials.Login);
        var loginResponse = await SendPostAsync(
            "login",
            new Dictionary<string, string>
            {
                ["_token"] = loginCsrf,
                ["name"] = _credentials.Login,
                ["password"] = _credentials.Password
            },
            cancellationToken).ConfigureAwait(false);

        if (loginResponse.Headers.Location?.AbsolutePath == "/login")
        {
            Logger.LogWarning("Authentication failed for {Login}: redirected back to /login (invalid credentials)", _credentials.Login);
            throw new AuthenticationFailedException("Login or password is invalid.");
        }

        Logger.LogDebug("Step 2/4: login accepted, redirect to {Location}", loginResponse.Headers.Location?.AbsolutePath);

        Logger.LogDebug("Step 3/4: GET /authenticator — loading 2FA page");
        var authenticatorPageResponse = await SendGetAsync("authenticator", cancellationToken).ConfigureAwait(false);
        if (!authenticatorPageResponse.IsSuccessStatusCode)
        {
            Logger.LogError(
                "Failed to load authenticator page for {Login}: StatusCode={StatusCode}",
                _credentials.Login,
                (int)authenticatorPageResponse.StatusCode);
            throw new TwoFactorAuthenticationException("Failed to open authenticator page.");
        }

        var authenticatorPageContent = await authenticatorPageResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var authenticatorCsrf = ExtractCsrfToken(authenticatorPageContent);
        Logger.LogTrace("CSRF token extracted from authenticator page ({TokenLength} chars)", authenticatorCsrf.Length);

        Logger.LogDebug("Step 4/4: POST /authenticator — submitting TOTP code for {Login}", _credentials.Login);
        var totpCode = GenerateTotp(_credentials.TotpSecret);
        Logger.LogTrace("TOTP code generated ({CodeLength} digits)", totpCode.Length);

        var authenticatorResponse = await SendPostAsync(
            "authenticator",
            new Dictionary<string, string>
            {
                ["_token"] = authenticatorCsrf,
                ["code"] = totpCode
            },
            cancellationToken).ConfigureAwait(false);

        if (authenticatorResponse.Headers.Location?.AbsolutePath == "/authenticator")
        {
            Logger.LogWarning("TOTP code rejected for {Login}: redirected back to /authenticator", _credentials.Login);
            throw new TwoFactorAuthenticationException("TOTP code was rejected.");
        }

        Logger.LogInformation("Authentication completed successfully for {Login}", _credentials.Login);
    }

    private async Task<HttpResponseMessage> SendGetAsync(string relativeUri, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, relativeUri);
        _cookieStorage.ApplyTo(request.Headers);

        Logger.LogTrace("Auth GET {RelativeUri} (cookies: {CookieCount})", relativeUri, _cookieStorage.GetCookies().Count);
        var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        _cookieStorage.UpdateFrom(response.Headers);

        Logger.LogTrace("Auth GET {RelativeUri} → {StatusCode}", relativeUri, (int)response.StatusCode);
        return response;
    }

    private async Task<HttpResponseMessage> SendPostAsync(
        string relativeUri,
        IReadOnlyDictionary<string, string> data,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, relativeUri)
        {
            Content = new FormUrlEncodedContent(data)
        };

        _cookieStorage.ApplyTo(request.Headers);

        Logger.LogTrace("Auth POST {RelativeUri} (cookies: {CookieCount}, fields: {FieldCount})",
            relativeUri, _cookieStorage.GetCookies().Count, data.Count);
        var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        _cookieStorage.UpdateFrom(response.Headers);

        Logger.LogTrace("Auth POST {RelativeUri} → {StatusCode}, Location={Location}",
            relativeUri, (int)response.StatusCode, response.Headers.Location?.AbsolutePath);
        return response;
    }

    private static string ExtractCsrfToken(string html)
    {
        var match = CsrfRegex().Match(html);
        if (!match.Success)
        {
            Logger.LogError("CSRF token not found in HTML response ({ContentLength} chars)", html.Length);
            throw new CsrfTokenNotFoundException("CSRF token was not found in the response.");
        }

        return match.Groups["token"].Value;
    }

    private static string GenerateTotp(string secret)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new TwoFactorAuthenticationException("TOTP secret is empty.");
        }

        var key = DecodeBase32(secret);
        var timestamp = GetJuneauTimestamp();
        var timestep = timestamp / 30;
        Span<byte> counter = stackalloc byte[8];

        for (var index = 7; index >= 0; index--)
        {
            counter[index] = (byte)(timestep & 0xFF);
            timestep >>= 8;
        }

        using var hmac = new HMACSHA1(key);
        var hash = hmac.ComputeHash(counter.ToArray());
        var offset = hash[^1] & 0x0F;

        var binaryCode =
            ((hash[offset] & 0x7F) << 24) |
            (hash[offset + 1] << 16) |
            (hash[offset + 2] << 8) |
            hash[offset + 3];

        return (binaryCode % 1_000_000).ToString("D6");
    }

    private static long GetJuneauTimestamp()
    {
        var utcNow = DateTimeOffset.UtcNow;
        var juneauTimeZone = TryResolveTimeZone("America/Juneau", "Alaskan Standard Time");
        var juneauTime = TimeZoneInfo.ConvertTime(utcNow, juneauTimeZone);
        return juneauTime.ToUnixTimeSeconds();
    }

    private static TimeZoneInfo TryResolveTimeZone(params string[] ids)
    {
        foreach (var id in ids)
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
            }
        }

        return TimeZoneInfo.Utc;
    }

    private static byte[] DecodeBase32(string input)
    {
        var sanitized = input.Trim().TrimEnd('=').ToUpperInvariant();
        var output = new List<byte>(sanitized.Length * 5 / 8);
        var buffer = 0;
        var bitsLeft = 0;

        foreach (var character in sanitized)
        {
            var value = character switch
            {
                >= 'A' and <= 'Z' => character - 'A',
                >= '2' and <= '7' => character - '2' + 26,
                _ => throw new TwoFactorAuthenticationException("TOTP secret contains invalid Base32 symbols.")
            };

            buffer = (buffer << 5) | value;
            bitsLeft += 5;

            if (bitsLeft < 8)
            {
                continue;
            }

            output.Add((byte)(buffer >> (bitsLeft - 8)));
            bitsLeft -= 8;
            buffer &= (1 << bitsLeft) - 1;
        }

        return output.ToArray();
    }

    [GeneratedRegex("""<meta\s+name=["']csrf-token["']\s+content=["'](?<token>[^"']+)["']""", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex CsrfRegex();
}
