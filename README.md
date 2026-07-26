![LogsParser logo](https://raw.githubusercontent.com/AllahPower/LogTools/main/logo.png)

# LogsParser

[![English](https://img.shields.io/badge/EN-🇺🇸-informational)](README.md) [![Русский](https://img.shields.io/badge/RU-🇷🇺-informational)](README.ru.md)

`LogsParser` is a .NET library for working with `arizonarp.logsparser.info`.

It is designed as a reusable integration library first:
- parse logs, admin activity, and top operations
- authenticate with login, password, and TOTP 2FA
- bypass the React anti-DDoS challenge automatically
- expose current account information from page layout
- expose the current filters catalog and `dynamic[n]` additional parameters
- keep cookie persistence under caller control
- work both manually and through DI

## Features

- `LogsParserClient` for high-level operations
- `LogsParserHttpDataSource` for HTTP transport
- `LogsHtmlParser` for raw HTML parsing
- `LogsRequestUriBuilder` for low-level request generation
- `ICookieStorage` for caller-owned cookie persistence
- automatic login flow
- TOTP 2FA support
- automatic React challenge bypass
- retry and rate-limit handling with `X-Ratelimit-Reset` support
- DI registration extensions
- `Microsoft.Extensions.Logging` integration
- domain-specific exceptions

## Target Frameworks

- `.NET 7`, `.NET 8`, `.NET 9`, `.NET 10`
- C# `latest`

## Project Structure

```text
LogTools/
  Client/
  Contracts/
  DependencyInjection/
  Diagnostics/
  Exceptions/
  Infrastructure/
  Models/
  Parsing/
```

Main entry points:
- `LogsParserClient`
- `LogsParserHttpDataSource`
- `LogsHtmlParser`
- `LogsParserServiceCollectionExtensions`

## Installation

NuGet:

```
dotnet add package LogsParser
```

Or via `ProjectReference`:

```xml
<ItemGroup>
  <ProjectReference Include="..\LogTools\LogTools.csproj" />
</ItemGroup>
```

## Quick Start

### Manual Mode

```csharp
using LogsParser;
using LogsParser.Abstractions;
using LogsParser.Models;
using LogsParser.Net;

var cookies = new MemoryCookieStorage();

using var dataSource = new LogsParserHttpDataSource(
    credentials: new LogsParserCredentials(
        Login: "my_login",
        Password: "my_password",
        TotpSecret: "BASE32SECRET"),
    cookieStorage: cookies,
    options: new LogsParserHttpOptions
    {
        BaseUri = new Uri("https://arizonarp.logsparser.info/")
    });

var client = new LogsParserClient(dataSource);

var logs = await client.GetLogsAsync(new LogsQuery(
    ServerId: 201,
    Filters: ["warn", "mute"],
    PeriodFrom: DateTime.UtcNow.AddDays(-1),
    PeriodTo: DateTime.UtcNow,
    Limit: 1000));
```

### DI Mode

```csharp
using LogsParser.DependencyInjection;
using LogsParser.Models;

services.AddLogging();

services.AddLogsParser(options =>
{
    options.Credentials = new LogsParserCredentials(
        Login: "my_login",
        Password: "my_password",
        TotpSecret: "BASE32SECRET");

    options.HttpOptions = new LogsParserHttpOptions
    {
        BaseUri = new Uri("https://arizonarp.logsparser.info/")
    };
});
```

## Authentication

`LogsParserHttpDataSource` can perform the full authentication flow automatically:

1. open `/login`
2. extract CSRF token
3. submit login and password
4. open `/authenticator`
5. extract CSRF token
6. generate TOTP code
7. submit 2FA code
8. continue the original request

If an already authenticated session keeps its login cookie but its 2FA window lapses, the site
redirects data requests to `/authenticator` (not `/login`). In that case only the second-factor
confirmation (steps 4–7) is re-run, without re-submitting the password.

Credentials model:

```csharp
new LogsParserCredentials(
    Login: "my_login",
    Password: "my_password",
    TotpSecret: "BASE32SECRET");
```

### Getting `TotpSecret`

If the account is protected by TOTP and you do not have the raw secret yet:

1. Open Google Authenticator → **Transfer accounts** → **Export accounts**
2. Scan the generated QR code with any QR reader to get the `otpauth-migration://offline?data=...` URI
3. Decode the URI using [`otpauth-migration-decoder`](https://github.com/digitalduke/otpauth-migration-decoder):
   ```bash
   python decoder.py decode --migration "otpauth-migration://offline?data=..."
   ```
4. The output will contain standard `otpauth://totp/...?secret=BASE32SECRET&...` links — use the `secret` parameter value as `TotpSecret`

The library does not extract the secret for you. It only consumes the final Base32 secret string.

If credentials are not configured and the service redirects to `/login`, the library throws `AuthenticationRequiredException`.

## React Challenge Bypass

The transport layer includes automatic anti-DDoS handling:
- challenge detection
- payload extraction from the page script
- token derivation
- `R3ACTLB` cookie update
- retry of the original request

This behavior is internal to `LogsParserHttpDataSource`.

## Rate Limiting

`LogsParserHttpDataSource` tracks rate limit headers from the server:

| Property | Header | Description |
|---|---|---|
| `RateLimitMax` | `X-Ratelimit-Limit` | Maximum requests allowed |
| `RateLimitRemaining` | `X-Ratelimit-Remaining` | Remaining requests in current window |
| `RateLimitReset` | `X-Ratelimit-Reset` | Exact time when the limit resets (Unix timestamp) |

### Behavior on 429

When a `429 Too Many Requests` response is received, the behavior depends on the `WaitForRateLimitReset` option:

| `WaitForRateLimitReset` | Behavior |
|---|---|
| `true` (default) | Waits until the exact reset time from `X-Ratelimit-Reset` (+1s margin), then retries. This wait does **not** count as a retry attempt. Falls back to `Retry-After` / exponential backoff if the header is missing. |
| `false` | Immediately throws `RateLimitExceededException` with `ResetAt` populated so the caller can decide when to retry. |

Configuration:

```csharp
var options = new LogsParserHttpOptions
{
    WaitForRateLimitReset = true // default - wait for reset automatically
};
```

Handling the exception when `WaitForRateLimitReset = false`:

```csharp
catch (RateLimitExceededException ex)
{
    Console.WriteLine($"Retry after: {ex.RetryAfterSeconds}s");

    if (ex.ResetAt is { } resetAt)
    {
        Console.WriteLine($"Limit resets at: {resetAt:u}");
    }
}
```

## Cookie Storage

The library does not own cookie persistence.

It uses the `ICookieStorage` contract:

```csharp
public interface ICookieStorage
{
    IReadOnlyCollection<ParserCookie> GetCookies();
    void SetCookies(IReadOnlyCollection<ParserCookie> cookies);
}
```

Default implementation:
- `MemoryCookieStorage`

If cookies must survive process restarts, implement your own storage:
- file-based storage
- database storage
- encrypted secrets storage
- distributed cache

## High-Level API

### Get Logs

```csharp
var result = await client.GetLogsAsync(new LogsQuery(
    ServerId: 201,
    Filters: ["warn", "mute"],
    PeriodFrom: DateTime.UtcNow.AddDays(-7),
    PeriodTo: DateTime.UtcNow,
    Page: 1,
    Limit: 1000));
```

`LogsPage` contains:
- parsed log entries
- page meta info when available
- current account context in `LogsPage.Account`

### Revealed Row Values

Some log rows end with an eye toggle that reveals extra content on the page — most commonly
under the `item_market` filter, where the visible text ends with `Готовый сет:` and the toggle
reveals the contents of the set. Those values are parsed into `LogEntry.RevealedValues`:

```csharp
foreach (var entry in result.Entries)
{
    foreach (var value in entry.RevealedValues)
    {
        Console.WriteLine($"{value.Label}: {value.Text}");
    }
}
```

`LogRevealedValue` carries:
- `Label` — the block caption taken from `data-title`, for example `Значение строки 1`
- `Text` — the revealed content with its line structure preserved, so each item of a set stays on its own line
- `Html` — the raw markup of the block

`LogEntry.Text` holds only what the page actually shows and therefore excludes revealed
content; `LogEntry.Html` still contains the complete cell markup.

### Get Current Account

```csharp
var account = await client.GetCurrentAccountAsync();
```

`LogsAccount` contains:
- `Nickname`
- `Badges`
- `AvailableServers`

This data is extracted from the shared page layout, so a host application does not need to scrape the navbar separately.

### Get Filters Catalog

```csharp
var catalog = await client.GetLogsFilterCatalogAsync();
```

`LogsFilterCatalog` contains:
- the current filter list from `type[]`
- the current account context in `LogsFilterCatalog.Account`
- per-filter `AdditionalParameters`

`AdditionalParameters` are the dynamic fields from the logs page, for example `dynamic[123]`.

Example:

```csharp
var catalog = await client.GetLogsFilterCatalogAsync();

foreach (var filter in catalog.Filters)
{
    Console.WriteLine($"{filter.Code} -> {filter.Name}");

    foreach (var parameter in filter.AdditionalParameters)
    {
        Console.WriteLine($"  {parameter.QueryKey} | {parameter.Label}");
    }
}
```

### Get Admin Activity

```csharp
var activity = await client.GetAdminActivityAsync(
    new AdminActivityQuery(
        PeriodFrom: DateTime.UtcNow.AddDays(-7),
        PeriodTo: DateTime.UtcNow));
```

### Get Top Operations

```csharp
var top = await client.GetTopOperationsAsync(
    new TopOperationsQuery(
        Filter: "bank",
        Date: DateTime.UtcNow.Date));
```

## Low-Level API

### Build Request URI

```csharp
var uri = LogsRequestUriBuilder.BuildLogsUri(new LogsQuery(
    ServerId: 201,
    Player: "Some_Nick"));
```

### Parse Raw HTML

```csharp
string html = await File.ReadAllTextAsync("logs.html");
var parsed = LogsHtmlParser.ParseLogs(html);
```

Useful when:
- HTTP is handled outside the library
- another service is responsible for authorization
- parsing should be tested separately from transport

## Models

Request models:
- `LogsQuery`
- `AdminActivityQuery`
- `TopOperationsQuery`

Transport and configuration models:
- `ParserRequest`
- `ParserCookie`
- `LogsParserCredentials`
- `LogsParserHttpOptions`
- `LogsParserRegistrationOptions`

Response models:
- `LogsPage`
- `LogEntry`
- `LogParticipant` — per-participant data (Money, Bank, Donate, AdditionalInfo, LastIp, RegistrationIp)
- `LogAdditionalInfo` — extended account data (AccountId, VC, SubAccount1–6, Deposit, AdminLevel)
- `LogRevealedValue` — content behind an eye toggle (Label, Text, Html)
- `LogPageMetaInfo`
- `LogsAccount`
- `LogsAccountBadge`
- `LogsAccountServer`
- `LogsFilterCatalog`
- `LogsFilterDefinition`
- `LogsFilterAdditionalParameter`
- `AdminActivityReport`
- `TopOperationsReport`

Each `LogEntry` contains `Sender` (I) and `Target` (II) as `LogParticipant?`. When a log record involves two participants, both are populated with their own financial data, additional info, and IP addresses. Each participant is matched to the additional-info block that follows its own `I:` / `II:` marker, so a record that only has a target never has its data attributed to the sender.

All public models are immutable `record` types.

## Logging

The library uses `Microsoft.Extensions.Logging` through an internal `LogsParserLogging` facade with thread-safe logger caching.

### DI Mode

Logging is configured automatically when `ILoggerFactory` is registered in the DI container:

```csharp
services.AddLogging(builder => builder.AddConsole());
services.AddLogsParser(options => { /* ... */ });
```

### Manual Mode

```csharp
using LogsParser.Diagnostics;

LogsParserLogging.UseLoggerFactory(myLoggerFactory);
```

### Log Levels

| Level | What is logged |
|---|---|
| `Trace` | Cookie operations, CSRF token extraction, individual HTTP requests, rate limit changes |
| `Debug` | API call parameters, HTML content sizes, parsing summaries, auth flow steps |
| `Information` | API call results (entry counts), auth flow start/completion, React challenge bypass |
| `Warning` | Rate limit exceeded, transient retries, TOTP rejection, missing account info |
| `Error` | HTTP failures, account configuration errors, unparseable challenge pages |

If no logger is configured, the library safely falls back to `NullLogger`.

## Exceptions

Base exception:
- `LogsParserException`

Parsing:
- `HtmlParsingException`

HTTP and authentication:
- `LogsParserHttpException`
- `AuthenticationRequiredException`
- `AuthenticationFailedException`
- `TwoFactorAuthenticationException`
- `CsrfTokenNotFoundException`
- `AccountConfigurationException`
- `ReactShieldBypassException`
- `RateLimitExceededException`

Example:

```csharp
try
{
    var logs = await client.GetLogsAsync(new LogsQuery(ServerId: 201));
}
catch (AuthenticationRequiredException)
{
    // credentials are missing
}
catch (AuthenticationFailedException)
{
    // login or password is invalid
}
catch (TwoFactorAuthenticationException)
{
    // TOTP or 2FA flow failed
}
catch (RateLimitExceededException ex)
{
    Console.WriteLine($"Retry after: {ex.RetryAfterSeconds}s");

    if (ex.ResetAt is { } resetAt)
    {
        Console.WriteLine($"Limit resets at: {resetAt:u}");
    }
}
catch (LogsParserException ex)
{
    Console.WriteLine(ex.Message);
}
```

## DI Registration

```csharp
services.AddLogsParser(options =>
{
    options.Credentials = new LogsParserCredentials("login", "password", "secret");
    options.CookieStorageFactory = _ => new MemoryCookieStorage();
    options.HttpOptions = new LogsParserHttpOptions
    {
        MaxRetryAttempts = 5,
        WaitForRateLimitReset = true
    };
});
```

You can also replace the transport completely:

```csharp
services.AddLogsParser(options =>
{
    options.DataSourceFactory = provider => new MyCustomLogsParserDataSource();
});
```

## Build

```powershell
$env:DOTNET_CLI_HOME='C:\Users\boss\source\repos\LogTools\.dotnet'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1'
dotnet build
```

## License

This project is licensed under `CC BY-NC 4.0`.
Commercial use is not permitted.
See [LICENSE](LICENSE).

## Notes

- The library is intended for integration scenarios first.
- It can be used both with and without DI.
- Cookie persistence remains the caller's responsibility.
- The transport layer is reusable, but parsing can also be used independently.
