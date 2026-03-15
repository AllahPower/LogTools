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

        Logger.LogDebug(
            "LogsParserHttpDataSource initialized: BaseUri={BaseUri}, HasCredentials={HasCredentials}, MaxRetryAttempts={MaxRetryAttempts}, CookieStorage={CookieStorageType}, HttpClient={HttpClientSource}",
            _options.BaseUri,
            _credentials is not null,
            _options.MaxRetryAttempts,
            _defaultCookieStorage.GetType().Name,
            httpClient is null ? "internal" : "external");
    }

    public int RateLimitMax { get; private set; }

    public int RateLimitRemaining { get; private set; }

    public async Task<string> GetContentAsync(ParserRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var cookieStorage = request.CookieStorage ?? _defaultCookieStorage;
        var retryAttempt = 0;

        Logger.LogDebug("GetContentAsync: requesting {RelativeUri}", request.RelativeUri);

        while (retryAttempt < _options.MaxRetryAttempts && !cancellationToken.IsCancellationRequested)
        {
            HttpResponseMessage? response = null;

            try
            {
                using var message = new HttpRequestMessage(HttpMethod.Get, request.RelativeUri);
                cookieStorage.ApplyTo(message.Headers);

                Logger.LogTrace("Sending GET {RelativeUri} (attempt {Attempt}/{MaxAttempts})",
                    request.RelativeUri, retryAttempt + 1, _options.MaxRetryAttempts);

                response = await _httpClient.SendAsync(message, cancellationToken).ConfigureAwait(false);
                cookieStorage.UpdateFrom(response.Headers);
                UpdateRateLimits(response.Headers);

                Logger.LogTrace("Response {RelativeUri}: StatusCode={StatusCode}, RateLimit={Remaining}/{Max}",
                    request.RelativeUri, (int)response.StatusCode, RateLimitRemaining, RateLimitMax);

                if (response.StatusCode == HttpStatusCode.OK &&
                    await ReactShieldBypass.IsReactChallengeAsync(response, cancellationToken).ConfigureAwait(false))
                {
                    Logger.LogInformation("React challenge detected for {RelativeUri}, solving...", request.RelativeUri);
                    var reactToken = await ReactShieldBypass.SolveAsync(response, cancellationToken).ConfigureAwait(false);
                    cookieStorage.Upsert(new ParserCookie("R3ACTLB", reactToken));
                    Logger.LogDebug("React challenge solved for {RelativeUri}, retrying request", request.RelativeUri);
                    response.Dispose();
                    continue;
                }

                if (response.StatusCode == HttpStatusCode.Found &&
                    response.Headers.Location?.AbsolutePath == "/login")
                {
                    response.Dispose();

                    if (_credentials is null)
                    {
                        Logger.LogWarning("Authentication required for {RelativeUri} but no credentials configured", request.RelativeUri);
                        throw new AuthenticationRequiredException("The service requested authentication, but credentials were not configured.");
                    }

                    Logger.LogInformation("Authentication required for {RelativeUri}, starting auth flow...", request.RelativeUri);
                    var authenticator = new LogsParserAuthenticator(_httpClient, cookieStorage, _credentials);
                    await authenticator.AuthenticateAsync(cancellationToken).ConfigureAwait(false);
                    Logger.LogDebug("Auth flow completed for {RelativeUri}, retrying request", request.RelativeUri);
                    continue;
                }

                if (response.StatusCode == HttpStatusCode.Found &&
                    response.Headers.Location?.AbsolutePath == "/profile")
                {
                    Logger.LogError("Account not configured: redirected to /profile for {RelativeUri}", request.RelativeUri);
                    throw new AccountConfigurationException("The logsparser account is not configured and was redirected to profile.");
                }

                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    var retryAfter = ResolveRetryAfterSeconds(response.Headers, retryAttempt);
                    Logger.LogWarning(
                        "Rate limit exceeded for {RelativeUri}: retrying in {RetryAfterSeconds}s (attempt {Attempt}/{MaxAttempts})",
                        request.RelativeUri, retryAfter, retryAttempt + 1, _options.MaxRetryAttempts);
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
                    Logger.LogError("Request failed: {RelativeUri} returned {StatusCode}", request.RelativeUri, (int)response.StatusCode);
                    throw new LogsParserHttpException(
                        $"Request to '{request.RelativeUri}' failed with status code {(int)response.StatusCode} ({response.StatusCode}).");
                }

                var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                response.Dispose();
                Logger.LogDebug("Request completed: {RelativeUri}, {ContentLength} characters", request.RelativeUri, content.Length);
                return content;
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
                        "Request failed after {MaxRetryAttempts} attempts: {RelativeUri}",
                        _options.MaxRetryAttempts,
                        request.RelativeUri);
                    throw new LogsParserHttpException(
                        $"Request to '{request.RelativeUri}' failed after {_options.MaxRetryAttempts} attempts.",
                        exception);
                }

                var delay = TimeSpan.FromSeconds(Math.Pow(2, retryAttempt));
                Logger.LogWarning(
                    exception,
                    "Transient failure for {RelativeUri}: retrying in {DelaySeconds}s (attempt {Attempt}/{MaxAttempts})",
                    request.RelativeUri,
                    delay.TotalSeconds,
                    retryAttempt,
                    _options.MaxRetryAttempts);
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }

        throw new OperationCanceledException("The request was canceled.");
    }

    public void Dispose()
    {
        if (_disposeHttpClient)
        {
            Logger.LogTrace("Disposing internal HttpClient");
            _httpClient.Dispose();
        }
    }

    private void UpdateRateLimits(HttpResponseHeaders headers)
    {
        var previousRemaining = RateLimitRemaining;

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

        if (previousRemaining != RateLimitRemaining)
        {
            Logger.LogTrace("Rate limit updated: {Remaining}/{Max}", RateLimitRemaining, RateLimitMax);
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
