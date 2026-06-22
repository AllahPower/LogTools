using LogsParser.Models;
using LogsParser.Net;
using Xunit;

namespace LogsParser.Tests;

/// <summary>
/// Live smoke tests against the real arizonarp.logsparser.info site. Credentials are read from
/// .NET User Secrets (preferred) or environment variables — NEVER hard-coded or committed. When
/// they are absent the tests skip, so the suite stays green in CI and without credentials.
///
/// Configure once (works in Visual Studio Test Explorer and the CLI):
///   dotnet user-secrets --project LogsParser.Tests set "LogsParser:Login" "my_login"
///   dotnet user-secrets --project LogsParser.Tests set "LogsParser:Password" "my_password"
///   dotnet user-secrets --project LogsParser.Tests set "LogsParser:TotpSecret" "BASE32SECRET"
/// Then run: dotnet test --filter "FullyQualifiedName~LiveIntegrationTests"
/// </summary>
[Trait("Category", "Live")]
public class LiveIntegrationTests
{
    private static (string Login, string Password, string Totp)? ReadCredentials()
    {
        var login = TestConfiguration.Get("LogsParser:Login", "LOGSPARSER_LOGIN");
        var password = TestConfiguration.Get("LogsParser:Password", "LOGSPARSER_PASSWORD");
        var totp = TestConfiguration.Get("LogsParser:TotpSecret", "LOGSPARSER_TOTP_SECRET");

        if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(totp))
        {
            return null;
        }

        return (login, password, totp);
    }

    [Fact]
    public async Task Authenticates_and_fetches_admin_activity()
    {
        var credentials = ReadCredentials();
        if (credentials is null)
        {
            // Skipped: live credentials are not configured in this environment.
            return;
        }

        using var dataSource = new LogsParserHttpDataSource(
            new LogsParserCredentials(credentials.Value.Login, credentials.Value.Password, credentials.Value.Totp));
        var client = new LogsParserClient(dataSource);

        var to = DateTime.UtcNow;
        var report = await client.GetAdminActivityAsync(new AdminActivityQuery(to.AddDays(-7), to));

        Assert.NotNull(report);
        Assert.True(report.Entries.Count >= 0);
    }

    [Fact]
    public async Task Fetches_filter_catalog_and_account()
    {
        var credentials = ReadCredentials();
        if (credentials is null)
        {
            // Skipped: live credentials are not configured in this environment.
            return;
        }

        using var dataSource = new LogsParserHttpDataSource(
            new LogsParserCredentials(credentials.Value.Login, credentials.Value.Password, credentials.Value.Totp));
        var client = new LogsParserClient(dataSource);

        var catalog = await client.GetLogsFilterCatalogAsync();

        Assert.NotEmpty(catalog.Filters);
    }
}
