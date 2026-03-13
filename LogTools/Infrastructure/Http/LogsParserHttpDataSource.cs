using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using LogsParser.Abstractions;
using LogsParser.Diagnostics;
using LogsParser.Exceptions;
using LogsParser.Models;

namespace LogsParser.Net;

public sealed class LogsParserHttpDataSource : ILogsParserDataSource, IDisposable
{
    private static ILogger Logger => LogsParserLogging.CreateLogger<LogsParserHttpDataSource>();

    private readonly HttpClient _httpClient;
    private readonly ICookieStorage _defaultCookieStorage;
    private readonly LogsParserCredentials? _credentials;
    private readonly LogsParserHttpOptions _options;
    private readonly bool _disposeHttpClient;

    public LogsParserHttpDataSource(
        LogsParserCredentials? credentials = null,
        ICookieStorage? cookieStorage = null,
        LogsParserHttpOptions? options = null,
        HttpClient? httpClient = null,
        ILoggerFactory? loggerFactory = null)
    {
        if (loggerFactory is not null)
        {
            LogsParserLogging.UseLoggerFactory(loggerFactory);
        }

        _options = options ?? new LogsParserHttpOptions();
        _defaultCookieStorage = cookieStorage ?? new MemoryCookieStorage();

        if (httpClient is null)
        {
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.All
            };

            _httpClient = new HttpClient(handler, disposeHandler: true);
            _disposeHttpClient = true;
        }
        else
        {
            _httpClient = httpClient;
        }

        _httpClient.BaseAddress ??= _options.BaseUri;

        if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(_options.UserAgent);
        }

        if (!_httpClient.DefaultRequestHeaders.Accept.Any())
        {
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept", _options.Accept);
        }

        _credentials = credentials;
    }

    public int RateLimitMax { get; private set; }

    public int RateLimitRemaining { get; private set; }

    public async Task<string> GetContentAsync(ParserRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var cookieStorage = request.CookieStorage ?? _defaultCookieStorage;
        var retryAttempt = 0;

        while (retryAttempt < _options.MaxRetryAttempts && !cancellationToken.IsCancellationRequested)
        {
            HttpResponseMessage? response = null;

            try
            {
                using var message = new HttpRequestMessage(HttpMethod.Get, request.RelativeUri);
                cookieStorage.ApplyTo(message.Headers);

                response = await _httpClient.SendAsync(message, cancellationToken).ConfigureAwait(false);
                cookieStorage.UpdateFrom(response.Headers);
                UpdateRateLimits(response.Headers);

                if (response.StatusCode == HttpStatusCode.OK &&
                    await ReactShieldBypass.IsReactChallengeAsync(response, cancellationToken).ConfigureAwait(false))
                {
                    Logger.LogInformation("React challenge detected for request {RelativeUri}.", request.RelativeUri);
                    var reactToken = await ReactShieldBypass.SolveAsync(response, cancellationToken).ConfigureAwait(false);
                    cookieStorage.Upsert(new ParserCookie("R3ACTLB", reactToken));
                    response.Dispose();
                    continue;
                }

                if (response.StatusCode == HttpStatusCode.Found &&
                    response.Headers.Location?.AbsolutePath == "/login")
                {
                    response.Dispose();

                    if (_credentials is null)
                    {
                        Logger.LogWarning("Authentication required for request {RelativeUri}, but credentials are missing.", request.RelativeUri);
                        throw new AuthenticationRequiredException("The service requested authentication, but credentials were not configured.");
                    }

                    Logger.LogInformation("Authentication required for request {RelativeUri}. Starting auth flow.", request.RelativeUri);
                    var authenticator = new LogsParserAuthenticator(_httpClient, cookieStorage, _credentials);
                    await authenticator.AuthenticateAsync(cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (response.StatusCode == HttpStatusCode.Found &&
                    response.Headers.Location?.AbsolutePath == "/profile")
                {
                    Logger.LogError("Account configuration error for request {RelativeUri}. Redirected to /profile.", request.RelativeUri);
                    throw new AccountConfigurationException("The logsparser account is not configured and was redirected to profile.");
                }

                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    var retryAfter = ResolveRetryAfterSeconds(response.Headers, retryAttempt);
                    Logger.LogWarning(
                        "Rate limit exceeded for request {RelativeUri}. RetryAfterSeconds: {RetryAfterSeconds}. Attempt: {Attempt}",
                        request.RelativeUri,
                        retryAfter,
                        retryAttempt + 1);
                    response.Dispose();
                    retryAttempt++;

                    if (retryAttempt >= _options.MaxRetryAttempts)
                    {
                        throw new RateLimitExceededException("Rate limit exceeded.", retryAfter);
                    }

                    await Task.Delay(TimeSpan.FromSeconds(retryAfter), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    Logger.LogError(
                        "Request {RelativeUri} failed with status code {StatusCode}.",
                        request.RelativeUri,
                        response.StatusCode);
                    throw new LogsParserHttpException(
                        $"Request to '{request.RelativeUri}' failed with status code {(int)response.StatusCode} ({response.StatusCode}).");
                }

                Logger.LogDebug("Request {RelativeUri} completed successfully.", request.RelativeUri);
                return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (LogsParserException)
            {
                response?.Dispose();
                throw;
            }
            catch (OperationCanceledException)
            {
                response?.Dispose();
                throw;
            }
            catch (Exception exception)
            {
                response?.Dispose();
                retryAttempt++;

                if (retryAttempt >= _options.MaxRetryAttempts)
                {
                    Logger.LogError(
                        exception,
                        "Request {RelativeUri} failed after {MaxRetryAttempts} attempts.",
                        request.RelativeUri,
                        _options.MaxRetryAttempts);
                    throw new LogsParserHttpException(
                        $"Request to '{request.RelativeUri}' failed after {_options.MaxRetryAttempts} attempts.",
                        exception);
                }

                Logger.LogWarning(
                    exception,
                    "Transient failure for request {RelativeUri}. Retrying attempt {Attempt}/{MaxRetryAttempts}.",
                    request.RelativeUri,
                    retryAttempt,
                    _options.MaxRetryAttempts);
                var delay = TimeSpan.FromSeconds(Math.Pow(2, retryAttempt));
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }

        throw new OperationCanceledException("The request was canceled.");
    }

    public void Dispose()
    {
        if (_disposeHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private void UpdateRateLimits(HttpResponseHeaders headers)
    {
        if (headers.TryGetValues("X-Ratelimit-Limit", out var rateLimitValues) &&
            int.TryParse(rateLimitValues.FirstOrDefault(), out var rateLimitMax))
        {
            RateLimitMax = rateLimitMax;
        }

        if (headers.TryGetValues("X-Ratelimit-Remaining", out var remainingValues) &&
            int.TryParse(remainingValues.FirstOrDefault(), out var remaining))
        {
            RateLimitRemaining = remaining;
        }
    }

    private static int ResolveRetryAfterSeconds(HttpResponseHeaders headers, int retryAttempt)
    {
        if (headers.TryGetValues("Retry-After", out var values) &&
            int.TryParse(values.FirstOrDefault(), out var retryAfter) &&
            retryAfter > 0)
        {
            return retryAfter;
        }

        return (int)Math.Max(1, Math.Pow(2, retryAttempt + 1));
    }
}
