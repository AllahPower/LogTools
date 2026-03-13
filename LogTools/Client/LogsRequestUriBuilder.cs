using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using LogsParser.Models;

namespace LogsParser;

public static partial class LogsRequestUriBuilder
{
    private const int DefaultPage = 1;
    private const int DefaultLimit = 1000;

    private static readonly HashSet<int> SupportedLimits =
    [
        100,
        500,
        1000
    ];

    private static readonly HashSet<string> ReservedParameterKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "server_number",
        "sort",
        "limit",
        "page",
        "type[]",
        "type",
        "min_period",
        "max_period",
        "player",
        "target",
        "ip"
    };

    public static string BuildLogsUri(LogsQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.ServerId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(query.ServerId), "ServerId must be greater than zero.");
        }

        var queryParts = new List<string>
        {
            $"server_number={query.ServerId}",
            $"sort={Encode(query.Sort)}",
            $"limit={NormalizeLimit(query.Limit)}",
            $"page={NormalizePage(query.Page)}"
        };

        if (query.Filters is not null)
        {
            foreach (var filter in query.Filters.Where(static value => !string.IsNullOrWhiteSpace(value)))
            {
                queryParts.Add($"type%5B%5D={Encode(filter)}");
            }
        }

        AppendDateTime(queryParts, "min_period", query.PeriodFrom);
        AppendDateTime(queryParts, "max_period", query.PeriodTo);
        AppendString(queryParts, "player", query.Player);
        AppendString(queryParts, "target", query.Target);
        AppendIpAddress(queryParts, "ip", query.IpAddress);

        if (query.AdditionalParameters is not null)
        {
            foreach (var pair in query.AdditionalParameters)
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value))
                {
                    continue;
                }

                ValidateAdditionalParameterKey(pair.Key);
                queryParts.Add($"{Encode(pair.Key)}={Encode(pair.Value)}");
            }
        }

        return $"?{string.Join("&", queryParts)}";
    }

    public static string BuildAdminActivityUri(AdminActivityQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var queryParts = new List<string>();
        AppendDateTime(queryParts, "min_period", query.PeriodFrom);
        AppendDateTime(queryParts, "max_period", query.PeriodTo);
        return $"/admins?{string.Join("&", queryParts)}";
    }

    public static string BuildTopOperationsUri(TopOperationsQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var queryParts = new List<string>();
        AppendString(queryParts, "type", query.Filter);
        AppendDateTime(queryParts, "date", query.Date);
        return $"top?{string.Join("&", queryParts)}";
    }

    private static void AppendString(ICollection<string> target, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            target.Add($"{Encode(key)}={Encode(value)}");
        }
    }

    private static void AppendDateTime(ICollection<string> target, string key, DateTime? value)
    {
        if (value.HasValue)
        {
            target.Add($"{Encode(key)}={Encode(value.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))}");
        }
    }

    private static void AppendIpAddress(ICollection<string> target, string key, IPAddress? value)
    {
        if (value is not null)
        {
            target.Add($"{Encode(key)}={Encode(value.ToString())}");
        }
    }

    private static int NormalizeLimit(int value)
    {
        if (SupportedLimits.Contains(value))
        {
            return value;
        }

        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "limit must be greater than zero.");
        }

        if (value < 100)
        {
            return 100;
        }

        if (value < 500)
        {
            return 100;
        }

        if (value < 1000)
        {
            return 500;
        }

        return DefaultLimit;
    }

    private static int NormalizePage(int value)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "page must be greater than zero.");
        }

        return value < DefaultPage ? DefaultPage : value;
    }

    private static string Encode(string value)
    {
        return Uri.EscapeDataString(value);
    }

    private static void ValidateAdditionalParameterKey(string key)
    {
        if (ReservedParameterKeys.Contains(key))
        {
            throw new ArgumentException(
                $"Additional parameter '{key}' conflicts with a reserved logs query parameter.",
                nameof(key));
        }

        if (!DynamicParameterKeyRegex().IsMatch(key))
        {
            throw new ArgumentException(
                $"Additional parameter '{key}' is invalid. Only dynamic[n] parameters from the logs page are supported.",
                nameof(key));
        }
    }

    [GeneratedRegex(@"^dynamic\[\d+\]$", RegexOptions.IgnoreCase)]
    private static partial Regex DynamicParameterKeyRegex();
}
