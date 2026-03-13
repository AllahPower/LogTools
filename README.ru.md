<div align="center">

<img src="logo.png" alt="LogsParser logo" width="180" height="180" />

# LogsParser

[![English](https://img.shields.io/badge/EN-🇺🇸-informational)](README.md)
[![Русский](https://img.shields.io/badge/RU-🇷🇺-informational)](README.ru.md)

</div>

`LogsParser` это библиотека на .NET 10 для работы с `arizonarp.logsparser.info`.

Библиотека изначально проектировалась как переиспользуемый integration layer:
- парсинг логов, активности администрации и top-операций
- авторизация по логину, паролю и TOTP 2FA
- автоматический обход React anti-DDoS challenge
- получение информации о текущем аккаунте из layout страницы
- получение текущего каталога фильтров и `dynamic[n]` дополнительных параметров
- полное управление cookies на стороне вызывающего кода
- работа как вручную, так и через DI

## Возможности

- `LogsParserClient` для high-level API
- `LogsParserHttpDataSource` для HTTP транспорта
- `LogsHtmlParser` для парсинга сырого HTML
- `LogsRequestUriBuilder` для low-level генерации URI
- `ICookieStorage` для внешнего хранения cookies
- автоматический login flow
- поддержка TOTP 2FA
- автоматический bypass React challenge
- retry и обработка rate limit
- расширения для DI
- интеграция с `Microsoft.Extensions.Logging`
- доменные исключения

## Целевая платформа

- `.NET 10`
- C# `latest`

## Структура проекта

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

Основные точки входа:
- `LogsParserClient`
- `LogsParserHttpDataSource`
- `LogsHtmlParser`
- `LogsParserServiceCollectionExtensions`

## Подключение

Через `ProjectReference`:

```xml
<ItemGroup>
  <ProjectReference Include="..\LogTools\LogTools.csproj" />
</ItemGroup>
```

Позже тот же публичный API можно упаковать и использовать как NuGet-пакет.

## Быстрый старт

### Ручной режим

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

### Режим через DI

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

## Авторизация

`LogsParserHttpDataSource` умеет автоматически проходить полный auth flow:

1. открыть `/login`
2. извлечь CSRF token
3. отправить логин и пароль
4. открыть `/authenticator`
5. извлечь CSRF token
6. сгенерировать TOTP код
7. отправить 2FA код
8. повторить исходный запрос

Модель учётных данных:

```csharp
new LogsParserCredentials(
    Login: "my_login",
    Password: "my_password",
    TotpSecret: "BASE32SECRET");
```

### Как получить `TotpSecret`

Если аккаунт защищён TOTP и у вас нет сырого секрета, практический сценарий такой:

1. экспортировать профиль / migration payload из приложения-аутентификатора
2. декодировать экспортированные данные
3. использовать полученный секрет как `TotpSecret`

Рекомендуемая утилита:
- `otpauth-migration-decoder`: https://github.com/digitalduke/otpauth-migration-decoder

Библиотека сама не извлекает секрет. Она принимает уже готовую Base32-строку секрета.

Если credentials не заданы, а сервис делает redirect на `/login`, библиотека выбросит `AuthenticationRequiredException`.

## React Challenge Bypass

В транспортном слое реализован автоматический обход anti-DDoS защиты:
- детект challenge-страницы
- извлечение payload из скрипта страницы
- вычисление токена
- установка cookie `R3ACTLB`
- повтор исходного запроса

Эта логика является внутренней частью `LogsParserHttpDataSource`.

## Хранение Cookies

Библиотека не навязывает собственный формат хранения cookies.

Используется контракт:

```csharp
public interface ICookieStorage
{
    IReadOnlyCollection<ParserCookie> GetCookies();
    void SetCookies(IReadOnlyCollection<ParserCookie> cookies);
}
```

Стандартная реализация:
- `MemoryCookieStorage`

Если cookies должны переживать перезапуск процесса, можно реализовать своё хранилище:
- файл
- база данных
- зашифрованное локальное хранилище
- distributed cache

## High-Level API

### Получить Логи

```csharp
var result = await client.GetLogsAsync(new LogsQuery(
    ServerId: 201,
    Filters: ["warn", "mute"],
    PeriodFrom: DateTime.UtcNow.AddDays(-7),
    PeriodTo: DateTime.UtcNow,
    Page: 1,
    Limit: 1000));
```

`LogsPage` содержит:
- распарсенные записи логов
- мета-информацию страницы, если она присутствует
- текущий контекст аккаунта в `LogsPage.Account`

### Получить Текущий Аккаунт

```csharp
var account = await client.GetCurrentAccountAsync();
```

`LogsAccount` содержит:
- `Nickname`
- `Badges`
- `AvailableServers`

Эти данные извлекаются из общего layout страницы, поэтому хост-приложению не нужно отдельно парсить navbar.

### Получить Каталог Фильтров

```csharp
var catalog = await client.GetLogsFilterCatalogAsync();
```

`LogsFilterCatalog` содержит:
- текущий список фильтров из `type[]`
- текущий контекст аккаунта в `LogsFilterCatalog.Account`
- `AdditionalParameters` для каждого фильтра

`AdditionalParameters` это динамические поля страницы логов, например `dynamic[123]`.

Пример:

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

### Получить Активность Администрации

```csharp
var activity = await client.GetAdminActivityAsync(
    new AdminActivityQuery(
        PeriodFrom: DateTime.UtcNow.AddDays(-7),
        PeriodTo: DateTime.UtcNow));
```

### Получить Top Операций

```csharp
var top = await client.GetTopOperationsAsync(
    new TopOperationsQuery(
        Filter: "bank",
        Date: DateTime.UtcNow.Date));
```

## Low-Level API

### Построить URI Запроса

```csharp
var uri = LogsRequestUriBuilder.BuildLogsUri(new LogsQuery(
    ServerId: 201,
    Player: "Some_Nick"));
```

### Парсинг Сырого HTML

```csharp
string html = await File.ReadAllTextAsync("logs.html");
var parsed = LogsHtmlParser.ParseLogs(html);
```

Полезно, если:
- HTTP реализован вне библиотеки
- авторизацией занимается другой сервис
- нужно тестировать парсинг отдельно от транспорта

## Модели

Модели запросов:
- `LogsQuery`
- `AdminActivityQuery`
- `TopOperationsQuery`

Транспорт и конфигурация:
- `ParserRequest`
- `ParserCookie`
- `LogsParserCredentials`
- `LogsParserHttpOptions`
- `LogsParserRegistrationOptions`

Модели ответов:
- `LogsPage`
- `LogEntry`
- `LogsAccount`
- `LogsAccountBadge`
- `LogsAccountServer`
- `LogsFilterCatalog`
- `LogsFilterDefinition`
- `LogsFilterAdditionalParameter`
- `AdminActivityReport`
- `TopOperationsReport`

Все публичные модели это неизменяемые `record`-типы.

## Логирование

Библиотека использует `Microsoft.Extensions.Logging`.

Это означает:
- хост может направлять логи в `Serilog`, `NLog` или любой другой provider
- библиотека не требует прямой зависимости на `Serilog` в каждом модуле
- если logger не настроен, используется безопасный fallback на `NullLogger`

## Исключения

Базовое исключение:
- `LogsParserException`

Парсинг:
- `HtmlParsingException`

HTTP и авторизация:
- `LogsParserHttpException`
- `AuthenticationRequiredException`
- `AuthenticationFailedException`
- `TwoFactorAuthenticationException`
- `CsrfTokenNotFoundException`
- `AccountConfigurationException`
- `ReactShieldBypassException`
- `RateLimitExceededException`

Пример:

```csharp
try
{
    var logs = await client.GetLogsAsync(new LogsQuery(ServerId: 201));
}
catch (AuthenticationRequiredException)
{
    // не заданы credentials
}
catch (AuthenticationFailedException)
{
    // неверный логин или пароль
}
catch (TwoFactorAuthenticationException)
{
    // ошибка TOTP или 2FA flow
}
catch (RateLimitExceededException ex)
{
    Console.WriteLine($"Retry after: {ex.RetryAfterSeconds}s");
}
catch (LogsParserException ex)
{
    Console.WriteLine(ex.Message);
}
```

## Регистрация В DI

```csharp
services.AddLogsParser(options =>
{
    options.Credentials = new LogsParserCredentials("login", "password", "secret");
    options.CookieStorageFactory = _ => new MemoryCookieStorage();
    options.HttpOptions = new LogsParserHttpOptions
    {
        MaxRetryAttempts = 5
    };
});
```

Можно полностью заменить транспорт:

```csharp
services.AddLogsParser(options =>
{
    options.DataSourceFactory = provider => new MyCustomLogsParserDataSource();
});
```

## Сборка

```powershell
$env:DOTNET_CLI_HOME='C:\Users\boss\source\repos\LogTools\.dotnet'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1'
dotnet build
```

## Лицензия

Проект распространяется по лицензии `CC BY-NC 4.0`.
Коммерческое использование не допускается.
См. [LICENSE](LICENSE).

## Примечания

- Библиотека ориентирована в первую очередь на интеграционные сценарии.
- Её можно использовать как с DI, так и без DI.
- Ответственность за persistence cookies остаётся на стороне вызывающего кода.
- Транспортный слой переиспользуемый, но парсинг можно применять и отдельно.
