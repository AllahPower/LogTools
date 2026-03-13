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
        var loginPageResponse = await SendGetAsync("login", cancellationToken).ConfigureAwait(false);
        if (!loginPageResponse.IsSuccessStatusCode)
        {
            Logger.LogError("Failed to open login page. StatusCode: {StatusCode}", loginPageResponse.StatusCode);
            throw new AuthenticationFailedException("Failed to open login page.");
        }

        var loginPageContent = await loginPageResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var loginCsrf = ExtractCsrfToken(loginPageContent);

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
            Logger.LogWarning("LogsParser authentication failed for login {Login}.", _credentials.Login);
            throw new AuthenticationFailedException("Login or password is invalid.");
        }

        var authenticatorPageResponse = await SendGetAsync("authenticator", cancellationToken).ConfigureAwait(false);
        if (!authenticatorPageResponse.IsSuccessStatusCode)
        {
            Logger.LogError(
                "Failed to open authenticator page for login {Login}. StatusCode: {StatusCode}",
                _credentials.Login,
                authenticatorPageResponse.StatusCode);
            throw new TwoFactorAuthenticationException("Failed to open authenticator page.");
        }

        var authenticatorPageContent = await authenticatorPageResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var authenticatorCsrf = ExtractCsrfToken(authenticatorPageContent);
        var totpCode = GenerateTotp(_credentials.TotpSecret);

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
            Logger.LogWarning("TOTP code was rejected for login {Login}.", _credentials.Login);
            throw new TwoFactorAuthenticationException("TOTP code was rejected.");
        }

        Logger.LogInformation("LogsParser authentication completed for login {Login}.", _credentials.Login);
    }

    private async Task<HttpResponseMessage> SendGetAsync(string relativeUri, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, relativeUri);
        _cookieStorage.ApplyTo(request.Headers);

        var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        _cookieStorage.UpdateFrom(response.Headers);
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
        var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        _cookieStorage.UpdateFrom(response.Headers);
        return response;
    }

    private static string ExtractCsrfToken(string html)
    {
        var match = CsrfRegex().Match(html);
        if (!match.Success)
        {
            Logger.LogError("CSRF token was not found in response content.");
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
