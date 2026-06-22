using Microsoft.Extensions.Configuration;

namespace LogsParser.Tests;

/// <summary>
/// Configuration source for the env-gated live tests. Reads from .NET User Secrets (stored in the
/// user profile, never in the repo) with a fallback to environment variables, so credentials work
/// whether tests run from Visual Studio Test Explorer or the CLI.
/// </summary>
internal static class TestConfiguration
{
    private static readonly IConfigurationRoot Configuration = new ConfigurationBuilder()
        .AddUserSecrets(typeof(TestConfiguration).Assembly, optional: true)
        .AddEnvironmentVariables()
        .Build();

    /// <summary>Returns the first non-empty value among the given configuration keys.</summary>
    public static string? Get(params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = Configuration[key];
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }
}
